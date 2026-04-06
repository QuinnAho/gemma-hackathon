param(
    [string]$ConfigPath = "config/local.json",
    [string]$SourcePath,
    [string]$TargetPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Get-ConfigValue {
    param(
        $ConfigObject,
        [string]$PropertyPath,
        [string]$DefaultValue
    )

    if ($null -eq $ConfigObject) {
        return $DefaultValue
    }

    $current = $ConfigObject
    foreach ($segment in $PropertyPath.Split(".")) {
        if ($null -eq $current -or -not ($current.PSObject.Properties.Name -contains $segment)) {
            return $DefaultValue
        }

        $current = $current.$segment
    }

    if ([string]::IsNullOrWhiteSpace([string]$current)) {
        return $DefaultValue
    }

    return [string]$current
}

$resolvedConfigPath = Resolve-RepoPath $ConfigPath
$config = $null

if (Test-Path -LiteralPath $resolvedConfigPath) {
    $config = Get-Content -LiteralPath $resolvedConfigPath -Raw | ConvertFrom-Json
}

if (-not $SourcePath) {
    $SourcePath = Get-ConfigValue $config "paths.cactusAndroidPluginSource" ".local/native/cactus/android/arm64-v8a/libcactus.so"
}

if (-not $TargetPath) {
    $TargetPath = Get-ConfigValue $config "unity.androidPluginTarget" "unity/sim/Assets/Plugins/Android/arm64-v8a/libcactus.so"
}

$resolvedSourcePath = Resolve-RepoPath $SourcePath
$resolvedTargetPath = Resolve-RepoPath $TargetPath

if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
    throw "Plugin source not found: $resolvedSourcePath"
}

if (-not $resolvedTargetPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin target must stay inside the repo: $resolvedTargetPath"
}

$targetDirectory = Split-Path -Parent $resolvedTargetPath
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null

Copy-Item -LiteralPath $resolvedSourcePath -Destination $resolvedTargetPath -Force

Write-Host "Staged libcactus.so"
Write-Host "  Source: $resolvedSourcePath"
Write-Host "  Target: $resolvedTargetPath"
Write-Host "The target binary stays untracked through unity/sim/.gitignore."
