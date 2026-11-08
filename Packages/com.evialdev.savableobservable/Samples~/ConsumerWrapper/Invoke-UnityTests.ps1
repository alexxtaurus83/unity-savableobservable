<#
.SYNOPSIS
    Consumer project wrapper for Universal Unity Test Runner.
.DESCRIPTION
    Resolves com.evialdev.savableobservable Universal Unity Test Runner via:
    1) Packages/com.evialdev.savableobservable (embedded or local file: reference)
    2) Packages/packages-lock.json / Packages/manifest.json
    3) Library/PackageCache/com.evialdev.savableobservable*
    and forwards parameters to Invoke-UnityTestRunner.ps1.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [ValidateSet('EditMode', 'PlayMode', 'SelfTest', 'All')][string]$Suite = 'All',
    [string]$Filter = '',
    [string[]]$IncludeCategories = @(),
    [string[]]$ExcludeCategories = @(),
    [string[]]$ExcludeAssemblies = @(),
    [switch]$SkipMultiplayer,
    [switch]$ListOnly,
    [string]$SelfTestCommand = '',
    [string]$SelfTestMethod = '',
    [string]$OutputDirectory = '',
    [int]$TimeoutSeconds = 900,
    [string]$Label = '',
    [hashtable]$MockExecutor = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
} else {
    $ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
}

$packageName = "com.evialdev.savableobservable"
$runnerRel = "Tools\Invoke-UnityTestRunner.ps1"
$runnerPath = Join-Path $ProjectPath "Packages\$packageName\$runnerRel"

if (-not (Test-Path -LiteralPath $runnerPath)) {
    $manifestPath = Join-Path $ProjectPath "Packages\manifest.json"
    if (Test-Path -LiteralPath $manifestPath) {
        $content = Get-Content -LiteralPath $manifestPath -Raw
        if ($content -match "`"com\.evialdev\.savableobservable`"\s*:\s*`"file:(?<path>[^`"]+)`"") {
            $candidate = Join-Path (Join-Path $ProjectPath "Packages") $Matches['path']
            $candRunner = Join-Path $candidate $runnerRel
            if (Test-Path -LiteralPath $candRunner) { $runnerPath = [System.IO.Path]::GetFullPath($candRunner) }
        }
    }
}
if (-not (Test-Path -LiteralPath $runnerPath)) {
    $cache = Join-Path $ProjectPath "Library\PackageCache"
    if (Test-Path -LiteralPath $cache) {
        $matches = Get-ChildItem -LiteralPath $cache -Directory -Filter "$packageName*" -ErrorAction SilentlyContinue
        if ($matches.Count -gt 0) {
            $cand = Join-Path $matches[0].FullName $runnerRel
            if (Test-Path -LiteralPath $cand) { $runnerPath = $cand }
        }
    }
}

if (-not (Test-Path -LiteralPath $runnerPath)) {
    throw "Could not resolve $packageName test runner in $ProjectPath"
}

$effectiveExcludeCats = [System.Collections.Generic.List[string]]::new()
if ($ExcludeCategories.Length -gt 0) { $effectiveExcludeCats.AddRange($ExcludeCategories) }
if ($SkipMultiplayer -and -not $effectiveExcludeCats.Contains("Online")) { $effectiveExcludeCats.Add("Online") }

$params = @{
    ProjectPath = @($ProjectPath)
    Suite = $Suite
    Filter = $Filter
    IncludeCategories = $IncludeCategories
    ExcludeCategories = $effectiveExcludeCats.ToArray()
    ExcludeAssemblies = $ExcludeAssemblies
    ListOnly = [bool]$ListOnly
    SelfTestCommand = $SelfTestCommand
    SelfTestMethod = $SelfTestMethod
    OutputDirectory = $OutputDirectory
    TimeoutSeconds = $TimeoutSeconds
    Labels = if ([string]::IsNullOrWhiteSpace($Label)) { @((Split-Path $ProjectPath -Leaf)) } else { @($Label) }
}
if ($null -ne $MockExecutor) { $params['MockExecutor'] = $MockExecutor }

& $runnerPath @params
exit $LASTEXITCODE
