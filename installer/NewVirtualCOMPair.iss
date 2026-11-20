; Inno Setup script for NewVirtualCOMPair (setup tools for the com0com virtual serial port driver).
; Build with build.ps1 -Installer (or: ISCC.exe installer\NewVirtualCOMPair.iss after build.ps1).
; Mirrors the original NSIS installer: copy files, then setupc preinstall/update/infclean,
; optional port pairs, setupc uninstall on removal.

; Version = UTC build time (e.g. 26.11.20.1526); build.ps1 passes the same stamp it gave the programs.
#ifndef AppVersion
  #error Build with build.ps1 -Installer, or pass /DAppVersion=<version> to ISCC.
#endif
#define DistDir "..\dist"
#define AppName "NewVirtualCOMPair"

[Setup]
; Own AppId: this package must not hijack the uninstall entry of an original com0com installation.
AppId={{6E0C5D3A-2F7B-4C61-9A3E-0C0C0C0C0C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Big Mouse
AppComments=Unofficial setup tools for the com0com virtual serial port driver by Vyacheslav Frolov.
AppCopyright=Copyright (c) 2026 Big Mouse. com0com driver (c) 2004-2012 Vyacheslav Frolov.
AppPublisherURL=https://github.com/betterone-top/new-com0com
AppSupportURL=https://github.com/betterone-top/new-com0com/issues
AppUpdatesURL=https://github.com/betterone-top/new-com0com/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\com0com.ico
UninstallDisplayIcon={app}\setupg.exe
UninstallDisplayName={#AppName} (virtual serial port pairs, com0com driver)
OutputDir=..\release
OutputBaseFilename={#AppName}-setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
en.PairsGroup=Install a port pair:
zh.PairsGroup=安装一对虚拟串口：
en.PairComClass=COM# <-> COM# (Windows picks free COM numbers; recommended)
zh.PairComClass=COM# <-> COM#（由 Windows 分配空闲 COM 号，推荐）
en.PairCnc=CNCA0 <-> CNCB0 (named by identifier)
zh.PairCnc=CNCA0 <-> CNCB0（以编号命名）
en.Shortcuts=Start menu shortcuts
zh.Shortcuts=开始菜单快捷方式
en.Desktop=Desktop shortcut for NewVirtualCOMPair
zh.Desktop=桌面快捷方式（NewVirtualCOMPair）
en.StatusDriver=Installing the com0com driver...
zh.StatusDriver=正在安装 com0com 驱动...
en.StatusPair=Installing a port pair...
zh.StatusPair=正在安装虚拟串口对...
en.StatusUninstall=Removing all port pairs and the com0com driver...
zh.StatusUninstall=正在删除所有虚拟串口对和 com0com 驱动...
en.LaunchGui=Open NewVirtualCOMPair
zh.LaunchGui=打开 NewVirtualCOMPair
en.OriginalFound=Another installation of the original com0com (by Vyacheslav Frolov) was found in:%n%n%1%n%nIt is recommended to uninstall it first. Continue anyway?
zh.OriginalFound=检测到已安装的原版 com0com（Vyacheslav Frolov 的安装包）：%n%n%1%n%n建议先卸载它。仍然继续？
en.SetupGui=NewVirtualCOMPair
zh.SetupGui=NewVirtualCOMPair
en.SetupCmd=NewVirtualCOMPair command prompt
zh.SetupCmd=NewVirtualCOMPair 命令行

[Tasks]
Name: "pair_com"; Description: "{cm:PairComClass}"; GroupDescription: "{cm:PairsGroup}"; Check: not IsUpgrade
Name: "pair_cnc"; Description: "{cm:PairCnc}"; GroupDescription: "{cm:PairsGroup}"; Flags: unchecked; Check: not IsUpgrade
Name: "shortcuts"; Description: "{cm:Shortcuts}"
Name: "desktop"; Description: "{cm:Desktop}"; Flags: unchecked

[Files]
Source: "{#DistDir}\setupg.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\setupc.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\setupcn.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; Signed driver package: must be copied unchanged (the catalog hashes these files).
Source: "{#DistDir}\com0com.sys"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\com0com.cat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\com0com.inf"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\cncport.inf"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\comport.inf"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{cm:SetupGui}"; Filename: "{app}\setupg.exe"; Tasks: shortcuts
Name: "{group}\{cm:SetupCmd}"; Filename: "{cmd}"; Parameters: "/k cd /d ""{app}"" && setupc help"; WorkingDir: "{app}"; IconFilename: "{app}\setupc.exe"; Tasks: shortcuts
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; Tasks: shortcuts
Name: "{autodesktop}\{cm:SetupGui}"; Filename: "{app}\setupg.exe"; Tasks: desktop

[Run]
; Same sequence as the original installer. Output goes to {app}\setup.log.
Filename: "{app}\setupc.exe"; Parameters: "{code:Silent} --output ""{app}\setup.log"" preinstall"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusDriver}"; Flags: runhidden waituntilterminated
Filename: "{app}\setupc.exe"; Parameters: "{code:Silent} --output ""{app}\setup.log"" update"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusDriver}"; Flags: runhidden waituntilterminated
Filename: "{app}\setupc.exe"; Parameters: "{code:Silent} --output ""{app}\setup.log"" infclean"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusDriver}"; Flags: runhidden waituntilterminated
Filename: "{app}\setupc.exe"; Parameters: "{code:Silent} --output ""{app}\setup.log"" install PortName=COM# PortName=COM#"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusPair}"; Tasks: pair_com; Flags: runhidden waituntilterminated
Filename: "{app}\setupc.exe"; Parameters: "{code:Silent} --output ""{app}\setup.log"" install 0 - -"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusPair}"; Tasks: pair_cnc; Flags: runhidden waituntilterminated
; postinstall entries run as the original (non-admin) user by default, but setupg requires admin
; (error 740 "requires elevation"): keep Setup's elevated rights.
Filename: "{app}\setupg.exe"; Description: "{cm:LaunchGui}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{app}\setupc.exe"; Parameters: "--silent --output ""{app}\setup.log"" uninstall"; WorkingDir: "{app}"; StatusMsg: "{cm:StatusUninstall}"; RunOnceId: "RemoveDriver"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: files; Name: "{app}\setup.log"
Type: dirifempty; Name: "{app}"

[Code]
{ True when this package is already installed: like the original, don't add port pairs on upgrade. }
function IsUpgrade(): Boolean;
var
  s: String;
begin
  Result := RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1',
    'InstallLocation', s);
end;

{ --silent for setupc when the installer itself runs silently (no message boxes). }
function Silent(Param: String): String;
begin
  if WizardSilent then Result := '--silent' else Result := '';
end;

{ Warn about an installation of the original com0com (NSIS package). }
function InitializeSetup(): Boolean;
var
  dir: String;
begin
  Result := True;
  if not IsUpgrade and RegQueryStringValue(HKLM, 'SOFTWARE\com0com', 'Install_Dir', dir) then
    Result := SuppressibleMsgBox(FmtMessage(CustomMessage('OriginalFound'), [dir]),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;
