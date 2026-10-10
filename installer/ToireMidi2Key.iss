; ToireMidi2Key 安装包脚本（Inno Setup 6）
;
; 设计要点
;  1) 每用户安装：PrivilegesRequired=lowest → 双击不弹 UAC，装到 %LocalAppData%\Programs\ToireMidi2Key
;     目录可写，程序的 config.json 恒在 exe 同目录（与安装路径一致）
;  2) 【父文件夹包裹】用户选的是「父目录」，实际始终装进 <所选目录>\ToireMidi2Key：
;     · AppendDefaultDirName=yes  → 用「浏览」按钮选的目录会自动补上 ToireMidi2Key
;     · [Code] 里的 NextButtonClick → 手输的路径也统一规范化（含盘根 E:\ → E:\ToireMidi2Key）
;     这样即使用户把位置填成 E:\ 或 D:\，也绝不会把 exe/README/LICENSE 散落到盘根或系统目录
;  3) 卸载器由 Inno 自动生成（unins000.exe），并在开始菜单与安装目录内各放一个中文卸载入口
;  4) 许可页 = 4 条安装提示（含"装到其它盘建议以管理员身份运行""卸载建议以管理员身份运行"）+ MIT 协议
;  5) 如果检测到之前用 MSI 版安装过，会提示先卸载，避免两份并存

#define AppName "ToireMidi2Key"
#define AppVersion "0.1.0"
#define AppPublisher "KianaMirayi"
#define AppURL "https://github.com/KianaMirayi/ToireMidi2Key"
#define AppExeName "ToireMidi2Key.exe"

[Setup]
AppId=ToireMidi2Key
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
AppendDefaultDirName=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
LicenseFile=license.txt
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ToireMidi2Key-{#AppVersion}-Setup
SetupIconFile=..\src\ToireMidi2Key.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\build\app\ToireMidi2Key.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{app}\卸载 {#AppName}"; Filename: "{uninstallexe}"; Comment: "卸载 {#AppName}（建议右键以管理员身份运行）"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  AppFolderName = '{#AppName}';

{ 把用户填/选的目录规范化成 <父目录>\ToireMidi2Key }
procedure NormalizeInstallDir();
var
  Dir: string;
begin
  Dir := WizardForm.DirEdit.Text;
  { 去掉尾部多余的反斜杠（盘根的最后一个保留，例如 E:\ ） }
  while (Length(Dir) > 3) and (Dir[Length(Dir)] = '\') do
    Delete(Dir, Length(Dir), 1);

  { 已经以 \ToireMidi2Key 结尾就保持不变 }
  if (Length(Dir) >= Length(AppFolderName)) and
     (CompareText(Copy(Dir, Length(Dir) - Length(AppFolderName) + 1, Length(AppFolderName)),
                  AppFolderName) = 0) then
  begin
    WizardForm.DirEdit.Text := Dir;
    Exit;
  end;

  { 盘根（E:\）直接拼名字；否则补一个反斜杠再拼 }
  if (Length(Dir) > 0) and (Dir[Length(Dir)] = '\') then
    WizardForm.DirEdit.Text := Dir + AppFolderName
  else
    WizardForm.DirEdit.Text := Dir + '\' + AppFolderName;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
    NormalizeInstallDir();
end;

{ 静默安装（/VERYSILENT /DIR=...）时不会经过目录页，所以在这里也规范化一次 }
procedure InitializeWizard();
begin
  NormalizeInstallDir();
end;
{ 检测之前是否存在用 MSI 版装的同一程序（我们写过 HKCU\Software\KianaMirayi\ToireMidi2Key） }
function InitializeSetup(): Boolean;
var
  Existing: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, 'Software\KianaMirayi\ToireMidi2Key', 'StartMenuShortcut', Existing) and
     (not FileExists(ExpandConstant('{localappdata}\Programs\ToireMidi2Key\unins000.exe'))) then
  begin
    if MsgBox('检测到之前用 MSI 安装包装过 ToireMidi2Key。' + #13#10 + #13#10 +
              '建议先卸载旧版本（「设置 → 应用 → 已安装的应用」里找到 ToireMidi2Key 卸载），' +
              '再安装本版本，避免两份并存。' + #13#10 + #13#10 + '要继续安装吗？',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
