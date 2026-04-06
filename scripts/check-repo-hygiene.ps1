param()

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$pathsToCheck = @(
    ".local/native/cactus/android/arm64-v8a/libcactus.so",
    ".local/models/gemma-4-e2b/model.bin",
    ".local/logs/telemetry/session.log",
    ".local/benchmarks/desktop.json",
    ".local/audio/fixtures/sample.wav",
    "config/local.json",
    "unity/sim/Assets/Plugins/Android/arm64-v8a/libcactus.so",
    "unity/sim/Assets/Plugins/Android/arm64-v8a/libcactus.so.meta"
)

$hasFailure = $false

foreach ($relativePath in $pathsToCheck) {
    & git -C $repoRoot check-ignore -q -- $relativePath
    $isIgnored = $LASTEXITCODE -eq 0

    $trackedOutput = & git -C $repoRoot ls-files -- $relativePath 2>$null
    $isTracked = -not [string]::IsNullOrWhiteSpace(($trackedOutput -join ""))

    if (-not $isIgnored) {
        Write-Host "[fail] Not ignored: $relativePath"
        $hasFailure = $true
        continue
    }

    if ($isTracked) {
        Write-Host "[fail] Tracked but should be local-only: $relativePath"
        $hasFailure = $true
        continue
    }

    Write-Host "[ok] $relativePath"
}

if ($hasFailure) {
    throw "Repo hygiene checks failed."
}

Write-Host "Repo hygiene checks passed."
