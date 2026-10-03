<#
    Compiles and runs BepInExVersionCheck against a built dll.

        .\test-bepinex-version.ps1 -Assembly bin\ScamWYF.ModHandler.dll

    build.ps1 calls this after every compile, so a mod whose [BepInPlugin] version BepInEx cannot parse
    fails the build rather than shipping. See BepInExVersionCheck.cs for why that failure is otherwise
    invisible: BepInEx logs it and loads nothing, and says so in no other place.

    -Cecil is passed in by build.ps1 because the copy in BepInEx\core is the one already resolved for the
    compile; this falls back to the same place build.ps1 would when run by hand.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [string]$Cecil,
    [string]$CscDll
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Assembly)) { throw "No such assembly: $Assembly" }

function Resolve-ToolCompiler {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }

    foreach ($root in @("${env:ProgramFiles}\dotnet\sdk", "$env:USERPROFILE\.dotnet\sdk")) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        $dll = Get-ChildItem -Path (Join-Path $root '*\Roslyn\bincore\csc.dll') -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($dll) { return $dll.FullName }
    }

    throw "No C# compiler found. Install the .NET SDK, or pass -CscDll."
}

if (-not $Cecil) {
    # Same order build.ps1 uses: a copy staged beside this script, then the game's BepInEx\core. Running
    # by hand from a mod repo, the staged path does not exist and the game's does.
    foreach ($root in @($PSScriptRoot, (Join-Path $PSScriptRoot '..'), (Join-Path $PSScriptRoot '..\..'))) {
        $candidate = Join-Path $root 'vendor\Mono.Cecil.dll'
        if (Test-Path $candidate) { $Cecil = (Resolve-Path $candidate).Path; break }
    }
}

if (-not $Cecil) {
    $game = $env:SWYG_GAME_DIR
    if (-not $game) { $game = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Scam With Your Friends Playtest" }
    $candidate = Join-Path $game 'BepInEx\core\Mono.Cecil.dll'
    if (Test-Path $candidate) { $Cecil = $candidate }
}

if (-not $Cecil -or -not (Test-Path $Cecil)) {
    throw "No Mono.Cecil.dll. build.ps1 passes the one from BepInEx\core; pass -Cecil when running this by hand."
}

$frameworkBase = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework'
$refRoot = Join-Path $frameworkBase 'v4.8'
if (-not (Test-Path $refRoot)) {
    $found = Get-ChildItem $frameworkBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v(\d+)\.(\d+)' } |
        Sort-Object { [version]$_.Name.TrimStart('v') } -Descending |
        Select-Object -First 1
    if ($found) { $refRoot = $found.FullName }
}

if (-not (Test-Path $refRoot)) { throw "No .NET Framework reference assemblies under $frameworkBase" }

$csc = Resolve-ToolCompiler $CscDll
$outDir = Join-Path $PSScriptRoot '..\obj\tools'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$exe = Join-Path $outDir 'BepInExVersionCheck.exe'

$references = @(
    (Join-Path $refRoot 'mscorlib.dll')
    $Cecil
)
foreach ($r in $references) { if (-not (Test-Path $r)) { throw "Missing reference: $r" } }

$args = @(
    '-target:exe'
    '-platform:anycpu'
    '-nostdlib+'
    '-langversion:7.3'
    '-optimize+'
    '-warn:4'
    '-main:ScamWYF.Modding.Tools.BepInExVersionCheck'
    "-out:$exe"
    ($references | ForEach-Object { "-reference:$_" })
    (Join-Path $PSScriptRoot 'BepInExVersionCheck.cs')
)

if ($csc -like '*.dll') { & dotnet $csc @args } else { & $csc @args }
if ($LASTEXITCODE -ne 0) { throw "BepInExVersionCheck failed to compile (exit $LASTEXITCODE)" }

# Beside the exe, or it compiles against Cecil fine and then fails to load it at run time.
Copy-Item $Cecil (Join-Path $outDir 'Mono.Cecil.dll') -Force

& $exe (Resolve-Path $Assembly).Path
exit $LASTEXITCODE