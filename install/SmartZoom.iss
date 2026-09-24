; SmartZoom installer (Inno Setup 6).
;
; Per-user by design: SmartZoom needs no administrator rights to do its job, so the installer asks for none
; either and there is no UAC prompt anywhere in it. It installs to %LOCALAPPDATA%\Programs\SmartZoom, writes
; one registry value under HKCU if you ask it to start with Windows, and touches nothing else.
;
; Build it with install\build.ps1, which publishes the app first. Compiling this file on its own will fail
; unless a published SmartZoom.exe is already sitting in the payload directory.

#ifndef PayloadDir
  #define PayloadDir "..\publish"
#endif

#define AppExe PayloadDir + "\SmartZoom.exe"

#if !FileExists(AppExe)
  #error No published SmartZoom.exe to package. Run install\build.ps1 instead of compiling this directly.
#endif

#define AppName "SmartZoom"
#define AppPublisher "SmartZoom contributors"
#define AppVersion GetVersionNumbersString(AppExe)

[Setup]
; Never change this: it is how Windows recognises an upgrade rather than a second copy.
AppId={{6F3A5C1E-9D42-4B87-A0E1-2C7B4F8D5A93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Bringing macOS's smart zoom to Windows
VersionInfoVersion={#AppVersion}

; "lowest" is what keeps the whole thing UAC-free; {autopf} then means %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; The payload is a self-contained x64 build, so there is nowhere else for it to run.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

LicenseFile=..\LICENSE
OutputDir=output
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\src\SmartZoom.App\Resources\SmartZoom.ico
UninstallDisplayIcon={app}\SmartZoom.exe
UninstallDisplayName={#AppName}
WizardStyle=modern

; The payload is one large uncompressed .NET single-file bundle, which is exactly what LZMA2 is good at.
Compression=lzma2/max
SolidCompression=yes

; SmartZoom has no ordinary window most of the time, so the Restart Manager cannot see it. Closing it is
; handled in code instead; see StopSmartZoom below.
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; On by default: a zoom trigger that only works until you next sign out is not much use.
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Also:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Also:"; Flags: unchecked

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\SmartZoom.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\SmartZoom.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\SmartZoom.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Asks for the trigger through SmartZoom's own recorder rather than a list of presets on a wizard page: a
; setup script cannot see a mouse side button at all - Inno only knows left, right and middle - so the only
; thing that can record "the button behind the wheel" is SmartZoom itself. The recorder offers the common
; choices as buttons too, so nothing is lost by dropping the page. It runs before the app is started, so the
; first launch already has the right trigger, and it writes through SettingsStore - the same code that reads
; the file, which therefore keeps everything else the file had.
Filename: "{app}\SmartZoom.exe"; Parameters: "--record-trigger"; \
    Flags: waituntilterminated skipifsilent
Filename: "{app}\SmartZoom.exe"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
{ Asks SmartZoom to close, then insists. A running copy holds its own executable open, so an upgrade cannot
  replace the file until it has gone; an uninstall cannot delete it either. The polite request first gives it
  the chance to take its tray icon down and put back any press it was holding. }
procedure StopSmartZoom();
var
  ResultCode: Integer;
  Waited: Integer;
begin
  { SmartZoom has no ordinary window, so a WM_CLOSE reaches nothing. It does answer "--quit", which shuts it
    down through the same path as Exit on its tray menu - tray icon removed, any swallowed press replayed. }
  if FileExists(ExpandConstant('{app}\SmartZoom.exe')) then
    Exec(ExpandConstant('{app}\SmartZoom.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Waited := 0;
  while Waited < 5000 do
  begin
    { Without /F this asks the window to close rather than killing the process. taskkill exits non-zero once
      nothing matches the name any more, which is also how we know SmartZoom has gone. }
    if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM SmartZoom.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Exit;

    if ResultCode <> 0 then
      Exit;

    Sleep(250);
    Waited := Waited + 250;
  end;

  { Five seconds of asking nicely is enough; a copy still holding its own executable open has to go. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SmartZoom.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSmartZoom();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopSmartZoom();
  Result := True;
end;

{ Settings and logs live outside the install directory and outlive it on purpose: reinstalling should not
  lose your triggers. Removing them is offered, never assumed. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Settings: String;
  Logs: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Settings := ExpandConstant('{userappdata}\SmartZoom');
  Logs := ExpandConstant('{localappdata}\SmartZoom');

  if not (DirExists(Settings) or DirExists(Logs)) then
    Exit;

  { A silent uninstall has nobody to answer this, and a modal dialog nobody can see is a hang - which is
    exactly what an unattended upgrade or a scripted removal would run into. Keeping the data is the safe
    default when there is no one to ask. }
  if UninstallSilent() then
    Exit;

  if MsgBox('Remove your SmartZoom settings and logs as well?' + #13#10#13#10 +
            'Keep them if you are reinstalling or upgrading.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(Settings, True, True, True);
    DelTree(Logs, True, True, True);
  end;
end;
