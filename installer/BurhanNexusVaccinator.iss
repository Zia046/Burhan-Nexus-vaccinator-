; Burhan Nexus Vaccinator installer (Inno Setup). Built by the GitHub Actions workflow.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{5D2C8E41-7A3B-4F6E-9C1D-8B4A2E6F0C73}
AppName=Burhan Nexus Vaccinator
AppVersion={#AppVersion}
AppPublisher=Burhan Nexus
AppPublisherURL=mailto:ziaullah.046@gmail.com
DefaultDirName={autopf}\Burhan Nexus Vaccinator
DefaultGroupName=Burhan Nexus Vaccinator
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=BurhanNexus-Vaccinator-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\BurhanNexus.Vaccinator.exe
WizardImageFile=..\assets\wizard-large.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
LicenseFile=..\LICENSE.txt
AppCopyright=Copyright (c) Burhan Nexus
AppContact=03475093446
VersionInfoCompany=Burhan Nexus
VersionInfoDescription=Burhan Nexus Vaccinator Setup
UninstallDisplayName=Burhan Nexus Vaccinator

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Database, backups and logs. Every Windows user of this computer must be able to write here.
Name: "{commonappdata}\BurhanNexus\Vaccinator"; Permissions: users-modify

[Icons]
Name: "{group}\Burhan Nexus Vaccinator"; Filename: "{app}\BurhanNexus.Vaccinator.exe"
Name: "{autodesktop}\Burhan Nexus Vaccinator"; Filename: "{app}\BurhanNexus.Vaccinator.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BurhanNexus.Vaccinator.exe"; Description: "Start Burhan Nexus Vaccinator"; Flags: nowait postinstall skipifsilent

; Uninstalling removes the program only. Patient data in C:\ProgramData\BurhanNexus\Vaccinator is never deleted automatically.
