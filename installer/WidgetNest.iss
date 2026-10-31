; ============================================================
;  WidgetNest 安装包脚本（Inno Setup 6）
;  用法：ISCC.exe installer\WidgetNest.iss
;  前置：先 dotnet publish 生成 publish\win-x64\WidgetNest.exe
; ============================================================

#define AppName        "WidgetNest"
#define AppDisplayName "WidgetNest 桌面组件面板"
#define AppVersion     "1.0.0"
#define AppPublisher   "WidgetNest"
#define AppExeName     "WidgetNest.exe"

[Setup]
; AppId 一经发布不要再改，否则会被识别成另一个软件，无法覆盖升级
AppId={{D3A7B2C4-5E8F-4A19-9C2B-7F6E1A8D3B54}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=右下角随手唤出的桌面小组件面板
VersionInfoVersion={#AppVersion}

; 免管理员：{autopf} 在非提权模式下会自动落到 %LocalAppData%\Programs
; 双击安装、一路下一步即可，不弹 UAC。高级用户可用 /ALLUSERS 显式要求全局安装。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no

; 外观
WizardStyle=modern
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppDisplayName}
ShowLanguageDialog=no

; 产物
OutputDir=..\publish
OutputBaseFilename=WidgetNest-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes

; 平台
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; 程序常驻托盘，更新前需要先结束旧进程（见 [Code]）
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\WidgetNest.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单（默认文件夹名为 WidgetNest，图标取自 exe 内嵌图标）
Name: "{group}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Comment: "打开 WidgetNest 组件面板"
Name: "{group}\卸载 {#AppDisplayName}"; Filename: "{uninstallexe}"; Comment: "卸载 WidgetNest"
; 桌面快捷方式（受附加任务控制）
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即运行 {#AppDisplayName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只清理安装目录自身，用户配置（%AppData%）保留，重装后设置还在
Type: filesandordirs; Name: "{app}"

[Code]

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;

  { WidgetNest 常驻系统托盘，Restart Manager 无法可靠关闭它。
    覆盖安装前直接结束旧进程，避免升级后启动的仍是旧实例。 }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
