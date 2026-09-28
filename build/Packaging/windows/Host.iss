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
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AI Client in browser"; Filename: "{app}\AI.Host.exe"; Parameters: "open"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-host-task.ps1"" ""{app}"""; Flags: runhidden waituntilterminated
Filename: "{app}\AI.Host.exe"; Parameters: "open"; Description: "Open AI Client in the browser"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-host-task.ps1"""; Flags: runhidden waituntilterminated
