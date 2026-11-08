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

    if (Test-Path -LiteralPath $XmlOutputPath) {
        Remove-Item -LiteralPath $XmlOutputPath -Force
    }

    # If any exclusion/inclusion filters are supplied, unity test does NOT support -category or -assemblyNames.
    # We must use raw Unity runner flags via:
    # unity run <Project> --timeout <Timeout> -- -runTests -testPlatform <Mode> -testResults <XmlOutputPath> [-testCategory '!Cat;Inc'] [-assemblyNames '!Asm'] [-testFilter '<Filter>']
    $useRawRunner = ($AssembliesToExclude.Length -gt 0 -or $CategoriesToExclude.Length -gt 0 -or $CategoriesToInclude.Length -gt 0)

    $suiteSw = [System.Diagnostics.Stopwatch]::StartNew()
    $testRes = $null

    if ($useRawRunner) {
        Write-RunnerLog "Executing raw batch tests ($ModeName) with category/assembly filters on $TargetProjectPath..." "INFO"

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

        $catList = [System.Collections.Generic.List[string]]::new()
        if ($CategoriesToInclude.Length -gt 0) {
            foreach ($c in $CategoriesToInclude) {
                if (-not [string]::IsNullOrWhiteSpace($c)) { $catList.Add($c.Trim()) }
            }
        }
        if ($CategoriesToExclude.Length -gt 0) {
            foreach ($c in $CategoriesToExclude) {
                if (-not [string]::IsNullOrWhiteSpace($c)) {
                    $catList.Add("!^" + [System.Text.RegularExpressions.Regex]::Escape($c.Trim()) + "`$")
                }
            }
        }
        if ($catList.Count -gt 0) {
            $rawArgs.Add("-testCategory")
            $rawArgs.Add(($catList -join ';'))
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
