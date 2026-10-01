<#
    Builds a mod: the shared library's sources plus the mod's own, compiled into ONE dll.

    A mod repo does not keep a copy of this script. It adds the library as a submodule and calls
    this file, so every mod is built the same way and there is only one place where the compiler
    flags live.

        # from a mod repo (see that repo's build.ps1 for the wrapper)
        ..\vendor\ScamWYF.Modding.Core\build.ps1 `
            -Project MyMod -Sources .\src -Refs "...\Newtonsoft.Json.dll"

        # just check the library still compiles
        .\build.ps1 -LibraryOnly

    No .NET SDK needed. Point -CscDll at a Roslyn csc.dll and it runs with `dotnet`; otherwise it
    looks for csc.exe from Visual Studio Build Tools.

      .\build.ps1 -CscDll C:\path\to\csc.dll -GameDir "C:\...\Scam With Your Friends"
#>
[CmdletBinding()]
param(
    # Assembly to produce. Ignored with -LibraryOnly.
    [string]$Project,

    # Extra source directories or files, on top of this library's src\.
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

    [switch]$NoCopy
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

# Shared references.
#
# mscorlib comes from the GAME, together with -nostdlib+. The compiler then sees exactly the API
# surface Unity actually shipped, so it is impossible to write code against a method that managed
# stripping removed, and that failure shows up at build time instead of at runtime. Keep this.
$common = @(
    (Join-Path $managed 'mscorlib.dll')
    (Join-Path $managed 'System.dll')
    (Join-Path $managed 'System.Core.dll')
    (Join-Path $managed 'UnityEngine.dll')
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
    (Join-Path $managed 'UnityEngine.IMGUIModule.dll')
    (Join-Path $managed 'Unity.InputSystem.dll')
    (Join-Path $core 'BepInEx.dll')
    (Join-Path $core '0Harmony.dll')
)

$references = @($common)
if ($Refs) { $references += $Refs }
foreach ($r in $references) { if (-not (Test-Path $r)) { throw "Missing reference assembly: $r" } }

$sourceFiles = @()
$sourceFiles += Get-ChildItem $libSrc -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
foreach ($extra in $Sources) {
    if (-not (Test-Path $extra)) { throw "No such source path: $extra" }
    $sourceFiles += Get-ChildItem $extra -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
}
if ($sourceFiles.Count -eq 0) { throw 'No .cs files found.' }

$assemblyName = if ($LibraryOnly) { 'ScamWYF.Modding.Core' } else { $Project }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
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

# The library on its own is not a plugin. Anything else goes where BepInEx will find it.
if ($LibraryOnly -or $NoCopy) { return }

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $outDll $plugins -Force
$pdb = [IO.Path]::ChangeExtension($outDll, '.pdb')
if (Test-Path $pdb) { Copy-Item $pdb $plugins -Force }
Write-Host "Installed $assemblyName to $plugins" -ForegroundColor Green