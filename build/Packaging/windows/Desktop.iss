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

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AI Client"; Filename: "{app}\AI.Desktop.exe"

[Run]
Filename: "{app}\AI.Desktop.exe"; Description: "Start AI Client"; Flags: nowait postinstall skipifsilent
