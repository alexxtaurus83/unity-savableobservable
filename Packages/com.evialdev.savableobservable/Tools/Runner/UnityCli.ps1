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

function Invoke-UnityCliJson {
    param(
        [Parameter(Mandatory=$true)][string[]]$CliArgs,
        [int]$TimeoutSec = 900
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
