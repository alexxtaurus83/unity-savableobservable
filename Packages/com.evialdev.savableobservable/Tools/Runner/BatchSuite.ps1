function Run-BatchTestSuite {
    param(
        [Parameter(Mandatory=$true)][string]$TargetProjectPath,
        [Parameter(Mandatory=$true)][string]$ModeName, # 'EditMode' or 'PlayMode'
        [Parameter(Mandatory=$true)][string]$XmlOutputPath,
        [string]$TestFilter = '',
        [string[]]$AssembliesToExclude = @(),
        [string[]]$CategoriesToExclude = @(),
        [string[]]$CategoriesToInclude = @()
    )

    # Stale report guard: `unity test --output` takes a FILE path (unity CLI 1.0.0-beta.17). It fails with
    # TEST_RESULTS_MISSING when the path is a pre-created directory, so never create one here.
    if (Test-Path -LiteralPath $XmlOutputPath) {
        Remove-Item -LiteralPath $XmlOutputPath -Recurse -Force
    }
    $xmlParentDir = Split-Path -Parent $XmlOutputPath
    if (-not [string]::IsNullOrWhiteSpace($xmlParentDir) -and -not (Test-Path -LiteralPath $xmlParentDir)) {
        [System.IO.Directory]::CreateDirectory($xmlParentDir) | Out-Null
    }

    $suiteSw = [System.Diagnostics.Stopwatch]::StartNew()

    Write-RunnerLog "Executing batch CLI tests ($ModeName) on $TargetProjectPath..." "INFO"

    # `unity test` is the only batch entry point that produces a report. (`unity run -- -runTests ...` quits the
    # editor before the test runner starts and never writes XML.)
    #   --filter                 -> test-name filter (supports ';' separated values and '!' negation)
    #   args after the '--' mark -> forwarded verbatim to the editor: -testCategory / -assemblyNames
    $cliArgs = [System.Collections.Generic.List[string]]::new()
    $cliArgs.AddRange([string[]]@(
        "test", $TargetProjectPath,
        "--mode", $ModeName,
        "--report-format", "nunit",
        "--output", $XmlOutputPath,
        "--timeout", "$TimeoutSeconds"
    ))
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $cliArgs.Add("--filter")
        $cliArgs.Add($TestFilter)
    }

    $editorArgs = [System.Collections.Generic.List[string]]::new()

    $catList = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $CategoriesToInclude) {
        if (-not [string]::IsNullOrWhiteSpace($c)) { $catList.Add($c.Trim()) }
    }
    foreach ($c in $CategoriesToExclude) {
        if (-not [string]::IsNullOrWhiteSpace($c)) {
            $catList.Add("!^" + [System.Text.RegularExpressions.Regex]::Escape($c.Trim()) + "`$")
        }
    }
    if ($catList.Count -gt 0) {
        $editorArgs.Add("-testCategory")
        $editorArgs.Add(($catList -join ';'))
    }

    $asmList = @($AssembliesToExclude | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { "!$($_.Trim())" })
    if ($asmList.Count -gt 0) {
        $editorArgs.Add("-assemblyNames")
        $editorArgs.Add(($asmList -join ';'))
    }

    if ($editorArgs.Count -gt 0) {
        $cliArgs.Add("--")
        $cliArgs.AddRange($editorArgs)
    }

    $testRes = Invoke-UnityCliJson -CliArgs $cliArgs.ToArray() -TimeoutSec ($TimeoutSeconds + 60)

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

    if (-not (Test-Path -LiteralPath $XmlOutputPath -PathType Leaf)) {
        $cliErr = Get-UnityCliErrorText -Result $testRes
        $cliErrSuffix = if ([string]::IsNullOrWhiteSpace($cliErr)) { "" } else { " CLI error: $cliErr" }
        return @{
            Success = $false
            ExitCode = $EXIT_INFRASTRUCTURE
            ErrorMessage = "Batch mode exited with code $($testRes.ExitCode) but no XML found at '$XmlOutputPath'.$cliErrSuffix"
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
