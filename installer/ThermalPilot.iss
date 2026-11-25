#ifndef AppVersion
  #define AppVersion "0.3.4"
#endif
#define AppName "Thermal Pilot"
#define AppExe "ThermalPilot.exe"

[Setup]
AppId={{5FD86D77-0981-4BC7-ABDC-7D0212E8E109}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Thermal Pilot
DefaultDirName={localappdata}\Programs\ThermalPilot
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
DisableProgramGroupPage=yes
DisableDirPage=no
WizardStyle=modern
WizardSizePercent=110
SetupIconFile=..\src\ThermalPilot\Assets\ThermalPilot.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
InfoBeforeFile=avant-installation.txt
OutputDir=..\dist
OutputBaseFilename=ThermalPilot-{#AppVersion}-Setup-x64
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Installation de Thermal Pilot
VersionInfoProductName={#AppName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "..\dist\ThermalPilot-win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Ouvrir Thermal Pilot"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "https://github.com/namazso/PawnIO.Setup/releases/latest"; Description: "Ouvrir la page officielle de PawnIO (installation séparée)"; Flags: shellexec nowait postinstall unchecked skipifsilent runasoriginaluser; Check: not PawnIOInstalled

[Code]
function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO') or
    RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--install-check --quiet', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := 'Ferme Thermal Pilot depuis son icône près de l’horloge, puis réessaie. Si les fichiers sont endommagés, choisis un autre dossier d’installation.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
  Args: String;
  Cleaned: Boolean;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
  begin
    Args := '--uninstall-cleanup';
    if UninstallSilent then Args := Args + ' --quiet';
    Cleaned := Exec(ExpandConstant('{app}\{#AppExe}'), Args, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
    if not Cleaned then
    begin
      SuppressibleMsgBox('La désinstallation est interrompue. Ferme Thermal Pilot depuis son icône près de l’horloge, puis réessaie. Si une tâche de démarrage doit être supprimée, autorise le nettoyage administrateur.', mbError, MB_OK, IDOK);
      Abort;
    end;
  end;
end;
