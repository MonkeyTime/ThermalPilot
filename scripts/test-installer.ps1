param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{5FD86D77-0981-4BC7-ABDC-7D0212E8E109}_is1'
if (Test-Path -LiteralPath $registryPath) { throw 'Une installation existe déjà : test annulé pour la préserver.' }
if (-not $InstallerPath) {
    $InstallerPath = (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist') -Filter 'ThermalPilot-*-Setup-x64.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $InstallerPath) { throw 'Installeur introuvable.' }
$testRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/installer-smoke'))
$installation = Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

function Invoke-TestProcess([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Délai dépassé : $Path" }
    if ($process.ExitCode -ne 0) { throw "Échec ($($process.ExitCode)) : $Path" }
}

$portableExe = Join-Path $projectRoot 'dist/ThermalPilot-win-x64/ThermalPilot.exe'
$diagnosticPath = Join-Path $testRoot 'before.json'
Invoke-TestProcess $portableExe @('--diagnostics', ('"' + $diagnosticPath + '"'))
$before = Get-Content -LiteralPath $diagnosticPath -Raw | ConvertFrom-Json
if ($before.JournalPresent -or $before.StartupTask.Error -or
    ($before.StartupTask.Enabled -and $before.StartupTask.Executable -eq (Join-Path $installation 'ThermalPilot.exe'))) {
    throw 'Une session ou tâche de démarrage existe, ou sa vérification a échoué : test annulé pour la préserver.'
}
$settingsPath = Join-Path $env:LOCALAPPDATA 'ThermalPilot/settings.json'
$savedSettings = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$uninstaller = Join-Path $installation 'unins000.exe'
try {
    $setupArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""',
        ('/DIR="' + $installation + '"'), ('/LOG="' + (Join-Path $testRoot 'install.log') + '"'))
    Invoke-TestProcess $InstallerPath $setupArguments
    if (-not (Test-Path -LiteralPath $registryPath)) { throw 'Entrée de désinstallation absente.' }
    $registered = Get-ItemProperty -LiteralPath $registryPath
    if ($registered.InstallLocation.TrimEnd('\') -ne $installation.TrimEnd('\')) { throw 'Dossier de test inattendu.' }
    $installedExe = Join-Path $installation 'ThermalPilot.exe'
    if ((Get-FileHash -LiteralPath $installedExe).Hash -ne (Get-FileHash -LiteralPath $portableExe).Hash) { throw 'Exécutable installé incorrect.' }
    Invoke-TestProcess $installedExe @('--demo', '--capture', ('"' + (Join-Path $testRoot 'installed-preview.png') + '"'))
    Invoke-TestProcess $InstallerPath $setupArguments
    Write-Output 'Installation, lancement du programme et mise à jour réussis.'
}
finally {
    if (Test-Path -LiteralPath $uninstaller) {
        Invoke-TestProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"'))
    }
    # Restore the developer preferences after exercising uninstall cleanup.
    if ($null -ne $savedSettings) { [IO.File]::WriteAllBytes($settingsPath, $savedSettings) }
}
if (Test-Path -LiteralPath $registryPath) { throw 'Entrée de désinstallation restante.' }
if (Test-Path -LiteralPath (Join-Path $installation 'ThermalPilot.exe')) { throw 'Application encore installée.' }
Invoke-TestProcess $portableExe @('--diagnostics', ('"' + (Join-Path $testRoot 'after.json') + '"'))
$after = Get-Content -LiteralPath (Join-Path $testRoot 'after.json') -Raw | ConvertFrom-Json
if ($after.StartupTask.Enabled -ne $before.StartupTask.Enabled -or $after.StartupTask.Executable -ne $before.StartupTask.Executable) {
    throw 'La tâche de la copie existante a changé.'
}
Write-Output 'Désinstallation réussie ; entrée Windows retirée, préférences conservées.'
