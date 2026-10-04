param([Parameter(Mandatory = $true)][string]$KspRoot)

$ErrorActionPreference = 'Stop'
$cecil = Join-Path $KspRoot 'KSP_x64_Data\Managed\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecil)) { throw 'KSP_ROOT is missing Mono.Cecil.dll.' }
Add-Type -Path $cecil

function Check-Package([string]$path, [string[]]$expected) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Missing package: $path"
    }
    $files = @(Get-ChildItem -LiteralPath $path -Recurse -File |
        ForEach-Object { $_.FullName.Substring($path.Length + 1).Replace('\', '/') } |
        Sort-Object)
    $expected = @($expected | Sort-Object)
    if (($files -join '|') -ne ($expected -join '|')) {
        throw "Unexpected package contents at $path`: $($files -join ', ')"
    }
    foreach ($relative in $files | Where-Object { $_.EndsWith('.dll') }) {
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $path $relative))
        $references = @($assembly.MainModule.AssemblyReferences | ForEach-Object Name)
        if ($references -contains 'netstandard') {
            throw "$relative requires netstandard.dll, which KSP_x64 cannot resolve."
        }
        Write-Output "PASS $relative has no netstandard runtime reference"
    }
    Write-Output "PASS package contents: $path"
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Check-Package (Join-Path $root 'artifacts\GameData\Lodeworks') @(
    'Plugins/Lodeworks.dll', 'Plugins/Lodeworks.Sim.dll')
Check-Package (Join-Path $root 'artifacts\Phase1Harness\GameData\Lodeworks') @(
    'Parts/lwPhase1LockProbe.cfg', 'Parts/lwPhase1FuelDepotProbe.cfg',
    'Plugins/Lodeworks.dll',
    'Plugins/Lodeworks.Phase1Harness.dll', 'Plugins/Lodeworks.Sim.dll')

