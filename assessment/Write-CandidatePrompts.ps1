$ErrorActionPreference = 'Stop'
$template = Get-Content (Join-Path $PSScriptRoot 'Candidate-Prompt.md') -Raw
$template = $template.Replace("`r`n", "`n")
$start = $template.IndexOf('Your handover identifies one starting point.')
if ($start -lt 0) { throw 'The shared candidate brief has no stage-selection section.' }
$end = $template.IndexOf('1. Complete the remaining migration work', $start)
if ($start -lt 0 -or $end -le $start) {
    throw 'The shared candidate brief has no recognized stage-selection section.'
}
$stages = [ordered]@{
    S0 = 'Your starting point is **S0: the original mixed .NET Framework application with production VB**. Normalize application and test projects to net472, convert production VB to C#, then establish SDK-style net472 before targeting .NET 10. The quality and workflow requirements below also remain.'
    S1 = 'Your starting point is **S1: net472 with production VB**. Framework normalization is already complete. Convert production VB to C#, then establish SDK-style net472 before targeting .NET 10. The quality and workflow requirements below also remain.'
    S2 = 'Your starting point is **S2: net472 with production C#**. Production language conversion is already complete; no VB converter or production language-conversion deliverable is requested. Establish SDK-style net472 before targeting .NET 10. The quality and workflow requirements below also remain.'
    S2a = 'Your starting point is **S2a: SDK-style net472 with production C#**. Production language and project-style conversion are already complete; do not repeat them. Target .NET 10 and complete the quality and workflow requirements below. No VB converter is requested.'
    S3 = 'Your starting point is **S3: SDK-style .NET 10 with production C#**. Language, project-style and framework migration are already complete. Complete the quality and workflow requirements below without repeating those migrations. No migration utility or VB converter is requested.'
}
foreach ($stage in $stages.Keys) {
    $text = $template.Substring(0, $start) + $stages[$stage] + "`n`n" + $template.Substring($end)
    $text = $text.Replace('# Task-o-Time: maintainable desktop delivery',
        "# Task-o-Time: maintainable desktop delivery ($stage)")
    if ($stage -eq 'S3') {
        $first = $text.IndexOf('1. Complete the remaining migration work')
        $second = $text.IndexOf('2. Keep the original main window', $first)
        $text = $text.Substring(0, $first) + @'
1. Keep the existing SDK-style C# .NET 10 solution buildable and preserve its
   supported target frameworks. No language or project migration is required.
   Existing VB test projects may remain VB; all test targets must stay compatible
   with the solution. Do not introduce a second production VB implementation.

'@ + $text.Substring($second)
        $text = $text.Replace(
            'Provide working code, resource files, reusable transformations for applicable migration work,',
            'Provide working code and resource files,')
        $text = [regex]::Replace($text, '(?s)9\. Similar applications.*?(?=\n## Boundaries)', @'
9. Similar applications will follow this handover. Consider whether repeatable
   quality changes can be made reproducible so later projects need less repeated
   analysis and effort. Explain the trade-off and leave evidence that the
   approach works. This is a design consideration, not a requirement to build
   a migration utility. Distinguish mechanical changes from architectural judgment.

'@)
    }
    else {
        $text = [regex]::Replace($text,
            ' For S3 this is a design consideration for\n   remaining repetitive quality work, not a requirement to build a converter\.', '')
    }
    $text = $text.Replace("`r`n", "`n")
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot "Candidate-Prompt.$stage.md"), $text,
        [Text.UTF8Encoding]::new($false))
}
