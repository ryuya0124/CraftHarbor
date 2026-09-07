#ifndef AppVersion
  #error AppVersion must be provided by package.ps1
#endif

[Setup]
AppId={{A3AF1289-728F-4FB3-A791-EC7DDD897C14}
AppName=CraftHarbor
AppVersion={#AppVersion}
AppPublisher=CraftHarbor
AppPublisherURL=https://github.com/ryuya0124/CraftHarbor
DefaultDirName={localappdata}\Programs\CraftHarbor
DefaultGroupName=CraftHarbor
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\CraftHarbor.exe
SetupIconFile=..\src\CraftHarbor.Desktop\Assets\CraftHarbor.ico
OutputDir=..\artifacts\packages
OutputBaseFilename=CraftHarbor-{#AppVersion}-win-x64-setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=no
AllowNoIcons=no
AppMutex=Local\CraftHarbor.Desktop
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\installed\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CraftHarbor"; Filename: "{app}\CraftHarbor.exe"; WorkingDir: "{app}"
Name: "{group}\CraftHarbor をアンインストール"; Filename: "{uninstallexe}"

; Server data belongs to Documents\CraftHarbor\data, outside {app}.
; No Run/UninstallDelete hooks: never launch/stop Java or remove server data.
