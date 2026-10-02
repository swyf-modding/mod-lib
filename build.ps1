<#
    Builds a mod against the shared library.

    The library is compiled ONCE, into its own dll, and every mod references that dll. It is not
    compiled into each mod's dll, and the reason is worth stating plainly because the alternative looks
    tempting and is wrong:

        Compiling the library into each mod gives every mod its own copy of every static field. Two
        mods means two Hotkey registries, two menus, two overlays. Each binds the menu hotkey, so one
        keypress opens two menus, and the "one shared menu, one registry, collisions reported across
        mods" arrangement the library exists to provide does not exist at all - each mod only ever sees
        itself.

    A single referenced dll has one set of statics, so the shared services are genuinely shared.

    A mod repo does not keep a copy of this script. It adds the library as a submodule and calls this
    file, so every mod is built the same way and there is only one place where the compiler flags live.

        # from a mod repo (see that repo's build.ps1 for the wrapper)
        ..\vendor\ScamWYF.Modding.Core\build.ps1 `
            -Project MyMod -Sources .\src -Refs "...\Newtonsoft.Json.dll"

        # build just the library dll
        .\build.ps1 -LibraryOnly

    The library dll is installed into BepInEx\core, next to BepInEx.dll and 0Harmony.dll, which is
    where BepInEx already keeps the assemblies every plugin may rely on. It is deliberately NOT put in
    BepInEx\plugins: that folder is for plugins, and the chainloader would try to load it as one.

    No .NET SDK needed. Point -CscDll at a Roslyn csc.dll and it runs with `dotnet`; otherwise it
    looks for csc.exe from Visual Studio Build Tools.

      .\build.ps1 -CscDll C:\path\to\csc.dll -GameDir "C:\...\Scam With Your Friends"
#>
[CmdletBinding()]
param(
    # Assembly to produce. Ignored with -LibraryOnly.
    [string]$Project,

    # Source directories or files for the mod itself.
    [string[]]$Sources,

    # Extra reference assemblies, on top of the shared list below.
    [string[]]$Refs,

    # Where the dll is written. Default: .\bin next to this script.
    [string]$OutDir,

    # Game install. Auto-detected when omitted; see Resolve-GameDir.
    [string]$GameDir,

    # Roslyn csc.dll, run via `dotnet`. Falls back to csc.exe from VS Build Tools.
    [string]$CscDll,

    # Compile the library on its own and do not install anything.
    [switch]$LibraryOnly,

    [switch]$NoCopy,

    # Copy the library into the mod's own output directory as well as BepInEx\core. Only useful when
    # something outside BepInEx needs to load the mod; normally the shared copy in BepInEx\core is
    # the right one, and two copies of the library is the bug this script exists to avoid.
    [switch]$EmbedLibrary,

    # Run the ConfigWatcher tests against the library sources. Off by default: they need the .NET SDK
    # rather than just Roslyn, and a mod repo has no reason to pay for them on every build.
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

<#
    Finds the game install: an explicit path, then SWYG_GAME_DIR, then the usual Steam locations.
    A candidate only counts if it has both the game's Managed folder and a BepInEx install, since
    without both there is nothing to compile against.
#>
function Resolve-GameDir {
    param([string]$Explicit)

    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    if ($env:SWYG_GAME_DIR) { $candidates += $env:SWYG_GAME_DIR }

    $names = @('Scam With Your Friends', 'Scam With Your Friends Playtest')
    $roots = @(
        "${env:ProgramFiles(x86)}\Steam\steamapps\common"
        "${env:ProgramFiles}\Steam\steamapps\common"
        "${env:HOME}\.local\share\Steam\steamapps\common"
        "${env:HOME}\.steam\steam\steamapps\common"
    )
    foreach ($root in $roots) {
        foreach ($name in $names) { $candidates += (Join-Path $root $name) }
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $managedOk = Test-Path (Join-Path $candidate 'Scam With Your Friends_Data\Managed\mscorlib.dll')
        $bepOk     = Test-Path (Join-Path $candidate 'BepInEx\core\BepInEx.dll')
        if ($managedOk -and $bepOk) { return $candidate }
    }

    $hints = ($candidates | Where-Object { $_ } | Select-Object -Unique) -join "`n  "
    throw @"
Could not find the game install. It needs 'Scam With Your Friends_Data\Managed' and BepInEx\core.
Pass -GameDir, or set SWYG_GAME_DIR.

Looked in:
  $hints
"@
}

<#
    Roslyn, from wherever this machine happens to keep it: an explicit csc.dll, VS Build Tools, or
    the copy inside a .NET SDK.

    Two things worth knowing here. The searches are globs at a known depth rather than a -Recurse
    over Program Files or the whole SDK, because a build script should not spend its time walking a
    filesystem. And the compiler path is parked in a script-scoped variable rather than captured in
    the scriptblock's closure: a PowerShell scriptblock resolves variables where it is *called*, so
    a function-local path would be empty by the time the compiler actually runs.
#>
function Resolve-Csc {
    param([string]$CscDllPath)

    if ($script:CscCmd) { return $script:CscCmd }

    if ($CscDllPath) {
        $script:CscPath = $CscDllPath
        $script:CscCmd = { param($compilerArgs) & dotnet $script:CscPath @compilerArgs }
        return $script:CscCmd
    }

    foreach ($root in @("${env:ProgramFiles(x86)}\Microsoft Visual Studio",
                        "${env:ProgramFiles}\Microsoft Visual Studio")) {
        if (-not (Test-Path $root)) { continue }

        $exe = Get-ChildItem -Path (Join-Path $root '*\MSBuild\Current\Bin\Roslyn\csc.exe') `
            -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $exe) {
            $exe = Get-ChildItem -Path $root -Filter csc.exe -Recurse -Depth 6 -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1
        }
        if ($exe) {
            $script:CscPath = $exe.FullName
            $script:CscCmd = { param($compilerArgs) & $script:CscPath @compilerArgs }
            return $script:CscCmd
        }
    }

    foreach ($root in @("${env:ProgramFiles}\dotnet\sdk", "$env:USERPROFILE\.dotnet\sdk",
                        '/usr/lib/dotnet/sdk', '/usr/share/dotnet/sdk')) {
        if (-not (Test-Path $root)) { continue }

        $dll = Get-ChildItem -Path (Join-Path $root '*\Roslyn\bincore\csc.dll') `
            -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($dll) {
            $script:CscPath = $dll.FullName
            $script:CscCmd = { param($compilerArgs) & dotnet $script:CscPath @compilerArgs }
            return $script:CscCmd
        }
    }

    throw "No compiler found. Pass -CscDll <path to Roslyn csc.dll>, or install VS Build Tools / the .NET SDK."
}

<#
    Runs one .NET SDK test project in a directory, reporting its name and failing the build on a failed run.
#>
function Invoke-TestProject {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Dir,
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$Dotnet
    )

    Write-Host ""
    Write-Host "Running $Name tests..." -ForegroundColor Cyan

    Push-Location $Dir
    try {
        & $Dotnet run --project $Project
        if ($LASTEXITCODE -ne 0) { throw "$Name tests failed (exit $LASTEXITCODE)" }
    }
    finally {
        Pop-Location
    }
}

<#
    Runs the tests: an API surface compile check plus the behaviour tests.

    The behaviour tests cover the parts of the library with logic worth asserting outside the game - file
    polling and waiting for a write to settle in ConfigWatcher, and the window size and position clamping
    in WindowGeometry. Everything else is either a Unity call or a thin wrapper whose correctness the
    compiler already checks, because the build compiles against the game's own assemblies rather than a
    reference copy.

    Each behaviour test compiles the real source - not a copy - against small stand-ins for the types it
    would otherwise need. BepInEx and Unity cannot be loaded here: both are built against the game's
    mscorlib. That constraint is why the interesting logic was kept in files with few dependencies.
#>
function Invoke-Tests {
    # First: compile the API surface against the built library, exactly as a mod would. tests\api is
    # checked against the real assembly rather than against the sources, so it also catches a mod
    # breaking on a member the library stopped exposing.
    Write-Host ""
    Write-Host "Checking the API surface..." -ForegroundColor Cyan

    & $PSScriptRoot\build.ps1 -Project ScamWYF.Modding.ApiCheck `
                              -Sources (Join-Path $PSScriptRoot 'tests\api') `
                              -OutDir (Join-Path $OutDir 'apicheck') `
                              -GameDir $GameDir -CscDll $CscDll -NoCopy
    if ($LASTEXITCODE -ne 0) { throw "the API surface check failed to compile" }

    # Second: the behaviour tests. These need the .NET SDK rather than just Roslyn, so they are skipped
    # with a warning rather than failing the build on a machine that can still ship a mod.
    $tests = Join-Path $PSScriptRoot 'tests\watcher'
    if (-not (Test-Path $tests)) {
        Write-Host "No behaviour tests at $tests" -ForegroundColor Yellow
        return
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Host "Skipping behaviour tests: no dotnet on PATH (they need the SDK, not just Roslyn)." -ForegroundColor Yellow
        return
    }

    # The sources are copied in so each test project always compiles what is actually shipping.
    Copy-Item (Join-Path $PSScriptRoot 'src\config\ConfigWatcher.cs') `
              (Join-Path $tests 'ConfigWatcher.cs') -Force

    Invoke-TestProject -Name 'ConfigWatcher' -Dir $tests -Project 'WatcherTests.csproj' -Dotnet $dotnet.Source

    # The window geometry arithmetic. Same arrangement: the real source, no engine, .NET SDK only.
    $geometry = Join-Path $PSScriptRoot 'tests\window'
    if (Test-Path $geometry) {
        Copy-Item (Join-Path $PSScriptRoot 'src\ui\WindowGeometry.cs') `
                  (Join-Path $geometry 'WindowGeometry.cs') -Force

        Invoke-TestProject -Name 'WindowGeometry' -Dir $geometry `
                           -Project 'WindowGeometryTests.csproj' -Dotnet $dotnet.Source
    }
}

$libSrc = Join-Path $PSScriptRoot 'src'
if (-not (Test-Path $libSrc)) {
    throw "No library sources at $libSrc. If this is a submodule, run: git submodule update --init --recursive"
}

if (-not $LibraryOnly -and [string]::IsNullOrWhiteSpace($Project)) {
    throw "Pass -Project <AssemblyName>, or -LibraryOnly to build the library by itself."
}

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'bin' }

$GameDir = Resolve-GameDir $GameDir
$managed = Join-Path $GameDir 'Scam With Your Friends_Data\Managed'
$core    = Join-Path $GameDir 'BepInEx\core'
$plugins = Join-Path $GameDir 'BepInEx\plugins'

# Where the shared library is built, and where it ends up.
#
# BepInEx\core, not BepInEx\plugins. The chainloader loads everything in plugins as a plugin, and a
# library has no plugin in it; core is where BepInEx already keeps the assemblies plugins may share
# (BepInEx.dll, 0Harmony.dll), and they are loaded once, before any plugin runs.
$libOut  = Join-Path $PSScriptRoot 'bin'
$libDll  = Join-Path $libOut 'ScamWYF.Modding.Core.dll'

# Shared references.
#
# mscorlib comes from the GAME, together with -nostdlib+. The compiler then sees exactly the API
# surface Unity actually shipped, so it is impossible to write code against a method that managed
# stripping removed, and that failure shows up at build time instead of at runtime. Keep this.
#
# The UI Toolkit modules are here because the shared UI is built on UI Toolkit - the same stack the base
# game uses for its own menus - so a mod's window inherits the game's theme and fonts. TextRendering
# comes along for TextAnchor and FontStyle, and TextCore for the font types PanelTextSettings needs.
$common = @(
    (Join-Path $managed 'mscorlib.dll')
    (Join-Path $managed 'System.dll')
    (Join-Path $managed 'System.Core.dll')
    (Join-Path $managed 'UnityEngine.dll')
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
    (Join-Path $managed 'UnityEngine.IMGUIModule.dll')
    (Join-Path $managed 'UnityEngine.UIElementsModule.dll')
    (Join-Path $managed 'UnityEngine.TextRenderingModule.dll')
    (Join-Path $managed 'UnityEngine.TextCoreTextEngineModule.dll')
    (Join-Path $managed 'UnityEngine.TextCoreFontEngineModule.dll')
    (Join-Path $managed 'Unity.InputSystem.dll')
    (Join-Path $core 'BepInEx.dll')
    (Join-Path $core '0Harmony.dll')
)

$references = @($common)
if ($Refs) { $references += $Refs }

$sourceFiles = @()
foreach ($extra in $Sources) {
    if (-not $extra) { continue }
    if (-not (Test-Path $extra)) { throw "No such source path: $extra" }
    $sourceFiles += Get-ChildItem $extra -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path $libOut | Out-Null

if ($LibraryOnly) {
    # The library builds from its own sources and nothing else.
    $sourceFiles = @(Get-ChildItem $libSrc -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
    if ($sourceFiles.Count -eq 0) { throw 'No library .cs files found.' }

    if ($Refs) { $references += $Refs }
    foreach ($r in $references) { if (-not (Test-Path $r)) { throw "Missing reference assembly: $r" } }
}
else {
    if ($sourceFiles.Count -eq 0) { throw 'No .cs files found. Pass -Sources.' }

    # Build the library first, so a mod can never be compiled against a stale version of it. This is
    # what makes "reference the library" safe: the reference is always to what was just built.
    Write-Host "Building the shared library first..." -ForegroundColor DarkGray
    & $PSScriptRoot\build.ps1 -LibraryOnly -OutDir $libOut -GameDir $GameDir -CscDll $CscDll -NoCopy
    if ($LASTEXITCODE -ne 0) { throw "the shared library failed to build (exit $LASTEXITCODE)" }

    if (-not (Test-Path $libDll)) { throw "The shared library did not produce $libDll" }
    $references += $libDll

    foreach ($r in $references) { if (-not (Test-Path $r)) { throw "Missing reference assembly: $r" } }
}

$assemblyName = if ($LibraryOnly) { 'ScamWYF.Modding.Core' } else { $Project }
$outDll = Join-Path $OutDir "$assemblyName.dll"

$cscArgs = @(
    '-target:library'
    "-out:$outDll"
    '-nostdlib+'
    '-noconfig'
    '-optimize+'
    '-langversion:7.3'
    '-warn:4'
    '-nologo'
    '-debug:portable'
)
$cscArgs += ($references | ForEach-Object { "-r:$_" })
$cscArgs += $sourceFiles

$csc = Resolve-Csc $CscDll

Write-Host "Game: $GameDir"
Write-Host "Output: $outDll"

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "$assemblyName failed to compile (exit $LASTEXITCODE)" }
Write-Host "Built $outDll" -ForegroundColor Green

if ($Test) { Invoke-Tests }

# The library on its own is not a plugin. Anything else goes where BepInEx will find it.
if ($LibraryOnly -or $NoCopy) { return }

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $outDll $plugins -Force
$pdb = [IO.Path]::ChangeExtension($outDll, '.pdb')
if (Test-Path $pdb) { Copy-Item $pdb $plugins -Force }
Write-Host "Installed $assemblyName to $plugins" -ForegroundColor Green

# The library goes to BepInEx\core, where BepInEx keeps the assemblies plugins share. Exactly one copy
# of it must be installed: two would mean two Hotkey registries and two menus, which is the whole
# problem this arrangement exists to solve.
$libPdb = [IO.Path]::ChangeExtension($libDll, '.pdb')
Copy-Item $libDll $core -Force
if (Test-Path $libPdb) { Copy-Item $libPdb $core -Force }
Write-Host "Installed ScamWYF.Modding.Core to $core" -ForegroundColor Green

if ($EmbedLibrary) {
    # Only for something that loads the mod outside BepInEx, where core is not on the probing path.
    Copy-Item $libDll $OutDir -Force
    if (Test-Path $libPdb) { Copy-Item $libPdb $OutDir -Force }
}

# A stale embedded copy would silently reintroduce the duplicate-state bug, so say so plainly if one
# is found next to a freshly built mod.
Get-ChildItem $OutDir -Filter 'ScamWYF.Modding.Core.dll' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($EmbedLibrary) { return }
    Write-Host ""
    Write-Warning ("A copy of ScamWYF.Modding.Core.dll is sitting in $OutDir. Do not deploy it: " +
                   "deploy only the mod dll alongside the one in BepInEx\core, or the mod gets its own " +
                   "copy of every shared service.")
}