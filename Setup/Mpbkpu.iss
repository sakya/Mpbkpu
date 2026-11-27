; Mpbkpu installer. Run build.bat first: the files are taken from Setup\publish.
; The user chooses whether to install for all users (administrator rights) or only for the current user.

#define AppName "Mpbkpu"
#define AppExe "mpbkpugui.exe"
#define PublishDir "publish"
#define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + PublishDir + "\" + AppExe)

[Setup]
AppId={{6F0C3E52-8B7A-4D2E-9C1F-3A5B7D9E2F41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Paolo Iommarini
AppPublisherURL=https://github.com/sakya/Mpbkpu
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Lowest privileges by default, with a dialog to install for all users instead
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\MpbkpuGUI\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=Mpbkpu-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.DeleteUserData=Also delete the settings and the saved credentials of {#AppName}?%n%n%1%n%2%n%nThe backups are not deleted.
italian.DeleteUserData=Eliminare anche le impostazioni e le credenziali salvate di {#AppName}?%n%n%1%n%2%n%nI backup non vengono eliminati.

[Code]
// Settings (MpbkpuGUI: settings.json, secrets) and library data (Mpbkpu: OAuth tokens, known_hosts) of the user who
// uninstalls are deleted only if they choose to (default No, kept on silent uninstalls): a reinstall finds the jobs
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  GuiData, LibData: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  GuiData := ExpandConstant('{localappdata}\MpbkpuGUI');
  LibData := ExpandConstant('{localappdata}\Mpbkpu');
  if not (DirExists(GuiData) or DirExists(LibData)) or UninstallSilent then
    Exit;
  if MsgBox(FmtMessage(CustomMessage('DeleteUserData'), [GuiData, LibData]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(GuiData, True, True, True);
    DelTree(LibData, True, True, True);
  end;
end;
