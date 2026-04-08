param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

dotnet run --project "backend/SimulationAssessment.Tests/SimulationAssessment.Tests.csproj"

if ($LASTEXITCODE -ne 0) {
    throw "SVR narrative regression failed."
}
