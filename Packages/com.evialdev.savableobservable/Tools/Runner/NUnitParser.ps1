function Parse-NUnitReportXml {
    param([Parameter(Mandatory=$true)][string]$XmlPath)

    if (-not (Test-Path -LiteralPath $XmlPath)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $XmlPath -Raw -ErrorAction Stop
        $runNode = $xml.SelectSingleNode("//test-run")
        if ($null -eq $runNode) { return $null }

        $runResult = $runNode.GetAttribute("result")
        $runResultClean = $runResult -replace '[:\(].*$', ''

        if ([string]::IsNullOrWhiteSpace($runResultClean)) {
            return $null
        }

        $total = 0; [int]::TryParse($runNode.GetAttribute("total"), [ref]$total) | Out-Null
        $passed = 0; [int]::TryParse($runNode.GetAttribute("passed"), [ref]$passed) | Out-Null
        $failed = 0; [int]::TryParse($runNode.GetAttribute("failed"), [ref]$failed) | Out-Null
        $skipped = 0; [int]::TryParse($runNode.GetAttribute("skipped"), [ref]$skipped) | Out-Null
        $inconclusive = 0; [int]::TryParse($runNode.GetAttribute("inconclusive"), [ref]$inconclusive) | Out-Null
        $duration = 0.0; [double]::TryParse($runNode.GetAttribute("duration"), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$duration) | Out-Null

        $testCaseCount = 0; [int]::TryParse($runNode.GetAttribute("testcasecount"), [ref]$testCaseCount) | Out-Null
        $discovered = if ($testCaseCount -gt 0) { $testCaseCount } else { $total }
        $executed = $passed + $failed

        $failureDetails = [System.Collections.Generic.List[hashtable]]::new()
        $failureNodes = $xml.SelectNodes("//test-case[failure] | //test-suite[failure and not(@site='Child')]")
        if ($null -ne $failureNodes) {
            foreach ($fn in $failureNodes) {
                $name = $fn.GetAttribute("fullname")
                if ([string]::IsNullOrEmpty($name)) { $name = $fn.GetAttribute("name") }
                $msgNode = $fn.SelectSingleNode("failure/message")
                $stackNode = $fn.SelectSingleNode("failure/stack-trace")
                $failureDetails.Add(@{
                    TestName = $name
                    Message = if ($null -ne $msgNode) { $msgNode.InnerText.Trim() } else { "No message" }
                    StackTrace = if ($null -ne $stackNode) { $stackNode.InnerText.Trim() } else { "" }
                })
            }
        }

        # Terminal result validation:
        $validTerminalResults = @('Passed', 'Failed', 'Skipped', 'Inconclusive')
        if (-not ($validTerminalResults -contains $runResultClean)) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                SlowestTests = [System.Collections.Generic.List[hashtable]]::new()
                CategoryTotals = [ordered]@{}
                IsValid = $false
                ErrorMessage = "Test-run result '$runResult' is non-terminal."
            }
        }

        # Consistent totals check: sum of individual states must equal total
        if (($passed + $failed + $skipped + $inconclusive) -ne $total) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                SlowestTests = [System.Collections.Generic.List[hashtable]]::new()
                CategoryTotals = [ordered]@{}
                IsValid = $false
                ErrorMessage = "Inconsistent counts: total ($total) != passed ($passed) + failed ($failed) + skipped ($skipped) + inconclusive ($inconclusive)."
            }
        }

        # If zero tests were discovered, or executed is 0 (all skipped or 0 total), cannot be valid passing
        if ($total -le 0 -or $executed -le 0) {
            return @{
                Total = $total
                Discovered = $discovered
                Executed = $executed
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Inconclusive = $inconclusive
                Duration = $duration
                RunResult = $runResultClean
                FailureDetails = $failureDetails
                SlowestTests = [System.Collections.Generic.List[hashtable]]::new()
                CategoryTotals = [ordered]@{}
                IsValid = $false
                ErrorMessage = "Test-run has 0 discovered tests or 0 executed tests ($total total, $executed executed, $skipped skipped)."
            }
        }

        # Determine effective verdict:
        $effectiveResult = if ($failed -gt 0 -or $runResultClean -eq "Failed") {
            "Failed"
        } elseif ($inconclusive -gt 0) {
            "Inconclusive"
        } elseif ($executed -gt 0 -and $failed -eq 0) {
            "Passed"
        } else {
            $runResultClean
        }

        $allTestCases = [System.Collections.Generic.List[hashtable]]::new()
        $categoryTotals = [ordered]@{}
        $tcNodes = $xml.SelectNodes("//test-case")
        if ($null -ne $tcNodes) {
            foreach ($tc in $tcNodes) {
                $tcName = $tc.GetAttribute("fullname")
                if ([string]::IsNullOrEmpty($tcName)) { $tcName = $tc.GetAttribute("name") }
                $tcRes = $tc.GetAttribute("result")
                $tcDur = 0.0
                [double]::TryParse($tc.GetAttribute("duration"), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$tcDur) | Out-Null

                $tcCategories = [System.Collections.Generic.List[string]]::new()
                $catProps = $tc.SelectNodes("properties/property[@name='Category']")
                if ($null -ne $catProps) {
                    foreach ($cp in $catProps) {
                        $cVal = $cp.GetAttribute("value")
                        if (-not [string]::IsNullOrWhiteSpace($cVal) -and -not $tcCategories.Contains($cVal)) {
                            $tcCategories.Add($cVal)
                        }
                    }
                }
                if ($tcCategories.Count -eq 0) {
                    $parentSuite = $tc.ParentNode
                    while ($null -ne $parentSuite -and $parentSuite.Name -eq "test-suite") {
                        $parentCats = $parentSuite.SelectNodes("properties/property[@name='Category']")
                        if ($null -ne $parentCats) {
                            foreach ($pc in $parentCats) {
                                $cVal = $pc.GetAttribute("value")
                                if (-not [string]::IsNullOrWhiteSpace($cVal) -and -not $tcCategories.Contains($cVal)) {
                                    $tcCategories.Add($cVal)
                                }
                            }
                        }
                        $parentSuite = $parentSuite.ParentNode
                    }
                }

                $tcRecord = @{
                    Name = $tcName
                    Duration = [Math]::Round($tcDur, 3)
                    Result = $tcRes
                    Categories = @($tcCategories)
                }
                $allTestCases.Add($tcRecord)

                foreach ($cat in $tcCategories) {
                    if (-not $categoryTotals.Contains($cat)) {
                        $categoryTotals[$cat] = @{
                            Category = $cat
                            Total = 0
                            Passed = 0
                            Failed = 0
                            Skipped = 0
                            Duration = 0.0
                        }
                    }
                    $categoryTotals[$cat].Total++
                    $categoryTotals[$cat].Duration = [Math]::Round(($categoryTotals[$cat].Duration + $tcDur), 3)
                    if ($tcRes -like "*Pass*") {
                        $categoryTotals[$cat].Passed++
                    } elseif ($tcRes -like "*Fail*") {
                        $categoryTotals[$cat].Failed++
                    } elseif ($tcRes -like "*Skip*" -or $tcRes -like "*Ignore*") {
                        $categoryTotals[$cat].Skipped++
                    }
                }
            }
        }

        $slowest = $allTestCases | Sort-Object -Property { $_.Duration } -Descending | Select-Object -First 10
        $slowestList = [System.Collections.Generic.List[hashtable]]::new()
        if ($null -ne $slowest) {
            foreach ($st in $slowest) { $slowestList.Add($st) }
        }

        return @{
            Total = $total
            Discovered = $discovered
            Executed = $executed
            Passed = $passed
            Failed = $failed
            Skipped = $skipped
            Inconclusive = $inconclusive
            Duration = $duration
            RunResult = $effectiveResult
            FailureDetails = $failureDetails
            SlowestTests = $slowestList
            CategoryTotals = $categoryTotals
            IsValid = $true
            ErrorMessage = ""
        }
    } catch {
        return @{
            Total = 0
            Discovered = 0
            Executed = 0
            Passed = 0
            Failed = 0
            Skipped = 0
            Inconclusive = 0
            Duration = 0.0
            RunResult = "XmlParseError"
            FailureDetails = [System.Collections.Generic.List[hashtable]]::new()
            SlowestTests = [System.Collections.Generic.List[hashtable]]::new()
            CategoryTotals = [ordered]@{}
            IsValid = $false
            ErrorMessage = "XML Parse exception: $_"
        }
    }
}
