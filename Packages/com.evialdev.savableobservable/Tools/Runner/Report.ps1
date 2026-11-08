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
