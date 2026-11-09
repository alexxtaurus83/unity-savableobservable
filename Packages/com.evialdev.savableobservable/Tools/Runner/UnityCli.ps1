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

function Build-UnityCliArgs {
    <#
    Appends the CLI-global flags (--format json, --no-banner) to the CLI part of the argument list.
    Everything after a literal '--' is forwarded verbatim to the Unity Editor by `unity test` / `unity run`
    (unity CLI 1.0.0-beta.17), so the global flags must be inserted BEFORE the separator; flags placed after it
    are never parsed by the CLI and the output silently stops being JSON.
    #>
    param([Parameter(Mandatory=$true)][string[]]$CliArgs)

    $head = [System.Collections.Generic.List[string]]::new()
    $tail = [System.Collections.Generic.List[string]]::new()
    $sepIdx = [Array]::IndexOf($CliArgs, '--')
    for ($i = 0; $i -lt $CliArgs.Length; $i++) {
        if ($sepIdx -ge 0 -and $i -ge $sepIdx) { $tail.Add($CliArgs[$i]) } else { $head.Add($CliArgs[$i]) }
    }

    if (-not ($head -contains "--format") -and -not ($head -contains "--json")) {
        $head.Add("--format")
        $head.Add("json")
    }
    if (-not ($head -contains "--no-banner")) {
        $head.Add("--no-banner")
    }

    $head.AddRange($tail)
    return $head.ToArray()
}

function Get-UnityCliErrorText {
    <# Extracts "CODE: message" pairs from a CLI JSON envelope so failures are diagnosable from the summary report. #>
    param($Result)

    if ($null -eq $Result) { return "" }
    $env = $null
    if ($Result -is [hashtable] -and $Result.ContainsKey('Envelope')) { $env = $Result['Envelope'] }
    if ($env -is [hashtable] -and $env.ContainsKey('errors') -and ($env['errors'] -is [System.Collections.IList])) {
        $parts = foreach ($e in $env['errors']) {
            if ($e -is [hashtable]) {
                $code = if ($e.ContainsKey('code')) { [string]$e['code'] } else { 'ERROR' }
                $msg = if ($e.ContainsKey('message')) { [string]$e['message'] } else { '' }
                "${code}: $msg"
            }
        }
        if ($null -ne $parts) { return (@($parts) -join ' | ') }
    }
    if ($Result -is [hashtable] -and $Result.ContainsKey('StdErr') -and -not [string]::IsNullOrWhiteSpace([string]$Result['StdErr'])) {
        return ([string]$Result['StdErr']).Trim()
    }
    return ""
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

    $fullArgs = Build-UnityCliArgs -CliArgs $CliArgs

    $raw = Invoke-ProcessWithTimeout -FilePath "unity" -ArgumentList $fullArgs -TimeoutMs ($TimeoutSec * 1000)
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
