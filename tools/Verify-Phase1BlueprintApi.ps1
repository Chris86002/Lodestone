param([Parameter(Mandatory = $true)][string]$KspRoot)

$ErrorActionPreference = 'Stop'
$managed = Join-Path $KspRoot 'KSP_x64_Data\Managed'
$cecil = Join-Path $managed 'Mono.Cecil.dll'
$game = Join-Path $managed 'Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $cecil) -or -not (Test-Path -LiteralPath $game)) {
    throw 'KSP_ROOT must contain KSP_x64_Data\Managed\Mono.Cecil.dll and Assembly-CSharp.dll.'
}
Add-Type -Path $cecil
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($game)

function Find-Type([string]$name) {
    $type = $assembly.MainModule.Types | Where-Object FullName -EQ $name | Select-Object -First 1
    if ($null -eq $type) { throw "Missing KSP type $name" }
    return $type
}

function Find-Method([string]$typeName, [string]$methodName) {
    $method = (Find-Type $typeName).Methods | Where-Object Name -EQ $methodName | Select-Object -First 1
    if ($null -eq $method) { throw "Missing KSP method $typeName.$methodName" }
    return $method
}

function Assert-Calls([string]$typeName, [string]$methodName, [string]$needle) {
    $method = Find-Method $typeName $methodName
    $match = $method.Body.Instructions | Where-Object { $_.ToString().Contains($needle) } | Select-Object -First 1
    if ($null -eq $match) { throw "$typeName.$methodName does not reference $needle" }
    Write-Output "PASS $typeName.$methodName -> $needle"
}

foreach ($name in @('AddExperimentalPart', 'RemoveExperimentalPart', 'IsExperimentalPart')) {
    $method = Find-Method 'ResearchAndDevelopment' $name
    if (-not $method.IsPublic) { throw "ResearchAndDevelopment.$name is not public" }
    Write-Output "PASS public ResearchAndDevelopment.$name"
}
Assert-Calls 'ResearchAndDevelopment' 'PartTechAvailable' 'experimentalPartsStock'
Assert-Calls 'ResearchAndDevelopment' 'PartModelPurchased' 'experimentalPartsStock'
Assert-Calls 'ResearchAndDevelopment' 'OnLoad' 'ExpParts'
Assert-Calls 'ResearchAndDevelopment' 'OnSave' 'ExpParts'
Assert-Calls 'EditorLogic' 'GetStockPreFlightCheck' 'ExperimentalPartsAvailable::.ctor'
Assert-Calls 'LaunchSiteFacility' 'launchChecks' 'ExperimentalPartsAvailable::.ctor'
Assert-Calls 'PreFlightTests.ExperimentalPartsAvailable' 'Test' 'IsExperimentalPart'
$proceed = Find-Method 'PreFlightTests.ExperimentalPartsAvailable' 'GetProceedOption'
$opcodes = @($proceed.Body.Instructions | ForEach-Object { $_.OpCode.Name })
if ($opcodes.Count -ne 2 -or $opcodes[0] -ne 'ldnull' -or $opcodes[1] -ne 'ret') {
    throw 'ExperimentalPartsAvailable has a non-null proceed option.'
}
Write-Output 'PASS ExperimentalPartsAvailable.GetProceedOption returns null'
