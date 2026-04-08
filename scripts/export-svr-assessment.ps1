param(
    [string]$Session = "",
    [string]$Output = "",
    [ValidateSet("none", "template", "desktop-gemma")]
    [string]$Narrative = "none"
)

$projectPath = "backend/SimulationAssessment.Cli/SimulationAssessment.Cli.csproj"
$arguments = @("run", "--project", $projectPath, "--")

if ([string]::IsNullOrWhiteSpace($Session)) {
    $arguments += "--latest"
}
else {
    $arguments += "--session"
    $arguments += $Session
}

if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $arguments += "--output"
    $arguments += $Output
}

if (-not [string]::IsNullOrWhiteSpace($Narrative)) {
    $arguments += "--narrative"
    $arguments += $Narrative
}

dotnet @arguments
