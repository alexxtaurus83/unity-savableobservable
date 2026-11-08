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

    [string[]]$ExcludeAssemblies = @(),

    [string[]]$ExcludeCategories = @(),

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
    ExcludeCategories = @($ExcludeCategories)
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
    Status = "SUCCESS"
    AutoSavedScenes = [System.Collections.Generic.List[string]]::new()
    Errors = [System.Collections.Generic.List[string]]::new()
}

$runnerTotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Write-RunnerLog {
    param([string]$Message, [string]$Level = "INFO")
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $prefix = "[$ts][$Level]"
    switch ($Level) {
        "ERROR"   { Write-Host "$prefix $Message" -ForegroundColor Red }
        "WARN"    { Write-Host "$prefix $Message" -ForegroundColor Yellow }
        "SUCCESS" { Write-Host "$prefix $Message" -ForegroundColor Green }
        default   { Write-Host "$prefix $Message" -ForegroundColor Cyan }
    }
}

# -------------------------------------------------------------------------------------------------
# Bounded Subprocess Execution via .NET Process + ReadToEndAsync
# -------------------------------------------------------------------------------------------------
function Invoke-ProcessWithTimeout {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$ArgumentList,
        [int]$TimeoutMs = ($TimeoutSeconds * 1000),
        [string]$WorkingDirectory = $PSScriptRoot
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    foreach ($arg in $ArgumentList) {
        $psi.ArgumentList.Add($arg)
    }
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        [void]$process.Start()
        $stdOutTask = $process.StandardOutput.ReadToEndAsync()
        $stdErrTask = $process.StandardError.ReadToEndAsync()

        $exited = $process.WaitForExit($TimeoutMs)
        if (-not $exited) {
            try { $process.Kill($true) } catch {}
            return @{
                ExitCode = 14
                TimedOut = $true
                StdOut = ""
                StdErr = "Execution timed out after $TimeoutMs ms."
                DurationMs = $sw.ElapsedMilliseconds
            }
        }

        $stdoutText = $stdOutTask.GetAwaiter().GetResult()
        $stderrText = $stdErrTask.GetAwaiter().GetResult()
        $sw.Stop()

        return @{
            ExitCode = $process.ExitCode
            TimedOut = $false
            StdOut = $stdoutText
            StdErr = $stderrText
            DurationMs = $sw.ElapsedMilliseconds
        }
    } finally {
        $process.Dispose()
    }
}

# -------------------------------------------------------------------------------------------------
# Unity CLI JSON Invocation & Robust Envelope Extraction
# -------------------------------------------------------------------------------------------------
function Invoke-UnityCliJson {
    param(
        [Parameter(Mandatory=$true)][string[]]$CliArgs,
        [int]$TimeoutSec = $TimeoutSeconds
    )

    if ($null -ne $MockExecutor -and $MockExecutor.ContainsKey('Invoke')) {
        $mockScript = [scriptblock]$MockExecutor['Invoke']
        return & $mockScript $CliArgs $TimeoutSec
    }

    $fullArgs = [System.Collections.Generic.List[string]]::new()
    $fullArgs.AddRange($CliArgs)
    if (-not ($fullArgs -contains "--format") -and -not ($fullArgs -contains "--json")) {
        $fullArgs.Add("--format")
        $fullArgs.Add("json")
    }
    if (-not ($fullArgs -contains "--no-banner")) {
        $fullArgs.Add("--no-banner")
    }

    $raw = Invoke-ProcessWithTimeout -FilePath "unity" -ArgumentList $fullArgs.ToArray() -TimeoutMs ($TimeoutSec * 1000)
    $envelope = Parse-UnityEnvelope -RawOutput $raw.StdOut

    return @{
        ExitCode = $raw.ExitCode
        TimedOut = $raw.TimedOut
        StdOut = $raw.StdOut
        StdErr = $raw.StdErr
        DurationMs = $raw.DurationMs
        Envelope = $envelope
    }
}

function Parse-UnityEnvelope {
    param([string]$RawOutput)

    if ([string]::IsNullOrWhiteSpace($RawOutput)) {
        return $null
    }

    $trimmed = $RawOutput.Trim()
    $firstBrace = $trimmed.IndexOf('{')
    $lastBrace = $trimmed.LastIndexOf('}')
    if ($firstBrace -lt 0 -or $lastBrace -le $firstBrace) {
        return $null
    }

    $jsonStr = $trimmed.Substring($firstBrace, ($lastBrace - $firstBrace + 1))
    try {
        return ($jsonStr | ConvertFrom-Json -AsHashtable)
    } catch {
        return $null
    }
}

function Extract-CommandResultPayload {
    param([hashtable]$Envelope)

    if ($null -eq $Envelope) { return $null }
    if (-not $Envelope.ContainsKey('data')) { return $null }

    $data = $Envelope['data']
    if ($null -eq $data -or -not ($data -is [hashtable])) { return $null }

    if (-not $data.ContainsKey('result')) { return $null }
    $res = $data['result']

    if ($res -is [string] -and ($res.TrimStart().StartsWith('{') -or $res.TrimStart().StartsWith('['))) {
        try {
            return ($res | ConvertFrom-Json -AsHashtable)
        } catch {
            return $res
        }
    }

    return $res
}

# -------------------------------------------------------------------------------------------------
# Truthful Live Editor Probe
# -------------------------------------------------------------------------------------------------
function Test-ProjectEditorStatus {
    param([Parameter(Mandatory=$true)][string]$TargetProjectPath)

    if (-not (Test-Path -LiteralPath $TargetProjectPath)) {
        return @{
            IsRunning = $false
            Reachable = $false
            State = "not_found"
            Pid = $null
            BlockedBy = ""
            Reason = "Project directory not found on disk: $TargetProjectPath"
        }
    }

    $statusRes = Invoke-UnityCliJson -CliArgs @("status", "--project-path", $TargetProjectPath) -TimeoutSec 15
    if ($statusRes.TimedOut) {
        return @{
            IsRunning = $false
            Reachable = $false
            State = "timeout"
            Pid = $null
            BlockedBy = ""
            Reason = "status command timed out after 15s"
        }
    }

    $normProj = [System.IO.Path]::GetFullPath($TargetProjectPath)
    $envelope = $statusRes.Envelope

    if ($null -ne $envelope -and $envelope.ContainsKey('data') -and ($envelope['data'] -is [hashtable])) {
        $data = [hashtable]$envelope['data']
        if ($data.ContainsKey('instances') -and ($data['instances'] -is [System.Collections.IList])) {
            foreach ($inst in $data['instances']) {
                if ($inst -is [hashtable]) {
                    $pPath = if ($inst.ContainsKey('projectPath')) { [string]$inst['projectPath'] } elseif ($inst.ContainsKey('project')) { [string]$inst['project'] } else { "" }
                    if (-not [string]::IsNullOrWhiteSpace($pPath) -and [System.IO.Path]::GetFullPath($pPath) -eq $normProj) {
                        $state = if ($inst.ContainsKey('state')) { [string]$inst['state'] } else { "unknown" }
                        $pidVal = if ($inst.ContainsKey('pid')) { [int]$inst['pid'] } else { $null }
                        $blockedBy = if ($inst.ContainsKey('blockedBy')) { [string]$inst['blockedBy'] } else { "" }

                        $isReachable = ($state -eq "ready" -or $state -eq "busy")
                        return @{
                            IsRunning = $true
                            Reachable = $isReachable
                            State = $state
                            Pid = $pidVal
                            BlockedBy = $blockedBy
                            Reason = "Discovered Editor instance with state '$state' (PID: $pidVal, BlockedBy: '$blockedBy')"
                        }
                    }
                }
            }
        }
    }

    # Check for lockfile fallback
    $lockFile = Join-Path $TargetProjectPath "Temp\UnityLockfile"
    if (Test-Path -LiteralPath $lockFile) {
        return @{
            IsRunning = $true
            Reachable = $false
            State = "locked_no_pipeline"
            Pid = $null
            BlockedBy = ""
            Reason = "UnityLockfile present but not reachable via Pipeline status"
        }
    }

    return @{
        IsRunning = $false
        Reachable = $false
        State = "no_instances"
        Pid = $null
        BlockedBy = ""
        Reason = "No running editor instance found for $TargetProjectPath"
    }
}

# -------------------------------------------------------------------------------------------------
# Safe Live Editor Preparation with bounded retry for PlayMode exit
# -------------------------------------------------------------------------------------------------
function Invoke-LiveEditorPrepare {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [ref]$AutoSavedScenesList,
        [int]$MaxPlayModeRetries = 5
    )

    $retryCount = 0
    while ($retryCount -lt $MaxPlayModeRetries) {
        $retryCount++
        $prepRes = Invoke-UnityCliJson -CliArgs @("command", "mmvc_prepare_unattended", "--project-path", $TargetProjectPath) -TimeoutSec 30
        if ($prepRes.ExitCode -ne 0) {
            return @{
                Success = $false
                ErrorMessage = "mmvc_prepare_unattended command exited with code $($prepRes.ExitCode): $($prepRes.StdErr)"
            }
        }

        $resultPayload = Extract-CommandResultPayload -Envelope $prepRes.Envelope
        if ($resultPayload -is [hashtable]) {
            $success = if ($resultPayload.ContainsKey('success')) { [bool]$resultPayload['success'] } else { $false }
            $pendingPlay = if ($resultPayload.ContainsKey('pendingPlayModeExit')) { [bool]$resultPayload['pendingPlayModeExit'] } else { $false }
            $msg = if ($resultPayload.ContainsKey('message')) { [string]$resultPayload['message'] } else { "" }

            if ($resultPayload.ContainsKey('savedScenes') -and ($resultPayload['savedScenes'] -is [System.Collections.IList])) {
                foreach ($s in $resultPayload['savedScenes']) {
                    $AutoSavedScenesList.Value.Add([string]$s)
                }
            }
            if ($resultPayload.ContainsKey('scratchScenesCreated') -and ($resultPayload['scratchScenesCreated'] -is [System.Collections.IList])) {
                foreach ($ss in $resultPayload['scratchScenesCreated']) {
                    $AutoSavedScenesList.Value.Add([string]$ss)
                }
            }

            if ($pendingPlay) {
                Write-RunnerLog "Editor PlayMode exit initiated; waiting 2s before retry ($retryCount/$MaxPlayModeRetries)..." "WARN"
                Start-Sleep -Seconds 2
                continue
            }

            if (-not $success) {
                return @{
                    Success = $false
                    ErrorMessage = "mmvc_prepare_unattended reported failure: $msg"
                }
            }
            return @{ Success = $true; ErrorMessage = "" }
        }

        return @{
            Success = $false
            ErrorMessage = "mmvc_prepare_unattended did not return valid result payload: $($prepRes.StdOut)"
        }
    }

    return @{
        Success = $false
        ErrorMessage = "Timed out waiting for Editor to exit PlayMode after $MaxPlayModeRetries retries."
    }
}

# -------------------------------------------------------------------------------------------------
# NUnit3 XML Parser
# -------------------------------------------------------------------------------------------------
function Parse-NUnitReportXml {
    param([Parameter(Mandatory=$true)][string]$XmlPath)

    if (-not (Test-Path -LiteralPath $XmlPath)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $XmlPath -Raw -ErrorAction Stop
        $runNode = $xml.SelectSingleNode("//test-run")
        if ($null -eq $runNode) { return $null }

        $runResult = $runNode.GetAttribute("result")
        $runResultClean = $runResult -replace '[:\(].*$', ''

        if ([string]::IsNullOrWhiteSpace($runResultClean)) {
            return $null
        }

        $total = 0; [int]::TryParse($runNode.GetAttribute("total"), [ref]$total) | Out-Null
        $passed = 0; [int]::TryParse($runNode.GetAttribute("passed"), [ref]$passed) | Out-Null
        $failed = 0; [int]::TryParse($runNode.GetAttribute("failed"), [ref]$failed) | Out-Null
        $skipped = 0; [int]::TryParse($runNode.GetAttribute("skipped"), [ref]$skipped) | Out-Null
        $inconclusive = 0; [int]::TryParse($runNode.GetAttribute("inconclusive"), [ref]$inconclusive) | Out-Null
        $duration = 0.0; [double]::TryParse($runNode.GetAttribute("duration"), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$duration) | Out-Null

        $testCaseCount = 0; [int]::TryParse($runNode.GetAttribute("testcasecount"), [ref]$testCaseCount) | Out-Null
        $discovered = if ($testCaseCount -gt 0) { $testCaseCount } else { $total }
        $executed = $passed + $failed

        $failureDetails = [System.Collections.Generic.List[hashtable]]::new()
        $failureNodes = $xml.SelectNodes("//test-case[failure] | //test-suite[failure and not(@site='Child')]")
        if ($null -ne $failureNodes) {
            foreach ($fn in $failureNodes) {
                $name = $fn.GetAttribute("fullname")
                if ([string]::IsNullOrEmpty($name)) { $name = $fn.GetAttribute("name") }
                $msgNode = $fn.SelectSingleNode("failure/message")
                $stackNode = $fn.SelectSingleNode("failure/stack-trace")
                $failureDetails.Add(@{
                    TestName = $name
                    Message = if ($null -ne $msgNode) { $msgNode.InnerText.Trim() } else { "No message" }
                    StackTrace = if ($null -ne $stackNode) { $stackNode.InnerText.Trim() } else { "" }
                })
            }
        }

        # Terminal result validation:
        $validTerminalResults = @('Passed', 'Failed', 'Skipped', 'Inconclusive')
        if (-not ($validTerminalResults -contains $runResultClean)) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                IsValid = $false
                ErrorMessage = "Test-run result '$runResult' is non-terminal."
            }
        }

        # Consistent totals check: sum of individual states must equal total
        if (($passed + $failed + $skipped + $inconclusive) -ne $total) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                IsValid = $false
                ErrorMessage = "Inconsistent counts: total ($total) != passed ($passed) + failed ($failed) + skipped ($skipped) + inconclusive ($inconclusive)."
            }
        }

        # If zero tests were discovered, or executed is 0 (all skipped or 0 total), cannot be valid passing
        if ($total -le 0 -or $executed -le 0) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                IsValid = $false
                ErrorMessage = "Test-run has 0 discovered tests or 0 executed tests ($total total, $executed executed, $skipped skipped)."
            }
        }

        # Determine effective verdict:
        # If any failed > 0 -> Failed
        # If runResult is Failed even with failed == 0 (e.g. OneTimeSetUp / fixture / suite failure) -> Failed
        # If any inconclusive > 0 -> Inconclusive
        # If executed > 0 and failed == 0 -> Passed (even if root runResult is Skipped due to ignored tests)
        $effectiveResult = if ($failed -gt 0 -or $runResultClean -eq "Failed") {
            "Failed"
        } elseif ($inconclusive -gt 0) {
            "Inconclusive"
        } elseif ($executed -gt 0 -and $failed -eq 0) {
            "Passed"
        } else {
            $runResultClean
        }

        return @{
            Total = $total
            Discovered = $discovered
            Executed = $executed
            Passed = $passed
            Failed = $failed
            Skipped = $skipped
            Inconclusive = $inconclusive
            Duration = $duration
            RunResult = $effectiveResult
            FailureDetails = $failureDetails
            IsValid = $true
            ErrorMessage = ""
        }
    } catch {
        return @{
            Total = 0
            Discovered = 0
            Executed = 0
            Passed = 0
            Failed = 0
            Skipped = 0
            Inconclusive = 0
            Duration = 0.0
            RunResult = "XmlParseError"
            FailureDetails = [System.Collections.Generic.List[hashtable]]::new()
            IsValid = $false
            ErrorMessage = "XML Parse exception: $_"
        }
    }
}

# -------------------------------------------------------------------------------------------------
# Live Editor Suite Execution
# -------------------------------------------------------------------------------------------------
function Run-LiveEditorTestSuite {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [Parameter(Mandatory=$true)][string]$ModeName, # 'editor' or 'playmode'
        [Parameter(Mandatory=$true)][string]$XmlOutputPath,
        [string]$TestFilter = '',
        [string]$ExcludeAssembly = '',
        [string]$ExcludeCategory = ''
    )

    if (Test-Path -LiteralPath $XmlOutputPath) {
        Remove-Item -LiteralPath $XmlOutputPath -Force
    }

    Write-RunnerLog "Preparing live Editor on $TargetProjectPath..." "INFO"
    $prep = Invoke-LiveEditorPrepare -TargetProjectPath $TargetProjectPath -AutoSavedScenesList ([ref]$summaryData.AutoSavedScenes)
    if (-not $prep.Success) {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = $prep.ErrorMessage
            Report = $null
            WallSeconds = 0.0
        }
    }

    Write-RunnerLog "Executing mmvc_run_tests_unattended ($ModeName) on $TargetProjectPath..." "INFO"
    $args = @("command", "mmvc_run_tests_unattended", "--mode", $ModeName, "--output", $XmlOutputPath, "--project-path", $TargetProjectPath)
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $args += @("--filter", $TestFilter)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExcludeAssembly)) {
        $args += @("--exclude_assembly", $ExcludeAssembly)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExcludeCategory)) {
        $args += @("--exclude_categories", $ExcludeCategory)
    }

    $suiteSw = [System.Diagnostics.Stopwatch]::StartNew()
    $runRes = Invoke-UnityCliJson -CliArgs $args -TimeoutSec $TimeoutSeconds
    if ($runRes.TimedOut) {
        $suiteSw.Stop()
        return @{
            Success = $false
            ExitCode = 14
            ErrorMessage = "Test execution start timed out after $TimeoutSeconds seconds"
            Report = $null
            WallSeconds = [Math]::Round($suiteSw.Elapsed.TotalSeconds, 3)
        }
    }

    if ($runRes.ExitCode -ne 0) {
        $suiteSw.Stop()
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "mmvc_run_tests_unattended failed to launch (CLI exit code $($runRes.ExitCode)): $($runRes.StdErr)"
            Report = $null
            WallSeconds = [Math]::Round($suiteSw.Elapsed.TotalSeconds, 3)
        }
    }

    $startPayload = Extract-CommandResultPayload -Envelope $runRes.Envelope
    if ($startPayload -is [hashtable]) {
        if ($startPayload.ContainsKey('success') -and -not [bool]$startPayload['success']) {
            $m = if ($startPayload.ContainsKey('message')) { [string]$startPayload['message'] } else { "Failed to initiate tests" }
            $suiteSw.Stop()
            return @{
                Success = $false
                ExitCode = $EXIT_INFRASTRUCTURE
                ErrorMessage = "mmvc_run_tests_unattended reported startup failure: $m"
                Report = $null
                WallSeconds = [Math]::Round($suiteSw.Elapsed.TotalSeconds, 3)
            }
        }
    }

    $timeoutMs = ($TimeoutSeconds * 1000)
    while ($suiteSw.ElapsedMilliseconds -lt $timeoutMs) {
        Start-Sleep -Seconds 2

        if (Test-Path -LiteralPath $XmlOutputPath) {
            $rawContent = Get-Content -LiteralPath $XmlOutputPath -Raw -ErrorAction SilentlyContinue
            if ($null -ne $rawContent -and $rawContent.Contains("</test-run>")) { break }
        }
    }

    $suiteSw.Stop()
    $wallSeconds = [Math]::Round($suiteSw.Elapsed.TotalSeconds, 3)

    if (-not (Test-Path -LiteralPath $XmlOutputPath)) {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Test run completed or timed out but XML output '$XmlOutputPath' was not produced."
            Report = $null
            WallSeconds = $wallSeconds
        }
    }

    $report = Parse-NUnitReportXml -XmlPath $XmlOutputPath
    if ($null -eq $report -or -not $report.IsValid) {
        $errDetail = if ($null -ne $report) { $report.ErrorMessage } else { "Failed to parse XML report" }
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Invalid test run XML produced at '$XmlOutputPath': $errDetail"
            Report = $report
            WallSeconds = $wallSeconds
        }
    }

    if ($report.Failed -gt 0) {
        return @{
            Success = $false
            ExitCode = $EXIT_TEST_FAILURES
            ErrorMessage = "$($report.Failed) test(s) failed."
            Report = $report
            WallSeconds = $wallSeconds
        }
    } elseif ($report.Inconclusive -gt 0) {
        return @{
            Success = $false
            ExitCode = $EXIT_TEST_FAILURES
            ErrorMessage = "$($report.Inconclusive) test(s) inconclusive."
            Report = $report
            WallSeconds = $wallSeconds
        }
    } elseif ($report.RunResult -eq "Passed" -and $report.Executed -gt 0) {
        return @{
            Success = $true
            ExitCode = $EXIT_SUCCESS
            ErrorMessage = ""
            Report = $report
            WallSeconds = $wallSeconds
        }
    } else {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Test run finished with unexpected result '$($report.RunResult)'."
            Report = $report
            WallSeconds = $wallSeconds
        }
    }
}

# -------------------------------------------------------------------------------------------------
# Batch Suite Execution via raw UTF runner command line flags or unity test
# -------------------------------------------------------------------------------------------------
function Run-BatchTestSuite {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [Parameter(Mandatory=$true)][string]$ModeName, # 'EditMode' or 'PlayMode'
        [Parameter(Mandatory=$true)][string]$XmlOutputPath,
        [string]$TestFilter = '',
        [string[]]$AssembliesToExclude = @(),
        [string[]]$CategoriesToExclude = @()
    )

    if (Test-Path -LiteralPath $XmlOutputPath) {
        Remove-Item -LiteralPath $XmlOutputPath -Force
    }

    # If any exclusion filters are supplied, unity test does NOT support -category or -assemblyNames.
    # We must use raw Unity runner flags via:
    # unity run <Project> --timeout <Timeout> -- -runTests -testPlatform <Mode> -testResults <XmlOutputPath> [-testCategory '!Cat'] [-assemblyNames '!Asm'] [-testFilter '<Filter>']
    $useRawRunner = ($AssembliesToExclude.Length -gt 0 -or $CategoriesToExclude.Length -gt 0)

    $suiteSw = [System.Diagnostics.Stopwatch]::StartNew()
    $testRes = $null

    if ($useRawRunner) {
        Write-RunnerLog "Executing raw batch tests ($ModeName) with exclusions on $TargetProjectPath..." "INFO"

        $rawArgs = [System.Collections.Generic.List[string]]::new()
        $rawArgs.Add("run")
        $rawArgs.Add($TargetProjectPath)
        $rawArgs.Add("--timeout")
        $rawArgs.Add("$($TimeoutSeconds + 60)")
        $rawArgs.Add("--")
        $rawArgs.Add("-runTests")
        $rawArgs.Add("-testPlatform")
        $rawArgs.Add($ModeName)
        $rawArgs.Add("-testResults")
        $rawArgs.Add($XmlOutputPath)

        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $rawArgs.Add("-testFilter")
            $rawArgs.Add($TestFilter)
        }

        if ($CategoriesToExclude.Length -gt 0) {
            # SettingsBuilder.cs supports -testCategory with semicolon separation and !^name$ anchored regex
            $catFlags = ($CategoriesToExclude | ForEach-Object { "!^" + [System.Text.RegularExpressions.Regex]::Escape($_.Trim()) + "`$" }) -join ';'
            $rawArgs.Add("-testCategory")
            $rawArgs.Add($catFlags)
        }

        if ($AssembliesToExclude.Length -gt 0) {
            $asmFlags = ($AssembliesToExclude | ForEach-Object { "!$_" }) -join ';'
            $rawArgs.Add("-assemblyNames")
            $rawArgs.Add($asmFlags)
        }

        $testRes = Invoke-UnityCliJson -CliArgs $rawArgs.ToArray() -TimeoutSec ($TimeoutSeconds + 60)
    } else {
        # unity test command: writes report.xml inside the output DIRECTORY
        $batchScratchDir = Join-Path (Split-Path -Parent $XmlOutputPath) ("batch_" + [System.IO.Path]::GetFileNameWithoutExtension($XmlOutputPath))
        if (Test-Path -LiteralPath $batchScratchDir) {
            # Purge stale batch dir to prevent stale pass
            Remove-Item -LiteralPath $batchScratchDir -Recurse -Force
        }
        [System.IO.Directory]::CreateDirectory($batchScratchDir) | Out-Null

        Write-RunnerLog "Executing batch CLI tests ($ModeName) on $TargetProjectPath..." "INFO"

        $cliArgs = @(
            "test", $TargetProjectPath,
            "--mode", $ModeName,
            "--report-format", "nunit",
            "--output", $batchScratchDir,
            "--timeout", "$TimeoutSeconds"
        )
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $cliArgs += @("--filter", $TestFilter)
        }

        $testRes = Invoke-UnityCliJson -CliArgs $cliArgs -TimeoutSec ($TimeoutSeconds + 60)

        # Move produced report.xml to XmlOutputPath
        $producedXml = Join-Path $batchScratchDir "report.xml"
        if (Test-Path -LiteralPath $producedXml) {
            Copy-Item -LiteralPath $producedXml -Destination $XmlOutputPath -Force
        }
    }

    $suiteSw.Stop()
    $wallSeconds = [Math]::Round($suiteSw.Elapsed.TotalSeconds, 3)

    if ($testRes.TimedOut) {
        return @{
            Success = $false
            ExitCode = 14
            ErrorMessage = "Batch test run timed out after $TimeoutSeconds seconds"
            Report = $null
            WallSeconds = $wallSeconds
        }
    }

    if (-not (Test-Path -LiteralPath $XmlOutputPath)) {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Batch mode exited with code $($testRes.ExitCode) but no XML found at '$XmlOutputPath'."
            Report = $null
            WallSeconds = $wallSeconds
        }
    }

    $report = Parse-NUnitReportXml -XmlPath $XmlOutputPath
    if ($null -eq $report -or -not $report.IsValid) {
        $errDetail = if ($null -ne $report) { $report.ErrorMessage } else { "Failed to parse XML report" }
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Invalid NUnit XML from '$XmlOutputPath': $errDetail (Subprocess exit: $($testRes.ExitCode))."
            Report = $report
            WallSeconds = $wallSeconds
        }
    }

    if ($testRes.ExitCode -ne 0 -and $report.Failed -eq 0 -and $report.Inconclusive -eq 0) {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Batch process failed with exit code $($testRes.ExitCode) despite XML report present."
            Report = $report
            WallSeconds = $wallSeconds
        }
    }

    if ($report.Failed -gt 0) {
        return @{
            Success = $false
            ExitCode = $EXIT_TEST_FAILURES
            ErrorMessage = "$($report.Failed) test(s) failed."
            Report = $report
            WallSeconds = $wallSeconds
        }
    } elseif ($report.Inconclusive -gt 0) {
        return @{
            Success = $false
            ExitCode = $EXIT_TEST_FAILURES
            ErrorMessage = "$($report.Inconclusive) test(s) inconclusive."
            Report = $report
            WallSeconds = $wallSeconds
        }
    } elseif ($report.RunResult -eq "Passed" -and $report.Executed -gt 0) {
        return @{
            Success = $true
            ExitCode = $EXIT_SUCCESS
            ErrorMessage = ""
            Report = $report
            WallSeconds = $wallSeconds
        }
    } else {
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Batch test run finished with unexpected result '$($report.RunResult)'."
            Report = $report
            WallSeconds = $wallSeconds
        }
    }
}

# -------------------------------------------------------------------------------------------------
# SelfTest Execution (Optional)
# -------------------------------------------------------------------------------------------------
function Run-SelfTestInternal {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [Parameter(Mandatory=$true)][bool]$IsLive,
        [string]$CommandSpec = '',
        [string]$MethodSpec = ''
    )

    $selfSw = [System.Diagnostics.Stopwatch]::StartNew()

    if ($IsLive) {
        if ([string]::IsNullOrWhiteSpace($CommandSpec)) {
            $selfSw.Stop()
            return @{ Success = $true; ExitCode = $EXIT_SUCCESS; Output = "SelfTest skipped (no live command configured)"; WallSeconds = 0.0 }
        }

        Write-RunnerLog "Executing live SelfTest '$CommandSpec' on $TargetProjectPath..." "INFO"
        $parts = $CommandSpec -split '\s+'
        $cmdName = $parts[0]
        $extraArgs = if ($parts.Length -gt 1) { $parts[1..($parts.Length - 1)] } else { @() }
        $cliArgs = @("command", $cmdName, "--project-path", $TargetProjectPath) + $extraArgs

        $res = Invoke-UnityCliJson -CliArgs $cliArgs -TimeoutSec 150
        $selfSw.Stop()
        $rawOutput = if ($null -ne $res.Envelope) { ($res.Envelope | ConvertTo-Json -Depth 4) } else { [string]$res.StdOut }
        $passed = ($res.ExitCode -eq 0 -and $rawOutput -match "RESULT:\s+PASS")
        return @{
            Success = $passed
            ExitCode = if ($passed) { $EXIT_SUCCESS } else { $EXIT_TEST_FAILURES }
            Output = $rawOutput
            WallSeconds = [Math]::Round($selfSw.Elapsed.TotalSeconds, 3)
        }
    } else {
        if ([string]::IsNullOrWhiteSpace($MethodSpec)) {
            $selfSw.Stop()
            return @{ Success = $true; ExitCode = $EXIT_SUCCESS; Output = "SelfTest skipped (no batch method configured)"; WallSeconds = 0.0 }
        }

        Write-RunnerLog "Executing batch SelfTest '$MethodSpec' on $TargetProjectPath..." "INFO"
        $methodName = $MethodSpec
        if ($methodName.StartsWith("-executeMethod ")) {
            $methodName = $methodName.Substring("-executeMethod ".Length).Trim()
        }
        $args = @("run", $TargetProjectPath, "--timeout", "150", "--", "-executeMethod", $methodName)
        $res = Invoke-UnityCliJson -CliArgs $args -TimeoutSec 180
        $selfSw.Stop()
        $outStr = [string]$res.StdOut + "`n" + [string]$res.StdErr
        $passed = ($res.ExitCode -eq 0 -and $outStr -match "RESULT:\s+PASS")
        return @{
            Success = $passed
            ExitCode = if ($passed) { $EXIT_SUCCESS } else { $EXIT_TEST_FAILURES }
            Output = $outStr
            WallSeconds = [Math]::Round($selfSw.Elapsed.TotalSeconds, 3)
        }
    }
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

    # 1. EditMode
    if ($runEdit) {
        $xmlOut = Join-Path $OutputDirectory "${cleanLabel}_editmode.xml"
        $editResult = if ($isLive) {
            Run-LiveEditorTestSuite -TargetProjectPath $normPath -ModeName "editor" -XmlOutputPath $xmlOut -TestFilter $Filter -ExcludeAssembly $excludeAssemblyStr -ExcludeCategory $excludeCategoryStr
        } else {
            Run-BatchTestSuite -TargetProjectPath $normPath -ModeName "EditMode" -XmlOutputPath $xmlOut -TestFilter $Filter -AssembliesToExclude $ExcludeAssemblies -CategoriesToExclude $ExcludeCategories
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
            Run-LiveEditorTestSuite -TargetProjectPath $normPath -ModeName "playmode" -XmlOutputPath $xmlOut -TestFilter $Filter -ExcludeAssembly $excludeAssemblyStr -ExcludeCategory $excludeCategoryStr
        } else {
            Run-BatchTestSuite -TargetProjectPath $normPath -ModeName "PlayMode" -XmlOutputPath $xmlOut -TestFilter $Filter -AssembliesToExclude $ExcludeAssemblies -CategoriesToExclude $ExcludeCategories
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
