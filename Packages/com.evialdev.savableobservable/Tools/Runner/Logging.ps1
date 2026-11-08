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
