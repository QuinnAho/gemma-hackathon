param(
    [string]$Fixture = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot "backend/fixtures/svr"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".local/exports/svr-regression"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$fixtureDirs =
    if ([string]::IsNullOrWhiteSpace($Fixture)) {
        Get-ChildItem -Path $fixtureRoot -Directory | Sort-Object Name
    }
    else {
        $selected = Join-Path $fixtureRoot $Fixture
        if (-not (Test-Path $selected)) {
            throw "Fixture '$Fixture' was not found under $fixtureRoot"
        }

        @(Get-Item $selected)
    }

$results = New-Object System.Collections.Generic.List[object]

foreach ($fixtureDir in $fixtureDirs) {
    $fixtureName = $fixtureDir.Name
    $sessionPath = $fixtureDir.FullName
    $exportPath = Join-Path $OutputRoot $fixtureName
    $expectedPath = Join-Path $fixtureDir.FullName "expected.json"

    if (-not (Test-Path $expectedPath)) {
        throw "Missing expected.json for fixture '$fixtureName'"
    }

    if (Test-Path $exportPath) {
        Remove-Item -LiteralPath $exportPath -Recurse -Force
    }

    dotnet run --project "backend/SimulationAssessment.Cli/SimulationAssessment.Cli.csproj" -- --session $sessionPath --output $exportPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Assessment export failed for fixture '$fixtureName'"
    }

    $expected = Get-Content -Path $expectedPath -Raw | ConvertFrom-Json
    $assessment = Get-Content -Path (Join-Path $exportPath "assessment.json") -Raw | ConvertFrom-Json
    $timeline = Get-Content -Path (Join-Path $exportPath "timeline.json") -Raw | ConvertFrom-Json
    $exportManifestPath = Join-Path $exportPath "export-manifest.json"
    $afterActionPath = Join-Path $exportPath "after-action-report.md"

    $failures = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path $exportManifestPath)) {
        $failures.Add("missing export-manifest.json")
    }

    if (-not (Test-Path $afterActionPath)) {
        $failures.Add("missing after-action-report.md")
    }

    $exportManifest = $null
    if (Test-Path $exportManifestPath) {
        $exportManifest = Get-Content -Path $exportManifestPath -Raw | ConvertFrom-Json
    }

    $afterAction = ""
    if (Test-Path $afterActionPath) {
        $afterAction = Get-Content -Path $afterActionPath -Raw
    }

    if ($assessment.Input.SessionState -ne $expected.session_state) {
        $failures.Add("session_state expected '$($expected.session_state)' but found '$($assessment.Input.SessionState)'")
    }

    if ($assessment.Input.Phase -ne $expected.phase) {
        $failures.Add("phase expected '$($expected.phase)' but found '$($assessment.Input.Phase)'")
    }

    $actualLocation = $assessment.Input.TextFacts.'participant.location'
    if ($actualLocation -ne $expected.participant_location) {
        $failures.Add("participant_location expected '$($expected.participant_location)' but found '$actualLocation'")
    }

    if ([int]$assessment.Result.TotalPoints -ne [int]$expected.total_points) {
        $failures.Add("total_points expected '$($expected.total_points)' but found '$($assessment.Result.TotalPoints)'")
    }

    if ([int]$assessment.Result.MaxPoints -ne [int]$expected.max_points) {
        $failures.Add("max_points expected '$($expected.max_points)' but found '$($assessment.Result.MaxPoints)'")
    }

    if ($assessment.Result.Band -ne $expected.band) {
        $failures.Add("band expected '$($expected.band)' but found '$($assessment.Result.Band)'")
    }

    $timelineCount = @($timeline).Count
    if ($timelineCount -ne [int]$expected.timeline_count) {
        $failures.Add("timeline_count expected '$($expected.timeline_count)' but found '$timelineCount'")
    }

    $actualCriticalFailures = @($assessment.Result.CriticalFailures)
    $expectedCriticalFailures = @($expected.critical_failures)
    $sortedExpectedCriticalFailures = @($expectedCriticalFailures | Sort-Object)
    $sortedActualCriticalFailures = @($actualCriticalFailures | Sort-Object)
    if ((Compare-Object -ReferenceObject $sortedExpectedCriticalFailures -DifferenceObject $sortedActualCriticalFailures)) {
        $failures.Add("critical_failures mismatch")
    }

    $actualDeficitIds = @($assessment.Result.Deficits | ForEach-Object { $_.Id })
    $expectedDeficitIds = @($expected.deficit_ids)
    $sortedExpectedDeficitIds = @($expectedDeficitIds | Sort-Object)
    $sortedActualDeficitIds = @($actualDeficitIds | Sort-Object)
    if ((Compare-Object -ReferenceObject $sortedExpectedDeficitIds -DifferenceObject $sortedActualDeficitIds)) {
        $failures.Add("deficit_ids mismatch")
    }

    $expectedMetricNames = @($expected.metric_scores.PSObject.Properties | ForEach-Object { $_.Name })
    $actualMetricNames = @($assessment.Result.MetricScores.PSObject.Properties | ForEach-Object { $_.Name })
    $sortedExpectedMetricNames = @($expectedMetricNames | Sort-Object)
    $sortedActualMetricNames = @($actualMetricNames | Sort-Object)
    if ((Compare-Object -ReferenceObject $sortedExpectedMetricNames -DifferenceObject $sortedActualMetricNames)) {
        $failures.Add("metric score key mismatch")
    }
    else {
        foreach ($metricName in $expectedMetricNames) {
            $expectedMetricValue = [int]$expected.metric_scores.$metricName
            $actualMetricValue = [int]$assessment.Result.MetricScores.$metricName
            if ($expectedMetricValue -ne $actualMetricValue) {
                $failures.Add("metric '$metricName' expected '$expectedMetricValue' but found '$actualMetricValue'")
            }
        }
    }

    $summary = [string]$assessment.Report.Summary
    if (-not $summary.Contains([string]$expected.summary_contains)) {
        $failures.Add("summary missing expected fragment '$($expected.summary_contains)'")
    }

    if ($null -ne $exportManifest) {
        if ($exportManifest.SchemaVersion -ne "svr.assessment.export-manifest.v1") {
            $failures.Add("export manifest schema version mismatch")
        }

        if ([bool]$exportManifest.HasNarrative) {
            $failures.Add("export manifest unexpectedly reported a narrative payload")
        }

        $expectedArtifactIds = @(
            "assessment",
            "timeline",
            "deterministic_report",
            "session_package",
            "review_summary",
            "after_action_report",
            "export_manifest"
        )
        $actualArtifactIds = @($exportManifest.Artifacts | ForEach-Object { $_.Id })
        if ((Compare-Object -ReferenceObject ($expectedArtifactIds | Sort-Object) -DifferenceObject ($actualArtifactIds | Sort-Object))) {
            $failures.Add("export manifest artifact inventory mismatch")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($afterAction)) {
        if (-not $afterAction.Contains("# SVR Fire After-Action Report")) {
            $failures.Add("after-action report missing title")
        }

        $expectedScoreLine = "Score: $($assessment.Result.TotalPoints)/$($assessment.Result.MaxPoints) ($($assessment.Result.Band))"
        if (-not $afterAction.Contains($expectedScoreLine)) {
            $failures.Add("after-action report missing expected score line")
        }

        if (-not $afterAction.Contains([string]$expected.summary_contains)) {
            $failures.Add("after-action report missing expected summary fragment '$($expected.summary_contains)'")
        }

        if (-not $afterAction.Contains("No optional narrative addendum was exported.")) {
            $failures.Add("after-action report missing no-narrative note")
        }
    }

    $result = [pscustomobject]@{
        fixture = $fixtureName
        passed = ($failures.Count -eq 0)
        score = [int]$assessment.Result.TotalPoints
        band = [string]$assessment.Result.Band
        failures = @($failures)
        export_path = $exportPath
    }

    $results.Add($result) | Out-Null
}

$summaryJson = Join-Path $OutputRoot "summary.json"
$summaryTxt = Join-Path $OutputRoot "summary.txt"

$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryJson -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("SVR Assessment Regression Summary") | Out-Null
$lines.Add("Output Root: $OutputRoot") | Out-Null

foreach ($result in $results) {
    $status = if ($result.passed) { "PASS" } else { "FAIL" }
    $lines.Add("$status $($result.fixture) score=$($result.score) band=$($result.band)") | Out-Null
    foreach ($failure in $result.failures) {
        $lines.Add("  - $failure") | Out-Null
    }
}

$lines | Set-Content -Path $summaryTxt -Encoding UTF8
Get-Content -Path $summaryTxt

if ((@($results | Where-Object { -not $_.passed })).Count -gt 0) {
    exit 1
}
