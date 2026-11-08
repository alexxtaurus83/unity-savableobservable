function Invoke-ProcessWithTimeout {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$ArgumentList,
        [int]$TimeoutMs = 900000,
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
