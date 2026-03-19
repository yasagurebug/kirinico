#ifndef AppVersion
#define CsprojPath AddBackslash(SourcePath) + "Kirinico.App\\Kirinico.App.csproj"
#define CsprojHandle 0
#define CsprojLine ""
#define VersionTagStart "<Version>"
#define VersionTagEnd "</Version>"
#define public AppVersion ""

#sub ReadAppVersionLine
  #define CsprojLine = FileRead(CsprojHandle)
  #if Pos(VersionTagStart, CsprojLine) > 0 && Pos(VersionTagEnd, CsprojLine) > Pos(VersionTagStart, CsprojLine)
    #define public AppVersion = Copy(CsprojLine, Pos(VersionTagStart, CsprojLine) + Len(VersionTagStart), Pos(VersionTagEnd, CsprojLine) - (Pos(VersionTagStart, CsprojLine) + Len(VersionTagStart)))
  #endif
#endsub

#for {CsprojHandle = FileOpen(CsprojPath); CsprojHandle && !FileEof(CsprojHandle) && AppVersion == ""; ""} ReadAppVersionLine
#if CsprojHandle
  #expr FileClose(CsprojHandle)
#endif

#if AppVersion == ""
  #error Unable to read <Version> from Kirinico.App\Kirinico.App.csproj
#endif
#endif

#ifndef PublishDir
#define PublishDir "Kirinico.App\bin\Release\net8.0-windows\publish"
#endif

#define AppId "{{9CFA4AC2-4F92-4A77-9DBA-67D39F4D6B87}"
#define AppName "Kirinico"
#define AppExeName "Kirinico.App.exe"
#define AppPublisher "Kirinico"
#define AppIconFile "Kirinico.App\Assets\app.ico"
#define DotNetDesktopRuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
#define DotNetDesktopRuntimeFileName "windowsdesktop-runtime-8.0.0-win-x64.exe"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile={#AppIconFile}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
OutputDir=installer-output
OutputBaseFilename=Kirinico-Setup-{#AppVersion}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加タスク:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} を起動する"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDotNetDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  RuntimeGlob: string;
begin
  RuntimeGlob := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*');
  Result := FindFirst(RuntimeGlob, FindRec);
  if Result then
  begin
    FindClose(FindRec);
  end;
end;

function InstallDotNetDesktopRuntime: string;
var
  InstallerPath: string;
  ResultCode: Integer;
begin
  Result := '';

  if IsDotNetDesktopRuntimeInstalled then
  begin
    exit;
  end;

  if SuppressibleMsgBox(
      '.NET 8 Desktop Runtime が見つからないため、Microsoft から自動的にダウンロードしてインストールします。',
      mbInformation,
      MB_OKCANCEL,
      IDOK) <> IDOK then
  begin
    Result := '.NET 8 Desktop Runtime のインストールがキャンセルされました。';
    exit;
  end;

  InstallerPath := ExpandConstant('{tmp}\{#DotNetDesktopRuntimeFileName}');

  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetDesktopRuntimeUrl}', InstallerPath, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := '.NET 8 Desktop Runtime のダウンロードに失敗しました。ネットワーク接続を確認してください。';
      exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  if not Exec(
      InstallerPath,
      '/install /quiet /norestart',
      '',
      SW_SHOWNORMAL,
      ewWaitUntilTerminated,
      ResultCode) then
  begin
    Result := '.NET 8 Desktop Runtime のインストーラーを起動できませんでした。';
    exit;
  end;

  if ResultCode <> 0 then
  begin
    Result := Format('.NET 8 Desktop Runtime のインストールに失敗しました。(終了コード: %d)', [ResultCode]);
    exit;
  end;

  if not IsDotNetDesktopRuntimeInstalled then
  begin
    Result := '.NET 8 Desktop Runtime のインストール後もランタイムを確認できませんでした。';
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    '前提コンポーネントを準備しています',
    '.NET 8 Desktop Runtime を確認しています。',
    nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  Result := InstallDotNetDesktopRuntime;
end;
