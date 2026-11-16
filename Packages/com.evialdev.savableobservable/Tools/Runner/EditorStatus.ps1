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

            if ($success) {
                return @{ Success = $true; ErrorMessage = "" }
            } else {
                return @{ Success = $false; ErrorMessage = "mmvc_prepare_unattended reported failure: $msg" }
            }
        } else {
            return @{ Success = $false; ErrorMessage = "mmvc_prepare_unattended returned unexpected payload: $($prepRes.StdOut)" }
        }
    }

    return @{ Success = $false; ErrorMessage = "Failed to exit PlayMode after $MaxPlayModeRetries retries." }
}
