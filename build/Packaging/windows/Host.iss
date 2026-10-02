#define ProductName "AI Client Host"
[Setup]
AppId={{C7863D98-C348-498B-B011-40659EE4A489}
AppName={#ProductName}
AppVersion={#Version}
DefaultDirName={localappdata}\Programs\AI Client Host
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
Name: "main"; Description: "AI Client Host"; Types: full standard custom; Flags: fixed
Name: "csharp"; Description: "C# scripting tools (optional, compiles and runs scripts with Roslyn)"; Types: full

[InstallDelete]
Type: filesandordirs; Name: "{app}\mcp-csharp"; Components: main

[Icons]
Name: "{autoprograms}\AI Client in browser"; Filename: "{app}\AI.Host.exe"; Parameters: "open"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-host-task.ps1"" ""{app}"""; Flags: runhidden waituntilterminated; Check: not IsUpdate
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-host-task.ps1"" ""{app}"" -NoStart"; Flags: runhidden waituntilterminated; Check: IsUpdate
Filename: "{app}\AI.Host.exe"; Parameters: "open"; Description: "Open AI Client in the browser"; Flags: nowait postinstall skipifsilent

[Code]
#include "ApplicationBranding.iss"

function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function StopHost(ScriptPath: String; RemoveTask: Boolean): Boolean;
var
  ExitCode: Integer;
  Arguments: String;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" "' + ExpandConstant('{app}') + '" AI.Host.exe -TaskName AI.Client.Host';
  if RemoveTask then
    Arguments := Arguments + ' -UnregisterTask';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('stop-installed-app.ps1');
  if StopHost(ExpandConstant('{tmp}\stop-installed-app.ps1'), False) then
    Result := ''
  else
    Result := 'Could not stop the installed AI Client Host. Close it and retry.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and
    not StopHost(ExpandConstant('{app}\stop-installed-app.ps1'), True) then
  begin
    MsgBox('Could not stop AI Client Host. Close it and retry.', mbError, MB_OK);
    Abort;
  end;
end;
