param([Parameter(Mandatory = $true)][string]$KspRoot)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $KspRoot 'KSP_x64_Data\Managed'
$cecil = Join-Path $managed 'Mono.Cecil.dll'
$game = Join-Path $managed 'Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $cecil) -or -not (Test-Path -LiteralPath $game)) {
    throw 'KSP_ROOT lacks the installed KSP managed assemblies.'
}
Add-Type -Path $cecil
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($game)
function Find-Type([string]$name) {
    $type = $assembly.MainModule.Types | Where-Object Name -EQ $name | Select-Object -First 1
    if ($null -eq $type) { throw "Missing KSP type $name" }
    return $type
}
function Require-Member([string]$typeName, [string]$kind, [string]$name, [string]$memberType) {
    $type = Find-Type $typeName
    $members = if ($kind -eq 'method') { $type.Methods } elseif ($kind -eq 'property') { $type.Properties } else { $type.Fields }
    $member = $members | Where-Object { $_.Name -eq $name -and $_.FullName.Contains($memberType) } | Select-Object -First 1
    $public = if ($null -eq $member) { $false } elseif ($kind -eq 'property') { $member.GetMethod -and $member.GetMethod.IsPublic } else { $member.IsPublic }
    if (-not $public) { throw "Missing public $typeName.$name $memberType" }
    Write-Output "PASS $($member.FullName)"
}
Require-Member PartResourceLibrary property Instance 'PartResourceLibrary'
Require-Member PartResourceLibrary method GetDefinition 'System.String'
Require-Member PartResourceDefinition property density 'System.Single'
Require-Member PartResourceDefinition property unitCost 'System.Single'
Require-Member Funding field Instance 'Funding'
Require-Member Funding property Funds 'System.Double'
Require-Member Funding method CanAfford 'System.Single'
Require-Member Funding method AddFunds 'System.Double,TransactionReasons'
Require-Member ConfigNode method AddValue 'System.String,System.Double'
Require-Member ConfigNode method TryGetValue 'System.String,System.Double&'

