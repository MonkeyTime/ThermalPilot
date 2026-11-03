param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdkPath = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $sdkPath) { $sdkPath } else { 'dotnet' }
Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        & $dotnetCommand test tests/ThermalPilot.Tests/ThermalPilot.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Les tests ont échoué.' }
    }
    & $dotnetCommand publish src/ThermalPilot/ThermalPilot.csproj -c Release -r win-x64 --self-contained true -o dist/ThermalPilot-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
    Copy-Item -LiteralPath README.md -Destination dist/ThermalPilot-win-x64/README.md
    Copy-Item -LiteralPath LISEZ-MOI.txt -Destination dist/ThermalPilot-win-x64/LISEZ-MOI.txt
    Copy-Item -LiteralPath licenses -Destination dist/ThermalPilot-win-x64 -Recurse -Force
    Compress-Archive -Path dist/ThermalPilot-win-x64/* -DestinationPath dist/ThermalPilot-win-x64.zip -Force
    Get-FileHash -LiteralPath dist/ThermalPilot-win-x64.zip -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  ThermalPilot-win-x64.zip" } |
        Set-Content -LiteralPath dist/ThermalPilot-win-x64.zip.sha256
    Write-Host 'Distribution prête dans dist/ThermalPilot-win-x64.zip'
}
finally { Pop-Location }
