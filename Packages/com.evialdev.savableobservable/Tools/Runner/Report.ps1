function Test-InventoryNameMatch {
    <#
    Same semantics as `unity test --filter` / `test run --filter_type testName`: ';' separates patterns,
    a leading '!' negates, matching is a case-insensitive substring of the full test name.
    #>
    param([string]$FullName, [string]$Filter)

    if ([string]::IsNullOrWhiteSpace($Filter)) { return $true }
    $positives = [System.Collections.Generic.List[string]]::new()
    foreach ($raw in $Filter.Split(';')) {
        $pat = $raw.Trim()
        if ([string]::IsNullOrEmpty($pat)) { continue }
        if ($pat.StartsWith('!')) {
            $neg = $pat.Substring(1)
            if ($neg.Length -gt 0 -and $FullName.IndexOf($neg, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $false }
        } else {
            $positives.Add($pat)
        }
    }
    if ($positives.Count -eq 0) { return $true }
    foreach ($pat in $positives) {
        if ($FullName.IndexOf($pat, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Get-LiveTestInventory {
    <#
    Per-case inventory from the live editor's `unity command test list` (Unity Pipeline). It reports one entry per NUnit
    test case, which is exactly what a run reports as Summary.Total: [TestCase]/[TestCaseSource] expansions and tests
    inherited from abstract base fixtures are counted, [Explicit] tests are not. (`mmvc_list_tests` only counts declared
    [Test] methods, so it under-counts parameterized and inherited tests.)
    Returns $null when the command is unavailable so callers can fall back.
    #>
    param(
        [string]$TargetProjectPath,
        [string]$SuiteName = 'All',
        [string]$TestFilter = '',
        [string[]]$IncludeCategories = @(),
        [string[]]$ExcludeCategories = @(),
        [string[]]$ExcludeAssemblies = @()
    )

    $modeArg = switch ($SuiteName) { 'EditMode' { 'editor' } 'PlayMode' { 'playmode' } default { 'all' } }
    try {
        $res = Invoke-UnityCliJson -CliArgs @("command", "test", "list", "--mode", $modeArg, "--project-path", $TargetProjectPath) -TimeoutSec 60
    } catch {
        return $null
    }
    $payload = Extract-CommandResultPayload -Envelope $res.Envelope
    if (-not ($payload -is [hashtable]) -or -not $payload.ContainsKey('Tests')) { return $null }

    $inc = @($IncludeCategories | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
    $exc = @($ExcludeCategories | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
    $excAsm = @($ExcludeAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })

    $tests = [System.Collections.Generic.List[hashtable]]::new()
    $groups = [ordered]@{}

    foreach ($t in $payload['Tests']) {
        if ($t -isnot [hashtable]) { continue }
        if ($t.ContainsKey('Explicit') -and [bool]$t['Explicit']) { continue }

        $fullName = [string]$t['FullName']
        $asm = [string]$t['Assembly']
        $mode = [string]$t['Mode']
        $cats = @($t['Categories'] | ForEach-Object { [string]$_ })

        if ($excAsm -contains $asm) { continue }
        if (-not (Test-InventoryNameMatch -FullName $fullName -Filter $TestFilter)) { continue }
        if ($inc.Count -gt 0 -and -not ($cats | Where-Object { $inc -contains $_ })) { continue }
        if ($exc.Count -gt 0 -and ($cats | Where-Object { $exc -contains $_ })) { continue }

        $tests.Add(@{ fullName = $fullName; mode = $mode; assembly = $asm; categories = $cats })

        # Fixture = full name without the parameter list and the method segment.
        $paren = $fullName.IndexOf('(')
        $head = if ($paren -ge 0) { $fullName.Substring(0, $paren) } else { $fullName }
        $dot = $head.LastIndexOf('.')
        $fixture = if ($dot -gt 0) { $head.Substring(0, $dot) } else { $head }

        $key = "$asm|$fixture"
        if (-not $groups.Contains($key)) {
            $groups[$key] = @{ assembly = $asm; fixture = $fixture; testCount = 0; categories = [System.Collections.Generic.List[string]]::new() }
        }
        $groups[$key].testCount++
        foreach ($c in $cats) {
            if ($c -ne 'Uncategorized' -and -not $groups[$key].categories.Contains($c)) { $groups[$key].categories.Add($c) }
        }
    }

    $fixtures = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($g in $groups.Values) {
        $fixtures.Add(@{ assembly = $g.assembly; fixture = $g.fixture; testCount = $g.testCount; categories = @($g.categories) })
    }
    return @{ Fixtures = $fixtures; Tests = $tests }
}

function Discover-TestsStatic {
    param(
        [string]$TargetProjectPath,
        [string]$SuiteName = 'All',
        [string]$TestFilter = '',
        [string[]]$IncludeCategories = @(),
        [string[]]$ExcludeCategories = @()
    )

    $results = [System.Collections.Generic.List[hashtable]]::new()
    $searchDirs = @(
        (Join-Path $TargetProjectPath "Assets"),
        (Join-Path $TargetProjectPath "Packages")
    )

    $incCats = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($c in $IncludeCategories) { if (-not [string]::IsNullOrWhiteSpace($c)) { [void]$incCats.Add($c.Trim()) } }
    $excCats = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($c in $ExcludeCategories) { if (-not [string]::IsNullOrWhiteSpace($c)) { [void]$excCats.Add($c.Trim()) } }

    $checkEdit = ($SuiteName -eq 'All' -or $SuiteName -eq 'EditMode')
    $checkPlay = ($SuiteName -eq 'All' -or $SuiteName -eq 'PlayMode')

    foreach ($dir in $searchDirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $csFiles = Get-ChildItem -LiteralPath $dir -Filter "*.cs" -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](PackageCache|Library|obj|bin|Temp)[\\/]' }

        foreach ($file in $csFiles) {
            $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
            if (-not $text -or ($text -notmatch '\[(?:Unity)?Test\]')) { continue }

            $isEditor = ($file.FullName -match '[\\/]Editor[\\/]' -or $file.FullName -match 'EditorTests')
            if ($isEditor -and -not $checkEdit) { continue }
            if (-not $isEditor -and -not $checkPlay -and -not $checkEdit) { continue }

            $asm = if ($isEditor) { "EditorTests" } else { "RuntimeTests" }
            $className = $file.BaseName
            if ($text -match 'class\s+(?<name>[A-Za-z0-9_]+)') {
                $className = $Matches['name']
            }

            $classCats = [System.Collections.Generic.List[string]]::new()
            $classCatMatches = [regex]::Matches($text, '\[Category\("(?<cat>[^"]+)"\)\]')
            foreach ($cm in $classCatMatches) {
                if (-not $classCats.Contains($cm.Groups['cat'].Value)) { $classCats.Add($cm.Groups['cat'].Value) }
            }

            $testCount = 0
            $methodMatches = [regex]::Matches($text, '\[(?:Unity)?Test(?:Attribute)?\](?:\s*\[[^\]]+\])*\s*(?:public|private|protected)?\s*(?:IEnumerator|void)\s+(?<mname>[A-Za-z0-9_]+)')
            foreach ($mm in $methodMatches) {
                $mname = $mm.Groups['mname'].Value
                if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
                    if ($mname -notmatch [regex]::Escape($TestFilter) -and $className -notmatch [regex]::Escape($TestFilter)) {
                        continue
                    }
                }
                if ($excCats.Count -gt 0 -and ($classCats | Where-Object { $excCats.Contains($_) })) {
                    continue
                }
                if ($incCats.Count -gt 0 -and -not ($classCats | Where-Object { $incCats.Contains($_) })) {
                    continue
                }
                $testCount++
            }

            if ($testCount -gt 0) {
                $results.Add(@{
                    assembly = $asm
                    fixture = $className
                    testCount = $testCount
                    categories = @($classCats)
                })
            }
        }
    }
    return $results
}
