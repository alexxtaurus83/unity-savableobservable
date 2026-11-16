<#
.SYNOPSIS
    Universal Modular Unity Test Runner for local and CI workflows.
.DESCRIPTION
    Executes unattended EditMode and PlayMode tests against one or more Unity projects.
    Supports live Unity instances via Unity Pipeline (using MmvcUnattendedTestGuard if available)
    or headless batch mode (via unity run -runTests with raw UTF runner flags or unity test).
    Provides robust NUnit3 XML parsing, truthful execution metrics, and safe crash protection.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$ProjectPath,

    [ValidateSet('EditMode', 'PlayMode', 'SelfTest', 'All')]
    [string]$Suite = 'All',

    [string]$Filter = '',

    [string[]]$IncludeCategories = @(),

    [string[]]$ExcludeAssemblies = @(),

    [string[]]$ExcludeCategories = @(),

    [switch]$ListOnly,

    [string]$SelfTestCommand = '',

    [string]$SelfTestMethod = '',

    [string]$OutputDirectory = '',

    [int]$TimeoutSeconds = 900,

    [string[]]$Labels = @(),

    [hashtable]$MockExecutor = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$EXIT_SUCCESS = 0
$EXIT_INFRASTRUCTURE = 6
$EXIT_TEST_FAILURES = 8

# Flatten and parse ProjectPath entries (handles semicolon-separated strings and remaining arguments)
$flattenedProjectPaths = [System.Collections.Generic.List[string]]::new()
foreach ($p in $ProjectPath) {
    if (-not [string]::IsNullOrWhiteSpace($p)) {
        $parts = $p -split ';'
        foreach ($part in $parts) {
            $trimmed = $part.Trim()
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $flattenedProjectPaths.Add($trimmed)
            }
        }
    }
}

if ($flattenedProjectPaths.Count -eq 0) {
    throw "At least one non-empty ProjectPath must be provided."
}

# Normalize Output Directory
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
    $OutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "unity-test-reports\$timestamp"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
}

$summaryData = [ordered]@{
    StartTimeUtc = (Get-Date).ToUniversalTime().ToString("o")
    Suite = $Suite
    Filter = $Filter
    ExcludeAssemblies = @($ExcludeAssemblies)
    IncludeCategories = @($IncludeCategories)
    ExcludeCategories = @($ExcludeCategories)
    ListOnly = [bool]$ListOnly
    SelfTestCommand = $SelfTestCommand
    SelfTestMethod = $SelfTestMethod
    OutputDirectory = $OutputDirectory
    Projects = [System.Collections.Generic.List[hashtable]]::new()
    TotalDiscovered = 0
    TotalExecuted = 0
    TotalPassed = 0
    TotalFailed = 0
    TotalSkipped = 0
    TotalInconclusive = 0
    TotalNUnitDuration = 0.0
    TotalWallDurationSeconds = 0.0
    SlowestTests = [System.Collections.Generic.List[hashtable]]::new()
    CategoryTotals = [ordered]@{}
    Status = "SUCCESS"
    AutoSavedScenes = [System.Collections.Generic.List[string]]::new()
    Errors = [System.Collections.Generic.List[string]]::new()
}

$runnerTotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# Dot-source modular runner components from Tools/Runner
$runnerModulesDir = Join-Path $PSScriptRoot "Runner"
. (Join-Path $runnerModulesDir "Logging.ps1")
. (Join-Path $runnerModulesDir "Process.ps1")
. (Join-Path $runnerModulesDir "UnityCli.ps1")
. (Join-Path $runnerModulesDir "EditorStatus.ps1")
. (Join-Path $runnerModulesDir "NUnitParser.ps1")
. (Join-Path $runnerModulesDir "LiveSuite.ps1")
. (Join-Path $runnerModulesDir "BatchSuite.ps1")
. (Join-Path $runnerModulesDir "SelfTest.ps1")
. (Join-Path $runnerModulesDir "Report.ps1")
if ($ListOnly) {
    Write-RunnerLog "Executing -ListOnly discovery for Project(s)..." "INFO"
    $inventoryReport = [System.Collections.Generic.List[PSObject]]::new()
    $inventoryTests = [System.Collections.Generic.List[hashtable]]::new()
    $totalCount = 0

    foreach ($p in $flattenedProjectPaths) {
        $normPath = [System.IO.Path]::GetFullPath($p)
        $isLive = $false
        try {
            $probe = Test-ProjectEditorStatus -TargetProjectPath $normPath
            $isLive = ($probe.IsRunning -and $probe.Reachable -and $probe.State -eq 'ready')
        } catch {
            $isLive = $false
        }

        $includeCatStr = if ($IncludeCategories.Length -gt 0) { ($IncludeCategories -join ';') } else { '' }
        $excludeCatStr = if ($ExcludeCategories.Length -gt 0) { ($ExcludeCategories -join ';') } else { '' }

        if ($isLive) {
            # Preferred: per-case inventory (what a run reports as Summary.Total, incl. [TestCase]/[TestCaseSource]).
            $live = Get-LiveTestInventory -TargetProjectPath $normPath -SuiteName $Suite -TestFilter $Filter `
                -IncludeCategories $IncludeCategories -ExcludeCategories $ExcludeCategories -ExcludeAssemblies $ExcludeAssemblies
            if ($null -ne $live) {
                foreach ($f in $live['Fixtures']) {
                    $inventoryReport.Add([pscustomobject]$f)
                    $totalCount += [int]$f['testCount']
                }
                foreach ($t in $live['Tests']) { $inventoryTests.Add($t) }
            } else {
                # Fallback: reflection-based list. It counts declared [Test] methods only, so parameterized and
                # inherited tests are under-counted and it understands no '!' negation in -Filter.
                Write-RunnerLog "'test list' unavailable; falling back to mmvc_list_tests (method counts, not per-case counts)." "WARN"
                $listArgs = @("command", "mmvc_list_tests", "--mode", $Suite.ToLowerInvariant(), "--project-path", $normPath)
                if (-not [string]::IsNullOrWhiteSpace($Filter)) { $listArgs += @("--filter", $Filter) }
                if (-not [string]::IsNullOrWhiteSpace($includeCatStr)) { $listArgs += @("--include_categories", $includeCatStr) }
                if (-not [string]::IsNullOrWhiteSpace($excludeCatStr)) { $listArgs += @("--exclude_categories", $excludeCatStr) }

                $res = Invoke-UnityCliJson -CliArgs $listArgs -TimeoutSec 30
                $payload = Extract-CommandResultPayload -Envelope $res.Envelope
                if ($payload -is [hashtable] -and $payload.ContainsKey('fixtures')) {
                    foreach ($f in $payload['fixtures']) {
                        $inventoryReport.Add([pscustomobject]$f)
                        $totalCount += [int]$f['testCount']
                    }
                }
            }
        }

        if ($inventoryReport.Count -eq 0) {
            $staticFixtures = Discover-TestsStatic -TargetProjectPath $normPath -SuiteName $Suite -TestFilter $Filter -IncludeCategories $IncludeCategories -ExcludeCategories $ExcludeCategories
            foreach ($f in $staticFixtures) {
                $inventoryReport.Add([pscustomobject]$f)
                $totalCount += [int]$f['testCount']
            }
        }
    }

    Write-Host "`nDiscovered Tests Inventory ($totalCount total):" -ForegroundColor Cyan
    $inventoryReport | Format-Table -Property @(
        @{ Label = "Assembly"; Expression = { $_.assembly } },
        @{ Label = "Fixture"; Expression = { $_.fixture } },
        @{ Label = "Count"; Expression = { $_.testCount } },
        @{ Label = "Categories"; Expression = { if ($null -ne $_.categories) { ($_.categories -join ', ') } else { '' } } }
    ) | Out-String | Write-Host

    $inventoryFile = Join-Path $OutputDirectory "test-inventory.json"
    # 'Tests' is the per-case list (empty when only the reflection/static fallbacks were available).
    $invData = @{
        TotalTests = $totalCount
        Fixtures = $inventoryReport
        Tests = $inventoryTests
    }
    Set-Content -LiteralPath $inventoryFile -Value (ConvertTo-Json $invData -Depth 5) -Encoding utf8
    Write-RunnerLog "Inventory written to $inventoryFile" "INFO"
    exit $EXIT_SUCCESS
}

# -------------------------------------------------------------------------------------------------
# Main Routing & Execution Loop across ProjectPath[]
# -------------------------------------------------------------------------------------------------
$overallExitCode = $EXIT_SUCCESS

for ($i = 0; $i -lt $flattenedProjectPaths.Count; $i++) {
    $rawPath = $flattenedProjectPaths[$i]
    if ([string]::IsNullOrWhiteSpace($rawPath)) {
        continue
    }

    $normPath = [System.IO.Path]::GetFullPath($rawPath)
    $folderName = [System.IO.Path]::GetFileName($normPath.TrimEnd('\', '/'))
    $label = if ($i -lt $Labels.Length -and -not [string]::IsNullOrWhiteSpace($Labels[$i])) {
        $Labels[$i]
    } else {
        $folderName
    }

    # Comprehensive Unity project root validation: requires Assets, ProjectSettings, AND ProjectSettings/ProjectVersion.txt
    $assetsDir = Join-Path $normPath "Assets"
    $projectSettingsDir = Join-Path $normPath "ProjectSettings"
    $projectVersionFile = Join-Path $projectSettingsDir "ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $assetsDir) -or -not (Test-Path -LiteralPath $projectSettingsDir) -or -not (Test-Path -LiteralPath $projectVersionFile)) {
        $valErr = "Target path '$normPath' is not a valid Unity project (Assets directory, ProjectSettings directory, or ProjectVersion.txt missing)."
        Write-RunnerLog $valErr "ERROR"
        $summaryData.Errors.Add($valErr)
        $overallExitCode = [Math]::Max($overallExitCode, $EXIT_INFRASTRUCTURE)
        continue
    }

    Write-RunnerLog "=== Evaluating project: $label ($normPath) ===" "INFO"
    $projectSw = [System.Diagnostics.Stopwatch]::StartNew()

    $statusInfo = Test-ProjectEditorStatus -TargetProjectPath $normPath
    Write-RunnerLog "Editor probe for ${label}: Running=$($statusInfo['IsRunning']), Reachable=$($statusInfo['Reachable']), State=$($statusInfo['State']), Reason=$($statusInfo['Reason'])" "INFO"

    # CRITICAL POLICY: NEVER attempt batch mode if an Editor process is currently running on the project
    if ($statusInfo['IsRunning'] -and -not $statusInfo['Reachable']) {
        $blockErr = "Project '$label' has an Editor process running (State: $($statusInfo['State']), PID: $($statusInfo['Pid']), BlockedBy: $($statusInfo['BlockedBy'])), but it is unreachable or blocked. Running batch mode against an active project lock causes catastrophic contention. Refusing to run."
        Write-RunnerLog $blockErr "ERROR"
        $summaryData.Errors.Add($blockErr)
        $overallExitCode = [Math]::Max($overallExitCode, $EXIT_INFRASTRUCTURE)

        $projectSw.Stop()
        $summaryData.Projects.Add(@{
            Label = $label
            Path = $normPath
            ExecutionMode = "Blocked/Unreachable ($($statusInfo['State']))"
            Status = "BLOCKED"
            Suites = @()
            WallSeconds = [Math]::Round($projectSw.Elapsed.TotalSeconds, 3)
            NUnitDuration = 0.0
        })
        continue
    }

    if ($statusInfo['State'] -eq "safe_mode") {
        $safeErr = "Project '$label' is stuck in Safe Mode due to compilation errors. Fix C# source before running tests."
        Write-RunnerLog $safeErr "ERROR"
        $summaryData.Errors.Add($safeErr)
        $overallExitCode = [Math]::Max($overallExitCode, $EXIT_INFRASTRUCTURE)

        $projectSw.Stop()
        $summaryData.Projects.Add(@{
            Label = $label
            Path = $normPath
            ExecutionMode = "SafeMode"
            Status = "BLOCKED"
            Suites = @()
            WallSeconds = [Math]::Round($projectSw.Elapsed.TotalSeconds, 3)
            NUnitDuration = 0.0
        })
        continue
    }

    $isLive = [bool]$statusInfo['Reachable']
    $modeDesc = if ($isLive) { "LivePipeline" } else { "BatchCLI" }
    $projRecord = [ordered]@{
        Label = $label
        Path = $normPath
        ExecutionMode = $modeDesc
        Suites = [System.Collections.Generic.List[hashtable]]::new()
        Status = "SUCCESS"
        WallSeconds = 0.0
        NUnitDuration = 0.0
    }

    $runEdit = ($Suite -eq 'All' -or $Suite -eq 'EditMode')
    $runPlay = ($Suite -eq 'All' -or $Suite -eq 'PlayMode')
    $runSelf = ($Suite -eq 'All' -or $Suite -eq 'SelfTest') -and (
        ($isLive -and -not [string]::IsNullOrWhiteSpace($SelfTestCommand)) -or
        (-not $isLive -and -not [string]::IsNullOrWhiteSpace($SelfTestMethod))
    )

    $cleanLabel = ($label -replace '[^a-zA-Z0-9_\-\.]', '_').ToLowerInvariant()
    $excludeAssemblyStr = if ($ExcludeAssemblies.Length -gt 0) { ($ExcludeAssemblies -join ';') } else { '' }
    $excludeCategoryStr = if ($ExcludeCategories.Length -gt 0) { ($ExcludeCategories -join ';') } else { '' }
    $includeCategoryStr = if ($IncludeCategories.Length -gt 0) { ($IncludeCategories -join ';') } else { '' }

    # 1. EditMode
    if ($runEdit) {
        $xmlOut = Join-Path $OutputDirectory "${cleanLabel}_editmode.xml"
        $editResult = if ($isLive) {
            Run-LiveEditorTestSuite -TargetProjectPath $normPath -ModeName "editor" -XmlOutputPath $xmlOut -TestFilter $Filter -ExcludeAssembly $excludeAssemblyStr -ExcludeCategory $excludeCategoryStr -IncludeCategory $includeCategoryStr
        } else {
            Run-BatchTestSuite -TargetProjectPath $normPath -ModeName "EditMode" -XmlOutputPath $xmlOut -TestFilter $Filter -AssembliesToExclude $ExcludeAssemblies -CategoriesToExclude $ExcludeCategories -CategoriesToInclude $IncludeCategories
        }

        if (-not $editResult.Success) {
            $projRecord['Status'] = "FAILED"
            $overallExitCode = [Math]::Max($overallExitCode, $editResult.ExitCode)
        }

        $suiteNUnitDur = 0.0
        if ($null -ne $editResult.Report) {
            $rep = $editResult.Report
            $summaryData.TotalDiscovered += $rep.Discovered
            $summaryData.TotalExecuted += $rep.Executed
            $summaryData.TotalPassed += $rep.Passed
            $summaryData.TotalFailed += $rep.Failed
            $summaryData.TotalSkipped += $rep.Skipped
            $summaryData.TotalInconclusive += $rep.Inconclusive
            $summaryData.TotalNUnitDuration += $rep.Duration
            $suiteNUnitDur = $rep.Duration
            $projRecord['NUnitDuration'] = [Math]::Round(($projRecord['NUnitDuration'] + $rep.Duration), 3)

            if ($rep.ContainsKey('SlowestTests') -and $null -ne $rep['SlowestTests']) {
                foreach ($st in $rep['SlowestTests']) { $summaryData.SlowestTests.Add($st) }
            }
            if ($rep.ContainsKey('CategoryTotals') -and $null -ne $rep['CategoryTotals']) {
                foreach ($k in $rep['CategoryTotals'].Keys) {
                    if (-not $summaryData.CategoryTotals.Contains($k)) {
                        $summaryData.CategoryTotals[$k] = @{
                            Category = $k
                            Total = 0
                            Passed = 0
                            Failed = 0
                            Skipped = 0
                            Duration = 0.0
                        }
                    }
                    $src = $rep['CategoryTotals'][$k]
                    $summaryData.CategoryTotals[$k].Total += $src.Total
                    $summaryData.CategoryTotals[$k].Passed += $src.Passed
                    $summaryData.CategoryTotals[$k].Failed += $src.Failed
                    $summaryData.CategoryTotals[$k].Skipped += $src.Skipped
                    $summaryData.CategoryTotals[$k].Duration = [Math]::Round(($summaryData.CategoryTotals[$k].Duration + $src.Duration), 3)
                }
            }
        }

        $projRecord['Suites'].Add(@{
            SuiteName = "EditMode"
            Success = $editResult.Success
            Report = $editResult.Report
            ErrorMessage = $editResult.ErrorMessage
            XmlPath = $xmlOut
            WallSeconds = $editResult.WallSeconds
            NUnitDuration = $suiteNUnitDur
        })
    }

    # 2. PlayMode
    if ($runPlay) {
        $xmlOut = Join-Path $OutputDirectory "${cleanLabel}_playmode.xml"
        $playResult = if ($isLive) {
            Run-LiveEditorTestSuite -TargetProjectPath $normPath -ModeName "playmode" -XmlOutputPath $xmlOut -TestFilter $Filter -ExcludeAssembly $excludeAssemblyStr -ExcludeCategory $excludeCategoryStr -IncludeCategory $includeCategoryStr
        } else {
            Run-BatchTestSuite -TargetProjectPath $normPath -ModeName "PlayMode" -XmlOutputPath $xmlOut -TestFilter $Filter -AssembliesToExclude $ExcludeAssemblies -CategoriesToExclude $ExcludeCategories -CategoriesToInclude $IncludeCategories
        }

        if (-not $playResult.Success) {
            $projRecord['Status'] = "FAILED"
            $overallExitCode = [Math]::Max($overallExitCode, $playResult.ExitCode)
        }

        $suiteNUnitDur = 0.0
        if ($null -ne $playResult.Report) {
            $rep = $playResult.Report
            $summaryData.TotalDiscovered += $rep.Discovered
            $summaryData.TotalExecuted += $rep.Executed
            $summaryData.TotalPassed += $rep.Passed
            $summaryData.TotalFailed += $rep.Failed
            $summaryData.TotalSkipped += $rep.Skipped
            $summaryData.TotalInconclusive += $rep.Inconclusive
            $summaryData.TotalNUnitDuration += $rep.Duration
            $suiteNUnitDur = $rep.Duration
            $projRecord['NUnitDuration'] = [Math]::Round(($projRecord['NUnitDuration'] + $rep.Duration), 3)

            if ($rep.ContainsKey('SlowestTests') -and $null -ne $rep['SlowestTests']) {
                foreach ($st in $rep['SlowestTests']) { $summaryData.SlowestTests.Add($st) }
            }
            if ($rep.ContainsKey('CategoryTotals') -and $null -ne $rep['CategoryTotals']) {
                foreach ($k in $rep['CategoryTotals'].Keys) {
                    if (-not $summaryData.CategoryTotals.Contains($k)) {
                        $summaryData.CategoryTotals[$k] = @{
                            Category = $k
                            Total = 0
                            Passed = 0
                            Failed = 0
                            Skipped = 0
                            Duration = 0.0
                        }
                    }
                    $src = $rep['CategoryTotals'][$k]
                    $summaryData.CategoryTotals[$k].Total += $src.Total
                    $summaryData.CategoryTotals[$k].Passed += $src.Passed
                    $summaryData.CategoryTotals[$k].Failed += $src.Failed
                    $summaryData.CategoryTotals[$k].Skipped += $src.Skipped
                    $summaryData.CategoryTotals[$k].Duration = [Math]::Round(($summaryData.CategoryTotals[$k].Duration + $src.Duration), 3)
                }
            }
        }

        $projRecord['Suites'].Add(@{
            SuiteName = "PlayMode"
            Success = $playResult.Success
            Report = $playResult.Report
            ErrorMessage = $playResult.ErrorMessage
            XmlPath = $xmlOut
            WallSeconds = $playResult.WallSeconds
            NUnitDuration = $suiteNUnitDur
        })
    }

    # 3. SelfTest (only if explicitly requested and configured)
    if ($runSelf) {
        $selfRes = Run-SelfTestInternal -TargetProjectPath $normPath -IsLive $isLive -CommandSpec $SelfTestCommand -MethodSpec $SelfTestMethod
        if (-not $selfRes.Success) {
            $projRecord['Status'] = "FAILED"
            $overallExitCode = [Math]::Max($overallExitCode, $selfRes.ExitCode)
        }

        $projRecord['Suites'].Add(@{
            SuiteName = "SelfTest"
            Success = $selfRes.Success
            Output = $selfRes.Output
            WallSeconds = $selfRes.WallSeconds
            NUnitDuration = 0.0
        })
    }

    $projectSw.Stop()
    $projRecord['WallSeconds'] = [Math]::Round($projectSw.Elapsed.TotalSeconds, 3)
    $summaryData.Projects.Add($projRecord)
}

$runnerTotalStopwatch.Stop()
$summaryData.TotalWallDurationSeconds = [Math]::Round($runnerTotalStopwatch.Elapsed.TotalSeconds, 3)
$summaryData.EndTimeUtc = (Get-Date).ToUniversalTime().ToString("o")
$summaryData.Status = if ($overallExitCode -eq $EXIT_SUCCESS) { "PASS" } else { "FAIL" }

# -------------------------------------------------------------------------------------------------
# Report Generation
# -------------------------------------------------------------------------------------------------
if ($summaryData.SlowestTests.Count -gt 0) {
    $sortedSlowest = @($summaryData.SlowestTests | Sort-Object -Property { $_['Duration'] } -Descending | Select-Object -First 10)
    $summaryData.SlowestTests.Clear()
    foreach ($st in $sortedSlowest) { $summaryData.SlowestTests.Add($st) }
}

$jsonReportPath = Join-Path $OutputDirectory "test-summary.json"
$summaryData | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $jsonReportPath -Encoding utf8

$mdReportPath = Join-Path $OutputDirectory "test-summary.md"
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# Universal Unity Test Execution Summary")
[void]$md.AppendLine("")
$verdict = if ($overallExitCode -eq 0) { '**PASS**' } else { '**FAIL**' }
[void]$md.AppendLine("- **Overall Verdict:** $verdict")
[void]$md.AppendLine("- **Execution Date (UTC):** $($summaryData.StartTimeUtc)")
[void]$md.AppendLine("- **Suite:** $Suite | **Filter:** '$Filter'")
[void]$md.AppendLine("- **IncludeCategories:** $(($IncludeCategories -join ', '))")
[void]$md.AppendLine("- **ExcludeAssemblies:** $(($ExcludeAssemblies -join ', '))")
[void]$md.AppendLine("- **ExcludeCategories:** $(($ExcludeCategories -join ', '))")
[void]$md.AppendLine("- **Report Directory:** ``$OutputDirectory``")
[void]$md.AppendLine("- **Total Wall Duration:** $($summaryData.TotalWallDurationSeconds)s")
[void]$md.AppendLine("- **Total NUnit Test Duration:** $([Math]::Round($summaryData.TotalNUnitDuration, 3))s")
[void]$md.AppendLine("")

[void]$md.AppendLine("## Aggregate Metrics")
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("|---|---|")
[void]$md.AppendLine("| Total Discovered | $($summaryData.TotalDiscovered) |")
[void]$md.AppendLine("| Total Executed | $($summaryData.TotalExecuted) |")
[void]$md.AppendLine("| Passed | $($summaryData.TotalPassed) |")
[void]$md.AppendLine("| Failed | $($summaryData.TotalFailed) |")
[void]$md.AppendLine("| Skipped | $($summaryData.TotalSkipped) |")
[void]$md.AppendLine("| Inconclusive | $($summaryData.TotalInconclusive) |")
[void]$md.AppendLine("")

if ($summaryData.SlowestTests.Count -gt 0) {
    [void]$md.AppendLine("## Slowest 10 Tests")
    [void]$md.AppendLine("| Rank | Test Name | Duration (s) | Result |")
    [void]$md.AppendLine("|---|---|---|---|")
    $rank = 1
    foreach ($st in $summaryData.SlowestTests) {
        [void]$md.AppendLine("| $rank | $($st.Name) | $($st.Duration)s | $($st.Result) |")
        $rank++
    }
    [void]$md.AppendLine("")
}

if ($summaryData.CategoryTotals.Count -gt 0) {
    [void]$md.AppendLine("## Per-Category Breakdown")
    [void]$md.AppendLine("| Category | Total | Passed | Failed | Skipped | Duration (s) |")
    [void]$md.AppendLine("|---|---|---|---|---|---|")
    foreach ($catKey in ($summaryData.CategoryTotals.Keys | Sort-Object)) {
        $catRec = $summaryData.CategoryTotals[$catKey]
        [void]$md.AppendLine("| $($catRec.Category) | $($catRec.Total) | $($catRec.Passed) | $($catRec.Failed) | $($catRec.Skipped) | $($catRec.Duration)s |")
    }
    [void]$md.AppendLine("")
}

if ($summaryData.AutoSavedScenes.Count -gt 0) {
    [void]$md.AppendLine("## Unattended Recovery Actions")
    [void]$md.AppendLine("### Auto-Saved Scenes (Protected from Discarding)")
    foreach ($s in $summaryData.AutoSavedScenes) {
        [void]$md.AppendLine("- ``$s``")
    }
    [void]$md.AppendLine("")
}

if ($summaryData.Errors.Count -gt 0) {
    [void]$md.AppendLine("## Infrastructure & Execution Errors")
    foreach ($err in $summaryData.Errors) {
        [void]$md.AppendLine("- **Error:** $err")
    }
    [void]$md.AppendLine("")
}

[void]$md.AppendLine("## Detailed Project Breakdown")
foreach ($p in $summaryData.Projects) {
    [void]$md.AppendLine("### Project: $($p['Label'])")
    [void]$md.AppendLine("- **Path:** ``$($p['Path'])``")
    [void]$md.AppendLine("- **Routing Mode:** $($p['ExecutionMode'])")
    $pStat = if ($p['Status'] -eq 'SUCCESS') { '**PASS**' } else { '**FAIL**' }
    [void]$md.AppendLine("- **Status:** $pStat")
    [void]$md.AppendLine("- **Wall Duration:** $($p['WallSeconds'])s | **NUnit Execution Duration:** $($p['NUnitDuration'])s")
    [void]$md.AppendLine("")

    if ($p.ContainsKey('Suites') -and ($p['Suites'] -is [System.Collections.IList])) {
        foreach ($s in $p['Suites']) {
            [void]$md.AppendLine("#### Suite: $($s['SuiteName'])")
            $sRes = if ($s['Success']) { 'PASS' } else { 'FAIL' }
            [void]$md.AppendLine("- **Result:** $sRes")
            [void]$md.AppendLine("- **Wall Duration:** $($s['WallSeconds'])s | **NUnit Execution Duration:** $($s['NUnitDuration'])s")
            if ($null -ne $s['Report']) {
                $r = $s['Report']
                [void]$md.AppendLine("- **Counts:** Discovered: $($r.Discovered), Executed: $($r.Executed), Passed: $($r.Passed), Failed: $($r.Failed), Skipped: $($r.Skipped), Inconclusive: $($r.Inconclusive)")
                if ($r.FailureDetails.Count -gt 0) {
                    [void]$md.AppendLine("")
                    [void]$md.AppendLine('```text')
                    foreach ($fd in $r.FailureDetails) {
                        [void]$md.AppendLine("FAIL: $($fd['TestName'])")
                        [void]$md.AppendLine("Message: $($fd['Message'])")
                        if (-not [string]::IsNullOrEmpty($fd['StackTrace'])) {
                            [void]$md.AppendLine("Stack Trace:")
                            [void]$md.AppendLine($fd['StackTrace'])
                        }
                        [void]$md.AppendLine("--------------------------------------------------")
                    }
                    [void]$md.AppendLine('```')
                }
            } elseif (-not [string]::IsNullOrEmpty($s['Output'])) {
                [void]$md.AppendLine("")
                [void]$md.AppendLine('```text')
                [void]$md.AppendLine($s['Output'].Trim())
                [void]$md.AppendLine('```')
            }
            if (-not [string]::IsNullOrEmpty($s['ErrorMessage'])) {
                [void]$md.AppendLine("- **Error:** $($s['ErrorMessage'])")
            }
            [void]$md.AppendLine("")
        }
    }
}

$md.ToString() | Set-Content -LiteralPath $mdReportPath -Encoding utf8

$overallLogStatus = if ($overallExitCode -eq 0) { "SUCCESS" } else { "ERROR" }
Write-RunnerLog "Universal Unity Test Runner finished with status $($summaryData.Status). Exit code: $overallExitCode." $overallLogStatus
Write-RunnerLog "Summary markdown report: $mdReportPath" "INFO"
Write-RunnerLog "Summary json report: $jsonReportPath" "INFO"

exit $overallExitCode

