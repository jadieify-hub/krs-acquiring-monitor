#define MyAppName "KRS Эквайринг Монитор"
#ifndef MyAppVersion
  #define MyAppVersion "0.2.2"
#endif
#define MyAppPublisher "KRS"
#define MyAppExeName "Krs.AcquiringMonitor.exe"

[Setup]
AppId={{6D12D120-9AF7-4E44-9A1D-0C7CD5B954B1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\KRS Acquiring Monitor
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=KRS-AcquiringMonitor-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Установщик {#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\artifacts\release\Krs.AcquiringMonitor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\Krs.AcquiringMonitor.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\Krs.AcquiringMonitor.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\Krs.AcquiringMonitor.TerminalQuery.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\Krs.AcquiringMonitor.TerminalQuery.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\support-qr.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\release\docs\ACCEPTANCE-CHECKLIST.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\artifacts\release\docs\SECURITY-NOTES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\artifacts\release\docs\screenshots\overlay.png"; DestDir: "{app}\docs\screenshots"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "KRS Acquiring Monitor"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KRS Acquiring Monitor"; ValueData: """{app}\{#MyAppExeName}"""; Flags: dontcreatekey; Check: ShouldMigrateAutoStart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall

[Code]
function ShouldMigrateAutoStart(): Boolean;
begin
  Result := RegValueExists(
    HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Run',
    'KRS Acquiring Monitor');
end;
