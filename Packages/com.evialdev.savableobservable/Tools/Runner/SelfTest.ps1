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
