#ifdef Validation
  #define StudioId "{{946DF611-BF0E-45FC-9C90-E832E151DCB4}"
  #define StudioName "TwitchGrab Studio (validation 7.1)"
  #define ProjectProgId "TwitchGrabStudio.Validation71"
#else
  #define StudioId "{{D53B79B1-6254-4C46-93CD-7D52B8FA3795}"
  #define StudioName "TwitchGrab Studio"
  #define ProjectProgId "TwitchGrabStudio.Project"
#endif
[Setup]
AppId={#StudioId}
AppName={#StudioName}
AppVersion=7.1.0
AppVerName=TwitchGrab Studio 7.1
AppPublisher=sliks37
AppPublisherURL=https://github.com/slikscs2
AppSupportURL=https://t.me/sliksy37
DefaultDirName={localappdata}\Programs\TwitchGrab Studio
DefaultGroupName=TwitchGrab Studio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
#ifdef Validation
AppMutex=TwitchGrabStudioValidation71
#else
AppMutex=TwitchGrabStudio
#endif
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
DisableDirPage=no
DisableProgramGroupPage=yes
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\TwitchGrab-Studio.exe
OutputDir=dist
OutputBaseFilename=TwitchGrab-Studio-Setup-v7.1
Compression=lzma2/fast
SolidCompression=yes
VersionInfoVersion=7.1.0.0
VersionInfoCompany=sliks37
VersionInfoProductName=TwitchGrab Studio
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked
Name: "projectassociation"; Description: "Добавить в список «Открыть с помощью» для проектов .tgstudio"; GroupDescription: "Проекты:"

[Files]
Source: "TwitchGrab-Studio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Studio.xaml"; DestDir: "{app}"; Flags: ignoreversion
Source: "progress_bridge.py"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\app.ico"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "assets\logo.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "engine\*"; DestDir: "{app}\engine"; Excludes: "*.pyc,__pycache__"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Classes\.tgstudio\OpenWithProgids"; ValueType: string; ValueName: "{#ProjectProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: projectassociation
Root: HKCU; Subkey: "Software\Classes\{#ProjectProgId}"; ValueType: string; ValueData: "Проект TwitchGrab Studio"; Flags: uninsdeletekey; Tasks: projectassociation
Root: HKCU; Subkey: "Software\Classes\{#ProjectProgId}\DefaultIcon"; ValueType: string; ValueData: "{app}\TwitchGrab-Studio.exe,0"; Tasks: projectassociation
Root: HKCU; Subkey: "Software\Classes\{#ProjectProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\TwitchGrab-Studio.exe"" ""%1"""; Tasks: projectassociation

[Icons]
Name: "{userprograms}\TwitchGrab Studio"; Filename: "{app}\TwitchGrab-Studio.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\TwitchGrab Studio"; Filename: "{app}\TwitchGrab-Studio.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TwitchGrab-Studio.exe"; Description: "Запустить TwitchGrab Studio"; Flags: nowait postinstall skipifsilent
