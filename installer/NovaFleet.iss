; NovaFleet installer — IIS site + app pool + firewall + web.config, then opens the in-app setup wizard.
; Build:  iscc /DStage=stage /DAppVersion=1.0.0 installer\NovaFleet.iss
; Silent: NovaFleet-Setup-x.exe /VERYSILENT /SITENAME=NovaFleet /PORT=5192

#define Stage "stage"
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

[Setup]
AppId=NovaFleet-Install-8E0C4B27
AppName=NovaFleet
AppVersion={#AppVersion}
AppPublisher=NovaFleet
DefaultGroupName=NovaFleet
DefaultDirName={code:GetDefaultDir}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=output
OutputBaseFilename=NovaFleet-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: en; MessagesFile: compiler:Default.isl

[Dirs]
Name: "{app}\data\.novafleet"

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-Iis.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "Uninstall-Iis.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "web.config.template"; DestDir: "{app}\installer"; Flags: ignoreversion

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Visual C++ runtime..."; Flags: skipifdoesntexist
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\Install-Iis.ps1"" -SiteName ""{code:GetSiteName}"" -Port {code:GetPort} -AppDir ""{app}"""; StatusMsg: "Setting up IIS site, app pool, firewall and web.config..."; Flags: runhidden
Filename: "{code:GetSetupUrl}"; Description: "Open the NovaFleet setup wizard in the browser"; Flags: postinstall nowait shellexec runascurrentuser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\Uninstall-Iis.ps1"" -SiteName ""{code:SavedSiteName}"" -Port {code:SavedPort}"; Flags: runhidden; RunOnceId: "NovaFleetIis"

[Code]
var
  QueryPage: TInputQueryWizardPage;
  SiteName: string;
  PortNo: string;
  InstallDir: string;

function GetCmdValue(const Switch: string): string;
var
  Tail: string;
  Key: string;
  P: Integer;
begin
  Result := '';
  Key := '/' + Switch + '=';
  Tail := GetCmdTail;
  P := Pos(UpperCase(Key), UpperCase(Tail));
  if P > 0 then
  begin
    Result := Copy(Tail, P + Length(Key), Length(Tail));
    P := Pos(' ', Result);
    if P > 0 then Result := Copy(Result, 1, P - 1);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  SiteName := 'NovaFleet';
  PortNo := '5192';
  if WizardSilent then
  begin
    if GetCmdValue('SITENAME') <> '' then SiteName := GetCmdValue('SITENAME');
    if GetCmdValue('PORT') <> '' then PortNo := GetCmdValue('PORT');
  end;
  InstallDir := 'C:\inetpub\wwwroot\' + SiteName;
  Result := True;
end;

procedure InitializeWizard;
begin
  if not WizardSilent then
  begin
    QueryPage := CreateInputQueryPage(wpSelectDir,
      'NovaFleet site settings', 'IIS site name and HTTP port',
      'Database, domain and SMS settings are asked LATER by the in-app setup wizard — this installer only creates the IIS site.');
    QueryPage.Add('Site name:', False);
    QueryPage.Add('HTTP port:', False);
    QueryPage.Values[0] := SiteName;
    QueryPage.Values[1] := PortNo;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (QueryPage <> nil) and (CurPageID = QueryPage.ID) then
  begin
    SiteName := Trim(QueryPage.Values[0]);
    PortNo := Trim(QueryPage.Values[1]);
    if (SiteName = '') or (StrToIntDef(PortNo, 0) < 1) or (StrToIntDef(PortNo, 0) > 65535) then
    begin
      MsgBox('Enter a valid site name and port (1-65535).', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    InstallDir := 'C:\inetpub\wwwroot\' + SiteName;
    WizardForm.DirEdit.Text := InstallDir;
  end;
end;

function GetDefaultDir(Param: string): string;
begin
  Result := InstallDir;
end;

function GetSiteName(Param: string): string;
begin
  Result := SiteName;
end;

function GetPort(Param: string): string;
begin
  Result := PortNo;
end;

function GetComputerName: string;
begin
  Result := GetEnv('COMPUTERNAME');
end;

function GetSetupUrl(Param: string): string;
begin
  Result := 'http://' + GetComputerName + ':' + PortNo + '/setup';
end;

function SavedSiteName(Param: string): string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'SiteName', Result) then
    Result := 'NovaFleet';
end;

function SavedPort(Param: string): string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Port', Result) then
    Result := '5192';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'SiteName', SiteName);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Port', PortNo);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'AppDir', ExpandConstant('{app}'));
  end;
end;
