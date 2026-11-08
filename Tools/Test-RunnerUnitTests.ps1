param(
    [string]$ConsumerWrapperPath = ''
)

# Comprehensive Unit Tests for Universal Unity Test Runner and Thin Wrappers
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host "Running comprehensive unit tests for Universal Modular Unity Test Runner..." -ForegroundColor Cyan

$pwshPath = (Get-Process -Id $PID).Path
$frameworkToolsDir = $PSScriptRoot
$packageRunner = [System.IO.Path]::GetFullPath((Join-Path $frameworkToolsDir '..\Packages\com.evialdev.savableobservable\Tools\Invoke-UnityTestRunner.ps1'))
$frameworkWrapper = [System.IO.Path]::GetFullPath((Join-Path $frameworkToolsDir 'Invoke-UnityTests.ps1'))

if (-not (Test-Path -LiteralPath $packageRunner)) {
    throw "Package runner not found at: $packageRunner"
}
if (-not (Test-Path -LiteralPath $frameworkWrapper)) {
    throw "Framework wrapper not found at: $frameworkWrapper"
}

function Run-SubprocessTest {
    param([string]$Code)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwshPath
    $psi.ArgumentList.Add('-NoProfile')
    $psi.ArgumentList.Add('-Command')
    $psi.ArgumentList.Add($Code)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    [void]$p.Start()

    $stdoutTask = $p.StandardOutput.ReadToEndAsync()
    $stderrTask = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()

    return @{
        ExitCode = $p.ExitCode
        StdOut = $stdout
        StdErr = $stderr
    }
}

# -------------------------------------------------------------------------------------------------
# 1. Package Runner: Live Editor Success with Pending PlayMode Exit Retry
# -------------------------------------------------------------------------------------------------
$testScriptLive = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$prepCalls = 0
    `$mockReady = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[{\"project\":\"' + (`$fixtureTempDir -replace '\\\\', '\\\\') + '\",\"projectPath\":\"' + (`$fixtureTempDir -replace '\\\\', '\\\\') + '\",\"pid\":1234,\"state\":\"ready\"}]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{
                            instances = @(
                                @{
                                    project = `$fixtureTempDir
                                    projectPath = `$fixtureTempDir
                                    pid = 1234
                                    state = 'ready'
                                }
                            )
                        }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'command' -and `$CliArgs[1] -eq 'mmvc_prepare_unattended') {
                `$global:prepCalls++
                if (`$global:prepCalls -eq 1) {
                    return @{
                        ExitCode = 0
                        TimedOut = `$false
                        StdOut = '{\"success\":true,\"data\":{\"result\":\"{\\\"success\\\":false,\\\"pendingPlayModeExit\\\":true,\\\"message\\\":\\\"PlayMode exit initiated\\\"}\"}}'
                        StdErr = ''
                        DurationMs = 15
                        Envelope = @{
                            success = `$true
                            data = @{
                                result = '{"success":false,"pendingPlayModeExit":true,"message":"PlayMode exit initiated"}'
                            }
                        }
                    }
                }
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"data\":{\"result\":\"{\\\"success\\\":true,\\\"savedScenes\\\":[\\\"Assets/Scenes/Demo.unity\\\"],\\\"scratchScenesCreated\\\":[],\\\"message\\\":\\\"OK\\\"}\"}}'
                    StdErr = ''
                    DurationMs = 15
                    Envelope = @{
                        success = `$true
                        data = @{
                            result = '{"success":true,"savedScenes":["Assets/Scenes/Demo.unity"],"scratchScenesCreated":[],"message":"OK"}'
                        }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'command' -and `$CliArgs[1] -eq 'mmvc_run_tests_unattended') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outXml = `$CliArgs[`$outIdx + 1]
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Passed" total="2" passed="2" failed="0" inconclusive="0" skipped="0" duration="0.15">
    <test-case id="1" name="Pass1" fullname="Test.Pass1" result="Passed" duration="0.05" />
    <test-case id="2" name="Pass2" fullname="Test.Pass2" result="Passed" duration="0.10" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"data\":{\"result\":\"{\\\"success\\\":true,\\\"status\\\":\\\"completed\\\",\\\"message\\\":\\\"Done\\\"}\"}}'
                    StdErr = ''
                    DurationMs = 25
                    Envelope = @{
                        success = `$true
                        data = @{
                            result = '{"success":true,"status":"completed","message":"Done"}'
                        }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'command' -and `$CliArgs[1] -eq 'mmvc_selftest') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"data\":{\"result\":{\"passed\":true,\"summary\":\"RESULT: PASS\"}}}'
                    StdErr = ''
                    DurationMs = 20
                    Envelope = @{
                        success = `$true
                        data = @{
                            result = @{
                                passed = `$true
                                summary = 'RESULT: PASS'
                            }
                        }
                    }
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite All -SelfTestCommand 'mmvc_selftest --mode all' -MockExecutor `$mockReady
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resLive = Run-SubprocessTest -Code $testScriptLive
if ($resLive['ExitCode'] -ne 0) {
    Write-Host "STDOUT: $($resLive['StdOut'])"
    Write-Host "STDERR: $($resLive['StdErr'])"
    throw "Test 1 (Live editor success with retry) failed with exit code $($resLive['ExitCode'])"
}
Write-Host "Test 1 (Live editor success with retry): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 2. Package Runner: Batch CLI Assertion Failure Exit Code 8 (Validates dir output and report.xml)
# -------------------------------------------------------------------------------------------------
$testScriptFail = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockFail = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                if (-not (Test-Path -LiteralPath `$outDir)) {
                    [System.IO.Directory]::CreateDirectory(`$outDir) | Out-Null
                }
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Failed" total="2" passed="1" failed="1" inconclusive="0" skipped="0" duration="0.45">
    <test-case id="1" name="Pass1" fullname="Test.Pass1" result="Passed" duration="0.05" />
    <test-case id="2" name="Fail1" fullname="Test.Fail1" result="Failed" duration="0.40">
        <failure>
            <message><![CDATA[Expected 5 but was 4]]></message>
            <stack-trace><![CDATA[at Test.Fail1() in Test.cs:line 42]]></stack-trace>
        </failure>
    </test-case>
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Batch tests completed with failures'
                    StdErr = ''
                    DurationMs = 50
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -MockExecutor `$mockFail
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resFail = Run-SubprocessTest -Code $testScriptFail
if ($resFail['ExitCode'] -ne 8) {
    Write-Host "STDOUT: $($resFail['StdOut'])"
    Write-Host "STDERR: $($resFail['StdErr'])"
    throw "Test 2 (Batch test failure exit 8) failed with exit code $($resFail['ExitCode']) (expected 8)"
}
Write-Host "Test 2 (Batch test failure exit 8): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 3. Package Runner: Inconclusive Tests Yield Non-Zero (Exit Code 8)
# -------------------------------------------------------------------------------------------------
$testScriptInconclusive = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockInc = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Inconclusive" total="2" passed="1" failed="0" inconclusive="1" skipped="0" duration="0.20">
    <test-case id="1" name="Pass1" fullname="Test.Pass1" result="Passed" duration="0.05" />
    <test-case id="2" name="Inc1" fullname="Test.Inc1" result="Inconclusive" duration="0.15" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Batch tests completed with inconclusive'
                    StdErr = ''
                    DurationMs = 50
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -MockExecutor `$mockInc
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resInc = Run-SubprocessTest -Code $testScriptInconclusive
if ($resInc['ExitCode'] -ne 8) {
    Write-Host "STDOUT: $($resInc['StdOut'])"
    Write-Host "STDERR: $($resInc['StdErr'])"
    throw "Test 3 (Inconclusive yields exit 8) failed with exit code $($resInc['ExitCode']) (expected 8)"
}
Write-Host "Test 3 (Inconclusive tests yield non-zero exit code 8): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 4. Package Runner: Zero Tests / All Skipped Fails with Infrastructure Code 6
# -------------------------------------------------------------------------------------------------
$testScriptZero = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockZero = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="0" result="Passed" total="0" passed="0" failed="0" inconclusive="0" skipped="0" duration="0.0">
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'No tests found'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -MockExecutor `$mockZero
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resZero = Run-SubprocessTest -Code $testScriptZero
if ($resZero['ExitCode'] -ne 6) {
    Write-Host "STDOUT: $($resZero['StdOut'])"
    Write-Host "STDERR: $($resZero['StdErr'])"
    throw "Test 4 (Zero tests discovered/executed yields code 6) failed with exit code $($resZero['ExitCode']) (expected 6)"
}
Write-Host "Test 4 (Zero tests discovered/executed yields infrastructure code 6): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 5. Package Runner: Blocked Active Editor Process (Batch mode refused) -> Exit Code 6
# -------------------------------------------------------------------------------------------------
$testScriptBlocked = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockBlocked = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 6
                    TimedOut = `$false
                    StdOut = '{\"success\":false,\"command\":\"status\",\"data\":{\"instances\":[{\"project\":\"' + (`$fixtureTempDir -replace '\\\\', '\\\\') + '\",\"projectPath\":\"' + (`$fixtureTempDir -replace '\\\\', '\\\\') + '\",\"pid\":5678,\"state\":\"unreachable\",\"blockedBy\":\"ScriptUpdatingConsent\"}]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$false
                        command = 'status'
                        data = @{
                            instances = @(
                                @{
                                    project = `$fixtureTempDir
                                    projectPath = `$fixtureTempDir
                                    pid = 5678
                                    state = 'unreachable'
                                    blockedBy = 'ScriptUpdatingConsent'
                                }
                            )
                        }
                    }
                }
            }
            throw \"Unexpected call during blocked `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -MockExecutor `$mockBlocked
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resBlocked = Run-SubprocessTest -Code $testScriptBlocked
if ($resBlocked['ExitCode'] -ne 6) {
    Write-Host "STDOUT: $($resBlocked['StdOut'])"
    Write-Host "STDERR: $($resBlocked['StdErr'])"
    throw "Test 5 (Blocked editor batch refusal) failed with exit code $($resBlocked['ExitCode']) (expected 6)"
}
Write-Host "Test 5 (Blocked editor batch refusal yields exit code 6): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 6. Real Root Skipped:Ignored XML with Passed Cases (e.g. 557 Passed, 41 Skipped -> Must PASS 0)
# -------------------------------------------------------------------------------------------------
$testScriptSkipped = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockSkip = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="598" result="Skipped:Ignored" total="598" passed="557" failed="0" inconclusive="0" skipped="41" duration="42.50">
    <test-case id="1" name="Pass1" fullname="Test.Pass1" result="Passed" duration="0.05" />
    <test-case id="2" name="Ignored1" fullname="Test.Ignored1" result="Skipped:Ignored" duration="0.0">
        <reason><message><![CDATA[Scenario excluded by filter]]></message></reason>
    </test-case>
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Batch tests passed with ignored'
                    StdErr = ''
                    DurationMs = 30
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -MockExecutor `$mockSkip
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resSkipped = Run-SubprocessTest -Code $testScriptSkipped
if ($resSkipped['ExitCode'] -ne 0) {
    Write-Host "STDOUT: $($resSkipped['StdOut'])"
    Write-Host "STDERR: $($resSkipped['StdErr'])"
    throw "Test 6 (Real Root Skipped:Ignored with passed tests) failed with exit code $($resSkipped['ExitCode']) (expected 0)"
}
Write-Host "Test 6 (Real Root Skipped:Ignored with passed tests): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 7. Raw UTF Batch Execution with Categories and Assembly Exclusions (Pure Generic)
# -------------------------------------------------------------------------------------------------
$testScriptRawBatch = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockRaw = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'run') {
                if (-not (`$CliArgs -contains '-runTests')) { throw 'Missing -runTests' }
                if (-not (`$CliArgs -contains '-testPlatform')) { throw 'Missing -testPlatform' }
                
                `$catIdx = [array]::IndexOf(`$CliArgs, '-testCategory')
                if (`$catIdx -lt 0 -or `$CliArgs[`$catIdx + 1] -ne '!^Online`$') {
                    throw \"Expected -testCategory '!^Online`$' but got '`$(`$CliArgs[`$catIdx + 1])'\"
                }

                `$asmIdx = [array]::IndexOf(`$CliArgs, '-assemblyNames')
                if (`$asmIdx -lt 0 -or `$CliArgs[`$asmIdx + 1] -ne '!Example.Project.Scenarios') {
                    throw \"Expected -assemblyNames '!Example.Project.Scenarios' but got '`$(`$CliArgs[`$asmIdx + 1])'\"
                }

                `$resIdx = [array]::IndexOf(`$CliArgs, '-testResults')
                `$outXml = `$CliArgs[`$resIdx + 1]
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="10" result="Passed" total="10" passed="10" failed="0" inconclusive="0" skipped="0" duration="1.20">
    <test-case id="1" name="OfflinePass" fullname="Test.OfflinePass" result="Passed" duration="0.10" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Raw batch tests passed'
                    StdErr = ''
                    DurationMs = 100
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$packageRunner' -ProjectPath @(`$fixtureTempDir) -Suite EditMode -ExcludeAssemblies @('Example.Project.Scenarios') -ExcludeCategories @('Online') -MockExecutor `$mockRaw
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resRawBatch = Run-SubprocessTest -Code $testScriptRawBatch
if ($resRawBatch['ExitCode'] -ne 0) {
    Write-Host "STDOUT: $($resRawBatch['StdOut'])"
    Write-Host "STDERR: $($resRawBatch['StdErr'])"
    throw "Test 7 (Raw UTF batch execution with exclusions) failed with exit code $($resRawBatch['ExitCode']) (expected 0)"
}
Write-Host "Test 7 (Raw UTF batch execution with exclusions): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 8. Project Path with Spaces and Semicolon Separated Paths
# -------------------------------------------------------------------------------------------------
$testScriptSpaces = @"
`$p1 = Join-Path ([System.IO.Path]::GetTempPath()) ('Unity Project Space ' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$p1 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$p1 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$p1 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8

`$p2 = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityProject2_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$p2 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$p2 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$p2 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8

try {
    `$mockSpaces = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.10">
    <test-case id="1" name="Pass1" fullname="Test.Pass1" result="Passed" duration="0.10" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Batch tests passed'
                    StdErr = ''
                    DurationMs = 20
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        `$combined = \"`$p1;`$p2\"
        & '$packageRunner' -ProjectPath `$combined -Suite EditMode -MockExecutor `$mockSpaces
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$p1 -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath `$p2 -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resSpaces = Run-SubprocessTest -Code $testScriptSpaces
if ($resSpaces['ExitCode'] -ne 0) {
    Write-Host "STDOUT: $($resSpaces['StdOut'])"
    Write-Host "STDERR: $($resSpaces['StdErr'])"
    throw "Test 8 (Project path with spaces and semicolon paths) failed with exit code $($resSpaces['ExitCode']) (expected 0)"
}
Write-Host "Test 8 (Project path with spaces and semicolon paths): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 9. Framework Thin Wrapper Subprocess Test (Default Root & SelfTest Execution)
# -------------------------------------------------------------------------------------------------
$testScriptFw = @"
`$fixtureTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityFixture_' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'Assets')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path `$fixtureTempDir 'ProjectSettings')) | Out-Null
Set-Content -LiteralPath (Join-Path `$fixtureTempDir 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1' -Encoding utf8
try {
    `$mockFw = @{
        Invoke = {
            param(`$CliArgs, `$TimeoutSec)
            if (`$CliArgs[0] -eq 'status') {
                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[]}}'
                    StdErr = ''
                    DurationMs = 10
                    Envelope = @{
                        success = `$true
                        command = 'status'
                        data = @{ instances = @() }
                    }
                }
            }
            if (`$CliArgs[0] -eq 'test') {
                `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
                `$outDir = `$CliArgs[`$outIdx + 1]
                `$outXml = Join-Path `$outDir 'report.xml'
                @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.10">
    <test-case id="1" name="FwPass" fullname="Test.FwPass" result="Passed" duration="0.10" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

                return @{
                    ExitCode = 0
                    TimedOut = `$false
                    StdOut = 'Batch tests passed'
                    StdErr = ''
                    DurationMs = 20
                    Envelope = `$null
                }
            }
            throw \"Unexpected call `$(`$CliArgs -join ' ')\"
        }
    }

    try {
        & '$frameworkWrapper' -ProjectPath `$fixtureTempDir -Suite EditMode -MockExecutor `$mockFw
    } catch {
        exit `$LASTEXITCODE
    }
    exit `$LASTEXITCODE
} finally {
    Remove-Item -LiteralPath `$fixtureTempDir -Recurse -Force -ErrorAction SilentlyContinue
}
"@

$resFw = Run-SubprocessTest -Code $testScriptFw
if ($resFw['ExitCode'] -ne 0) {
    Write-Host "STDOUT: $($resFw['StdOut'])"
    Write-Host "STDERR: $($resFw['StdErr'])"
    throw "Test 9 (Framework wrapper execution) failed with exit code $($resFw['ExitCode']) (expected 0)"
}
Write-Host "Test 9 (Framework wrapper execution): PASS" -ForegroundColor Green

# -------------------------------------------------------------------------------------------------
# 10. Portable Consumer Wrapper Resolution & Verification (Runs only when ConsumerWrapperPath explicit)
# -------------------------------------------------------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace($ConsumerWrapperPath)) {
    if (-not (Test-Path -LiteralPath $ConsumerWrapperPath)) {
        throw "Specified ConsumerWrapperPath does not exist: $ConsumerWrapperPath"
    }

    $consumerWrapperNorm = [System.IO.Path]::GetFullPath($ConsumerWrapperPath)
    $consumerProjectDir = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $consumerWrapperNorm) '..'))

    $testScriptConsumer = @"
`$targetWrapper = '$consumerWrapperNorm'
`$targetProject = '$consumerProjectDir'
`$mockConsumer = @{
    Invoke = {
        param(`$CliArgs, `$TimeoutSec)
        if (`$CliArgs[0] -eq 'status') {
            return @{
                ExitCode = 0
                TimedOut = `$false
                StdOut = '{\"success\":true,\"command\":\"status\",\"data\":{\"instances\":[{\"project\":\"' + (`$targetProject -replace '\\\\', '\\\\') + '\",\"projectPath\":\"' + (`$targetProject -replace '\\\\', '\\\\') + '\",\"pid\":12345,\"state\":\"ready\"}]}}'
                StdErr = ''
                DurationMs = 10
                Envelope = @{
                    success = `$true
                    command = 'status'
                    data = @{
                        instances = @(
                            @{
                                project = `$targetProject
                                projectPath = `$targetProject
                                pid = 12345
                                state = 'ready'
                            }
                        )
                    }
                }
            }
        }
        if (`$CliArgs[0] -eq 'command' -and `$CliArgs[1] -eq 'mmvc_prepare_unattended') {
            return @{
                ExitCode = 0
                TimedOut = `$false
                StdOut = '{\"success\":true,\"data\":{\"result\":\"{\\\"success\\\":true,\\\"savedScenes\\\":[],\\\"scratchScenesCreated\\\":[],\\\"message\\\":\\\"OK\\\"}\"}}'
                StdErr = ''
                DurationMs = 15
                Envelope = @{
                    success = `$true
                    data = @{
                        result = '{"success":true,"savedScenes":[],"scratchScenesCreated":[],"message":"OK"}'
                    }
                }
            }
        }
        if (`$CliArgs[0] -eq 'command' -and `$CliArgs[1] -eq 'mmvc_run_tests_unattended') {
            `$outIdx = [array]::IndexOf(`$CliArgs, '--output')
            `$outXml = `$CliArgs[`$outIdx + 1]

            `$filterIdx = [array]::IndexOf(`$CliArgs, '--filter')
            `$filterVal = if (`$filterIdx -ge 0) { `$CliArgs[`$filterIdx + 1] } else { '' }

            `$excAssIdx = [array]::IndexOf(`$CliArgs, '--exclude_assembly')
            `$excAssVal = if (`$excAssIdx -ge 0) { `$CliArgs[`$excAssIdx + 1] } else { '' }

            `$excCatIdx = [array]::IndexOf(`$CliArgs, '--exclude_categories')
            `$excCatVal = if (`$excCatIdx -ge 0) { `$CliArgs[`$excCatIdx + 1] } else { '' }

            if (`$excAssVal -notmatch 'Scenario') {
                throw \"Expected exclude_assembly to contain 'Scenario' but got '`$excAssVal'\"
            }
            if (`$filterVal -notmatch 'Scenarios') {
                throw \"Expected filter to contain 'Scenarios' but got '`$filterVal'\"
            }
            if (`$excCatVal -ne 'Online') {
                throw \"Expected exclude_categories 'Online' but got '`$excCatVal'\"
            }

            @'
<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="3" result="Passed" total="3" passed="3" failed="0" inconclusive="0" skipped="0" duration="0.15">
    <test-case id="1" name="Offline1" fullname="Test.Offline1" result="Passed" duration="0.05" />
    <test-case id="2" name="Offline2" fullname="Test.Offline2" result="Passed" duration="0.05" />
    <test-case id="3" name="Offline3" fullname="Test.Offline3" result="Passed" duration="0.05" />
</test-run>
'@ | Set-Content -LiteralPath `$outXml -Encoding utf8

            return @{
                ExitCode = 0
                TimedOut = `$false
                StdOut = '{\"success\":true,\"data\":{\"result\":\"{\\\"success\\\":true,\\\"status\\\":\\\"completed\\\",\\\"message\\\":\\\"Done\\\"}\"}}'
                StdErr = ''
                DurationMs = 25
                Envelope = @{
                    success = `$true
                    data = @{
                        result = '{"success":true,"status":"completed","message":"Done"}'
                    }
                }
            }
        }
        throw \"Unexpected call `$(`$CliArgs -join ' ')\"
    }
}

try {
    & `$targetWrapper -Suite EditMode -SkipMultiplayer -MockExecutor `$mockConsumer
} catch {
    Write-Host \"Consumer wrapper error: `$_\"
    exit 1
}
exit `$LASTEXITCODE
"@

    $resConsumer = Run-SubprocessTest -Code $testScriptConsumer
    if ($resConsumer['ExitCode'] -ne 0) {
        Write-Host "STDOUT: $($resConsumer['StdOut'])"
        Write-Host "STDERR: $($resConsumer['StdErr'])"
        throw "Test 10 (Consumer wrapper resolution and argument verification) failed with exit code $($resConsumer['ExitCode']) (expected 0)"
    }
    Write-Host "Test 10 (Consumer wrapper resolution and argument verification): PASS" -ForegroundColor Green
} else {
    Write-Host "Test 10 (Consumer wrapper test skipped: specify -ConsumerWrapperPath <path> to run)" -ForegroundColor Yellow
}

Write-Host "All unit tests for Universal Modular Unity Test Runner passed successfully!" -ForegroundColor Green
exit 0
