; VideoDownloader per-user Windows x64 installer.
; Build with: iscc /DPublishDir="...\publish" installer\VideoDownloader.iss

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "VideoDownloader"
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(PublishDir + "\VideoDownloader.exe", "ProductVersion")
#endif
#define AppPublisher "Bruce Yang"
#define AppExeName "VideoDownloader.exe"

[Setup]
AppId={{4B1A9B89-8B94-4D56-8F68-3F9D2A5E0B2C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Windows desktop media downloader
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=VideoDownloader-v{#AppVersion}-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\VideoDownloader-logo.ico
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Universal Media Downloader
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}
; Publishing is self-contained, so the target machine does not need .NET installed.

[Languages]
; Keep the installer self-contained with Inno Setup's standard English messages.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑（建議）"; GroupDescription: "附加捷徑："

[Code]
var
  ProductInfoPage: TOutputMsgMemoWizardPage;

procedure InitializeWizard;
begin
  ProductInfoPage := CreateOutputMsgMemoPage(
    wpWelcome,
    '歡迎使用 VideoDownloader',
    'Windows x64 · Version {#AppVersion}',
    '程式資訊',
    'VideoDownloader 是 Windows 桌面影音下載工具，可取得公開影音的資訊並下載為影片或音訊。' + #13#10 + #13#10 +
    '支援平台：YouTube、Facebook、Instagram、Threads、X／Twitter 等。' + #13#10 +
    '下載核心：yt-dlp 與 FFmpeg（首次啟動時自動準備）。' + #13#10 +
    '作者：Bruce Yang' + #13#10 + #13#10 +
    '按「Next」選擇安裝位置與捷徑設定。');
end;

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\VideoDownloader-logo.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\VideoDownloader"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\VideoDownloader-logo.ico"; AppUserModelID: "BruceYang.VideoDownloader"
Name: "{autodesktop}\VideoDownloader"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\VideoDownloader-logo.ico"; AppUserModelID: "BruceYang.VideoDownloader"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "啟動 VideoDownloader"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
