param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

dotnet build "backend/SimulationAssessment.Backend/SimulationAssessment.Backend.csproj"
if ($LASTEXITCODE -ne 0) {
    throw "SVR backend library build failed."
}

dotnet run --project "backend/SimulationAssessment.SvrFire.Tests/SimulationAssessment.SvrFire.Tests.csproj"
if ($LASTEXITCODE -ne 0) {
    throw "SVR scenario-layer backend tests failed."
}

dotnet run --project "backend/SimulationAssessment.Tests/SimulationAssessment.Tests.csproj"
if ($LASTEXITCODE -ne 0) {
    throw "SVR backend host tests failed."
}

dotnet build "backend/SimulationAssessment.Cli/SimulationAssessment.Cli.csproj" --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "SVR CLI build failed."
}
