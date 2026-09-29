[Setup]
AppId={{01B12F46-AC3D-4268-B0F9-2094CC2DB504}
AppName=AI Client Desktop
AppVersion={#Version}
DefaultDirName={localappdata}\Programs\AI Client Desktop
DefaultGroupName=AI Client
PrivilegesRequired=lowest
ArchitecturesAllowed={#Architecture}
OutputDir={#OutputDir}
OutputBaseFilename={#BaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Files]
Source: "{#SourceDir}\stop-installed-app.ps1"; Flags: dontcopy noencryption
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AI Client"; Filename: "{app}\AI.Desktop.exe"

[Run]
Filename: "{app}\AI.Desktop.exe"; Description: "Start AI Client"; Flags: nowait postinstall skipifsilent

[Code]
function StopDesktop(ScriptPath: String): Boolean;
var
  ExitCode: Integer;
  Arguments: String;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" "' + ExpandConstant('{app}') + '" AI.Desktop.exe';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('stop-installed-app.ps1');
  if StopDesktop(ExpandConstant('{tmp}\stop-installed-app.ps1')) then
    Result := ''
  else
    Result := 'Close AI Client Desktop before continuing.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and
    not StopDesktop(ExpandConstant('{app}\stop-installed-app.ps1')) then
  begin
    MsgBox('Close AI Client Desktop before uninstalling it.', mbError, MB_OK);
    Abort;
  end;
end;
