<#
.SYNOPSIS
    SavableObservable Framework Wrapper for the Universal Unity Test Runner.
.DESCRIPTION
    Wraps Packages/com.evialdev.savableobservable/Tools/Invoke-UnityTestRunner.ps1
    defaulting ProjectPath to the Framework repository root and enabling mmvc_selftest.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = '',

    [ValidateSet('EditMode', 'PlayMode', 'SelfTest', 'All')]
    [string]$Suite = 'All',

    [string]$Filter = '',

    [string[]]$ExcludeAssemblies = @(),

    [string[]]$ExcludeCategories = @(),

    [string]$SelfTestCommand = 'mmvc_selftest --mode all',

    [string]$SelfTestMethod = 'SavableObservable.Editor.MmvcSelfTest.RunSelfTestBatch',

    [string]$OutputDirectory = '',

    [int]$TimeoutSeconds = 900,

    [string]$Label = 'SavableObservable',

    [hashtable]$MockExecutor = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    # Default to framework root (parent of Tools)
    $ProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
} else {
    $ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
}

# Resolve the shared Universal Test Runner module from Packages/com.evialdev.savableobservable/Tools
$packageRunner = Join-Path $ProjectPath "Packages\com.evialdev.savableobservable\Tools\Invoke-UnityTestRunner.ps1"
if (-not (Test-Path -LiteralPath $packageRunner)) {
    # Check direct relative path from PSScriptRoot
    $fallbackRunner = Join-Path $PSScriptRoot "..\Packages\com.evialdev.savableobservable\Tools\Invoke-UnityTestRunner.ps1"
    if (Test-Path -LiteralPath $fallbackRunner) {
        $packageRunner = [System.IO.Path]::GetFullPath($fallbackRunner)
    } else {
        throw "Could not resolve Universal Unity Test Runner at: $packageRunner"
    }
}

$runnerParams = @{
    ProjectPath = @($ProjectPath)
    Suite = $Suite
    Filter = $Filter
    ExcludeAssemblies = $ExcludeAssemblies
    ExcludeCategories = $ExcludeCategories
    SelfTestCommand = $SelfTestCommand
    SelfTestMethod = $SelfTestMethod
    OutputDirectory = $OutputDirectory
    TimeoutSeconds = $TimeoutSeconds
    Labels = @($Label)
}

if ($null -ne $MockExecutor) {
    $runnerParams['MockExecutor'] = $MockExecutor
}

& $packageRunner @runnerParams
exit $LASTEXITCODE
