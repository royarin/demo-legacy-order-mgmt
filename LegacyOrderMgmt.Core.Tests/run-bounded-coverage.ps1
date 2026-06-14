param(
    [decimal]$MinCoverage = 25,
    [decimal]$MaxCoverage = 30
)

$ErrorActionPreference = "Stop"

$coverageDir = Join-Path $PSScriptRoot "TestResults\coverage"
if (Test-Path $coverageDir) {
    Remove-Item $coverageDir -Recurse -Force
}

dotnet test (Join-Path $PSScriptRoot "LegacyOrderMgmt.Core.Tests.csproj") --nologo `
    /p:CollectCoverage=true `
    /p:CoverletOutput="$coverageDir\" `
    /p:CoverletOutputFormat=json `
    /p:Include="[LegacyOrderMgmt.Core]LegacyOrderMgmt.Core.Services.*" `
    /p:Threshold=$MinCoverage `
    /p:ThresholdType=line `
    /p:ThresholdStat=total

$coverageFile = Join-Path $coverageDir "coverage.json"
if (!(Test-Path $coverageFile)) {
    throw "Coverage file not found: $coverageFile"
}

$coverage = Get-Content $coverageFile -Raw | ConvertFrom-Json
$lineTotal = 0
$lineCovered = 0

foreach ($module in $coverage.PSObject.Properties.Value) {
    foreach ($document in $module.PSObject.Properties.Value) {
        foreach ($class in $document.PSObject.Properties.Value) {
            foreach ($method in $class.PSObject.Properties.Value) {
                if ($method.Lines) {
                    foreach ($line in $method.Lines.PSObject.Properties) {
                        $lineTotal++
                        if ([int]$line.Value -gt 0) {
                            $lineCovered++
                        }
                    }
                }
            }
        }
    }
}

if ($lineTotal -eq 0) {
    throw "No lines found in coverage report."
}

$lineCoverage = [math]::Round(($lineCovered / $lineTotal) * 100, 2)
Write-Host ("Line coverage: {0}%" -f $lineCoverage)

if ($lineCoverage -gt $MaxCoverage) {
    throw ("Coverage {0}% exceeds maximum cap of {1}%." -f $lineCoverage, $MaxCoverage)
}

if ($lineCoverage -lt $MinCoverage) {
    throw ("Coverage {0}% is below minimum target of {1}%." -f $lineCoverage, $MinCoverage)
}
