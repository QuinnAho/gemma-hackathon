param(
    [string]$Session = "",
    [string]$Output = ""
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

dotnet @arguments
