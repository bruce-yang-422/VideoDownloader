; VideoDownloader per-user Windows x64 installer.
; Build with: iscc /DPublishDir="...\publish" installer\VideoDownloader.iss

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "VideoDownloader"
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(PublishDir + "\VideoDownloader.exe", "ProductVersion")
#endif
#define AppPublisher "VideoDownloader"
#define AppExeName "VideoDownloader.exe"

[Setup]
AppId={{4B1A9B89-8B94-4D56-8F68-3F9D2A5E0B2C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=VideoDownloader-v{#AppVersion}-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\VideoDownloader-logo.ico
; Publishing is self-contained, so the target machine does not need .NET installed.

[Languages]
; Keep the installer self-contained with Inno Setup's standard English messages.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "附加捷徑："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VideoDownloader"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\VideoDownloader"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "啟動 VideoDownloader"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
