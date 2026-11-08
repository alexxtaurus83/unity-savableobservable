function Run-LiveEditorTestSuite {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [Parameter(Mandatory=$true)][string]$ModeName, # 'editor' or 'playmode'
        [Parameter(Mandatory=$true)][string]$XmlOutputPath,
        [string]$TestFilter = '',
        [string]$ExcludeAssembly = '',
        [string]$ExcludeCategory = '',
        [string]$IncludeCategory = ''
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
    if (-not [string]::IsNullOrWhiteSpace($IncludeCategory)) {
        $args += @("--include_categories", $IncludeCategory)
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
            ErrorMessage = "Test run finished or timed out without writing XML output to '$XmlOutputPath'."
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
            ErrorMessage = "Invalid NUnit XML report from '$XmlOutputPath': $errDetail"
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
