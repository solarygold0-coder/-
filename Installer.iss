#define MyAppName "نظام إدارة سجلات المراجعين"
#define MyAppVersion "1.1.0"
#define MyAppExeName "سجلات_المرضى.exe"

[Setup]
AppId={{A7904157-8218-4708-9191-D6C477B3940C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Saudi Patient Records
DefaultGroupName={#MyAppName}
OutputDir=release
OutputBaseFilename=تثبيت_نظام_سجلات_المرضى
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
MinVersion=6.1sp1

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Files]
Source: "release\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات إضافية:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "تشغيل {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 461808);
  if not Result then
    MsgBox('يتطلب البرنامج Microsoft .NET Framework 4.7.2 أو 4.8. ثبّته أولاً ثم أعد تشغيل المثبّت.', mbError, MB_OK);
end;
