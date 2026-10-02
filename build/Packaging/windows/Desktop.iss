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
WizardStyle=modern dynamic
SetupIconFile=..\..\..\src\AI.Desktop\Assets\app-icon.ico
WizardImageFile=..\..\..\src\AI.Desktop\Assets\app-icon.png
WizardSmallImageFile=..\..\..\src\AI.Desktop\Assets\app-icon.png
CloseApplications=yes

[Files]
Source: "{#SourceDir}\stop-installed-app.ps1"; Flags: dontcopy noencryption
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "mcp-csharp\*"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: main
Source: "{#CSharpSourceDir}\*"; DestDir: "{app}\mcp-csharp"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: csharp

[Types]
Name: "standard"; Description: "Standard installation"
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main"; Description: "AI Client Desktop"; Types: full standard custom; Flags: fixed
Name: "csharp"; Description: "C# scripting tools (optional, compiles and runs scripts with Roslyn)"; Types: full

[InstallDelete]
Type: filesandordirs; Name: "{app}\mcp-csharp"; Components: main

[Icons]
Name: "{autoprograms}\AI Client"; Filename: "{app}\AI.Desktop.exe"

[Run]
Filename: "{app}\AI.Desktop.exe"; Description: "Start AI Client"; Flags: nowait postinstall skipifsilent

[Code]
#include "ApplicationBranding.iss"

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
