; Inno Setup 6 script. Normally driven by tools/publish.ps1, which passes:
;   /DAppVersion=0.2.0  /DSourceDir=<publish folder>  /DSelfContained=0|1
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef SelfContained
  #define SelfContained "0"
#endif

[Setup]
AppId={{6F1B7F0C-5C0E-4C54-9B54-4F5A1C0D5A11}
AppName=SnapGit
AppVersion={#AppVersion}
AppPublisher=SnapGit
DefaultDirName={autopf}\SnapGit
DefaultGroupName=SnapGit
OutputDir=..\artifacts\installer
OutputBaseFilename=SnapGit-Setup-{#AppVersion}
SetupIconFile=..\src\SnapGit.App\Assets\SnapGit.ico
UninstallDisplayIcon={app}\SnapGit.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "vietnamese"; MessagesFile: "compiler:Languages\Vietnamese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "Start SnapGit with Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SnapGit"; Filename: "{app}\SnapGit.exe"
Name: "{group}\{cm:UninstallProgram,SnapGit}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SnapGit"; Filename: "{app}\SnapGit.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SnapGit"; ValueData: """{app}\SnapGit.exe"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\SnapGit.exe"; Description: "{cm:LaunchProgram,SnapGit}"; Flags: nowait postinstall skipifsilent

[Code]
// Framework-dependent builds need the .NET 8 Desktop Runtime; tell the user instead of failing silently at first launch.
function DotNetDesktopInstalled(): Boolean;
var
  Dirs: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Dirs) then
    for I := 0 to GetArrayLength(Dirs) - 1 do
      if Pos('8.', Dirs[I]) = 1 then Result := True;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
#if SelfContained == "0"
  if not DotNetDesktopInstalled() then
    if MsgBox('SnapGit needs the .NET 8 Desktop Runtime, which was not found.' + #13#10 +
              'Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this setup again.' + #13#10#13#10 +
              'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
#endif
end;
