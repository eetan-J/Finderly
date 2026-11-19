[Setup]
AppName=Finderly
AppVersion=1.0.1
AppPublisher=Finderly
DefaultDirName={autopf}\Finderly
DefaultGroupName=Finderly
OutputDir=installer
OutputBaseFilename=Finderly-Setup
SetupIconFile=Assets\Finderly.ico
UninstallDisplayIcon={app}\Finderly.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: folderassoc; Description: "Add ""Open with Finderly"" to folders"; Flags: unchecked
Name: previewassoc; Description: "Add ""Open with Preview"" for images and PDFs"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Finderly"; Filename: "{app}\Finderly.exe"
Name: "{autodesktop}\Finderly"; Filename: "{app}\Finderly.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Directory\shell\Finderly"; ValueType: string; ValueData: "Open with Finderly"; Tasks: folderassoc; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\Finderly\command"; ValueType: string; ValueData: """{app}\Finderly.exe"" ""%1"""; Tasks: folderassoc
Root: HKA; Subkey: "Software\Classes\*\shell\FinderlyPreview"; ValueType: string; ValueData: "Open with Preview"; Tasks: previewassoc; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\*\shell\FinderlyPreview\command"; ValueType: string; ValueData: """{app}\Finderly.exe"" ""%1"""; Tasks: previewassoc

[Run]
Filename: "{app}\Finderly.exe"; Description: "Launch Finderly"; Flags: nowait postinstall skipifsilent

