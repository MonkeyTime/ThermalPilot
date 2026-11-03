param([switch]$SkipPublish, [string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not $SkipPublish) { & ./scripts/publish.ps1 }
    if (-not $CompilerPath) {
        $compilerCandidates = @(
            (Join-Path $projectRoot '.tools/InnoSetup/ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
        )
        $CompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $CompilerPath) { throw 'Installer Inno Setup 6 ou préciser -CompilerPath vers ISCC.exe.' }
    $appPath = Join-Path $projectRoot 'dist/ThermalPilot-win-x64/ThermalPilot.exe'
    if (-not (Test-Path -LiteralPath $appPath)) { throw 'Publier Thermal Pilot avant de compiler son installeur.' }
    $appVersion = (Get-Item -LiteralPath $appPath).VersionInfo.ProductVersion.Split('+')[0]
    & $CompilerPath /Qp "/DAppVersion=$appVersion" installer/ThermalPilot.iss
    if ($LASTEXITCODE -ne 0) { throw "La compilation de l’installeur a échoué." }
    $setupPath = Join-Path $projectRoot "dist/ThermalPilot-$appVersion-Setup-x64.exe"
    Get-FileHash -LiteralPath $setupPath -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($setupPath))" } |
        Set-Content -LiteralPath ($setupPath + '.sha256')
    Write-Host "Installeur prêt : $setupPath"
}
finally { Pop-Location }
