; Build: iscc /DAppVersion=1.0.0 installer\NovaFleet.iss
; Silent: NovaFleet-Setup-x.exe /VERYSILENT /SITENAME=NovaFleet /PORT=443 /HOSTNAME=fleet.example.test /CERTIFICATETHUMBPRINT=40_HEX_DIGITS
#define Stage "stage"
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef HBundleName
  #define HBundleName "dotnet-hosting-10.0.15-win.exe"
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
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
OutputDir=output
OutputBaseFilename=NovaFleet-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
SetupMutex=NovaFleet-Installer
CloseApplications=no
RestartApplications=no

[Languages]
Name: en; MessagesFile: compiler:Default.isl

[Dirs]
Name: "{app}\data\.novafleet"

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Excludes: "data\*,.novafleet\*,logs\*,appsettings*.json,app_offline.htm"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Upgrade-Iis.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "Install-Iis.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "Installer-Common.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#Stage}\NovaFleet.payload.json"; DestDir: "{tmp}"; Flags: dontcopy
Source: "prereq\{#HBundleName}"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion

[Run]
Filename: "{code:GetSetupUrl}"; Description: "Open NovaFleet in the browser"; Flags: postinstall nowait shellexec runascurrentuser; Check: InstallationSucceeded

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\Uninstall-Iis.ps1"" -SiteName ""{code:SavedSiteName}"" -Port {code:SavedPort}"; Flags: runhidden; RunOnceId: "NovaFleetIis"

[Code]
var
  QueryPage: TInputQueryWizardPage;
  SiteName, PortNo, InstallDir, PreviousVersion: string;
  CertificateHostName, CertificateThumbprint: string;
  IsUpgrade, UpgradePrepared, FreshPrepared, InstallAttempted, InstallSucceeded: Boolean;

function StatePath: string;
begin
  Result := ExpandConstant('{commonappdata}\NovaFleet\upgrade-pending.json');
end;

function InstallStatePath: string;
begin
  Result := ExpandConstant('{commonappdata}\NovaFleet\install-pending.json');
end;

function RunUpgradePhase(Phase: string): Boolean;
var
  Code: Integer;
  Arguments: string;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Upgrade-Iis.ps1') +
    '" -Phase ' + Phase + ' -SiteName "' + SiteName + '" -AppDir "' + ExpandConstant('{app}') +
    '" -TargetVersion "{#AppVersion}" -StateFile "' + StatePath + '" -HttpsPort ' + PortNo +
    ' -CertificateHostName "' + CertificateHostName + '" -CertificateThumbprint "' + CertificateThumbprint +
    '" -NewManifestPath "' + ExpandConstant('{tmp}\NovaFleet.payload.json') + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code);
  if Result then Result := Code = 0;
end;

function RunFreshPhase(Phase: string): Boolean;
var
  Code: Integer;
  Arguments: string;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Install-Iis.ps1') +
    '" -Phase ' + Phase + ' -SiteName "' + SiteName + '" -Port ' + PortNo + ' -AppDir "' + ExpandConstant('{app}') +
    '" -CertificateHostName "' + CertificateHostName + '" -CertificateThumbprint "' + CertificateThumbprint +
    '" -Version "{#AppVersion}" -StateFile "' + InstallStatePath + '" -NewManifestPath "' +
    ExpandConstant('{tmp}\NovaFleet.payload.json') + '" -BundlePath "' + ExpandConstant('{tmp}\{#HBundleName}') + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code);
  if Result then Result := Code = 0;
end;

function ValidSiteName(Name: string): Boolean;
var
  I: Integer;
begin
  Result := (Length(Name) > 0) and (Length(Name) <= 64);
  for I := 1 to Length(Name) do
    if Pos(Name[I], 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_ ') = 0 then Result := False;
end;

function ValidDnsLabel(DnsLabelPart: string): Boolean;
var
  I: Integer;
begin
  Result := (Length(DnsLabelPart) > 0) and (Length(DnsLabelPart) <= 63);
  if not Result then Exit;
  DnsLabelPart := Lowercase(DnsLabelPart);
  if Pos(DnsLabelPart[1], 'abcdefghijklmnopqrstuvwxyz0123456789') = 0 then Result := False;
  if Pos(DnsLabelPart[Length(DnsLabelPart)], 'abcdefghijklmnopqrstuvwxyz0123456789') = 0 then Result := False;
  for I := 1 to Length(DnsLabelPart) do
    if Pos(DnsLabelPart[I], 'abcdefghijklmnopqrstuvwxyz0123456789-') = 0 then Result := False;
end;

function ValidCertificateHostName(Name: string): Boolean;
var
  I, Dot: Integer;
  DnsLabelPart, Rest: string;
  NumericOnly: Boolean;
begin
  Name := Trim(Name);
  Result := (Length(Name) > 0) and (Length(Name) <= 253) and (Name[Length(Name)] <> '.');
  if not Result then Exit;
  NumericOnly := True;
  for I := 1 to Length(Name) do
    if Pos(Name[I], '0123456789.') = 0 then NumericOnly := False;
  if NumericOnly then Result := False;
  Rest := Name;
  while Pos('.', Rest) > 0 do
  begin
    Dot := Pos('.', Rest);
    DnsLabelPart := Copy(Rest, 1, Dot - 1);
    Delete(Rest, 1, Dot);
    if not ValidDnsLabel(DnsLabelPart) then Result := False;
  end;
  if not ValidDnsLabel(Rest) then Result := False;
end;

function ValidCertificateThumbprint(Value: string): Boolean;
var
  I: Integer;
begin
  Result := Length(Value) = 40;
  for I := 1 to Length(Value) do
    if Pos(Value[I], '0123456789abcdefABCDEF') = 0 then Result := False;
end;

function InitializeSetup: Boolean;
var
  OldVersion, NewVersion: Int64;
  SavedHostName, SavedThumbprint: string;
begin
  Result := False;
  if FileExists(StatePath) or FileExists(InstallStatePath) then
  begin
    MsgBox('An unfinished NovaFleet installation or upgrade exists. Recover it using the saved PowerShell script and journal in ProgramData\NovaFleet before running Setup again.', mbError, MB_OK);
    Exit;
  end;
  SiteName := ExpandConstant('{param:SITENAME|NovaFleet}');
  PortNo := ExpandConstant('{param:PORT|443}');
  CertificateHostName := ExpandConstant('{param:HOSTNAME}');
  CertificateThumbprint := ExpandConstant('{param:CERTIFICATETHUMBPRINT}');
  InstallDir := 'C:\inetpub\wwwroot\' + SiteName;
  IsUpgrade := RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'AppDir', InstallDir);
  if IsUpgrade then
  begin
    if not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'SiteName', SiteName) or
       not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Port', PortNo) or
       not GetVersionNumbersString(AddBackslash(InstallDir) + 'NovaFleet.Api.exe', PreviousVersion) then
    begin
      MsgBox('The existing installation is incomplete. No files were changed; repair its registration before upgrading.', mbError, MB_OK);
      Exit;
    end;
    if not StrToVersion(PreviousVersion, OldVersion) or not StrToVersion('{#AppVersion}', NewVersion) then
    begin
      MsgBox('Upgrade requires valid numeric release versions.', mbError, MB_OK);
      Exit;
    end;
    if ComparePackedVersion(NewVersion, OldVersion) < 0 then
    begin
      MsgBox('Installing an older version over ' + PreviousVersion + ' is not allowed.', mbError, MB_OK);
      Exit;
    end;
    if (ExpandConstant('{param:SITENAME|' + SiteName + '}') <> SiteName) or
       (ExpandConstant('{param:PORT|' + PortNo + '}') <> PortNo) then
    begin
      MsgBox('Upgrade keeps the previously installed site name and HTTPS port. Remove conflicting command-line overrides.', mbError, MB_OK);
      Exit;
    end;
    SavedHostName := '';
    SavedThumbprint := '';
    RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'CertificateHostName', SavedHostName);
    RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'CertificateThumbprint', SavedThumbprint);
    if SavedHostName <> '' then
    begin
      if (CertificateHostName <> '') and (CompareText(CertificateHostName, SavedHostName) <> 0) then
      begin
        MsgBox('Upgrade keeps the registered certificate hostname. Remove the conflicting /HOSTNAME override.', mbError, MB_OK);
        Exit;
      end;
      CertificateHostName := SavedHostName;
    end;
    if (SavedThumbprint <> '') and (CertificateThumbprint = '') then CertificateThumbprint := SavedThumbprint;
  end;
  Result := True;
end;

procedure InitializeWizard;
begin
  if IsUpgrade then
  begin
    CreateOutputMsgPage(wpWelcome, 'Upgrade existing NovaFleet installation', PreviousVersion + ' -> {#AppVersion}',
      'Site: ' + SiteName + #13#10 + 'Directory: ' + InstallDir + #13#10 + 'HTTPS port: ' + PortNo + #13#10#13#10 +
      'The site will be temporarily unavailable. IIS bindings, application-pool settings, database connection and encryption keys are preserved.' + #13#10#13#10 +
      'Files and the SQL database are backed up before replacement. SQL backup permissions and free space are required. Keep other applications and administrators from writing to this database during upgrade.' + #13#10#13#10 +
      'Verification uses the trusted certificate over HTTPS. Failure restores the old database and files before reopening the site. After normal traffic is enabled, database recovery requires an administrator to avoid losing new data.');
  end;
  if not WizardSilent and ((not IsUpgrade) or (CertificateHostName = '') or (CertificateThumbprint = '')) then
  begin
    QueryPage := CreateInputQueryPage(wpSelectDir, 'NovaFleet HTTPS settings',
      'A trusted certificate with a private key must already exist in LocalMachine\My. It must be valid and cover the hostname below.',
      'Database, domain and SMS settings are configured later in the application. Existing IIS sites are never replaced by a fresh installation.');
    if IsUpgrade then
    begin
      QueryPage.Add('Certificate hostname:', False);
      QueryPage.Add('Certificate thumbprint (40 hex digits):', False);
      QueryPage.Values[0] := CertificateHostName;
      QueryPage.Values[1] := CertificateThumbprint;
    end
    else
    begin
      QueryPage.Add('Site name:', False);
      QueryPage.Add('HTTPS port:', False);
      QueryPage.Add('Certificate hostname:', False);
      QueryPage.Add('Certificate thumbprint (40 hex digits):', False);
      QueryPage.Values[0] := SiteName;
      QueryPage.Values[1] := PortNo;
      QueryPage.Values[2] := CertificateHostName;
      QueryPage.Values[3] := CertificateThumbprint;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpgrade and (PageID = wpSelectDir);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (QueryPage <> nil) and (CurPageID = QueryPage.ID) then
  begin
    if IsUpgrade then
    begin
      CertificateHostName := Trim(QueryPage.Values[0]);
      CertificateThumbprint := Uppercase(Trim(QueryPage.Values[1]));
    end
    else
    begin
      SiteName := Trim(QueryPage.Values[0]);
      PortNo := Trim(QueryPage.Values[1]);
      CertificateHostName := Trim(QueryPage.Values[2]);
      CertificateThumbprint := Uppercase(Trim(QueryPage.Values[3]));
    end;
    if (not IsUpgrade) and (not ValidSiteName(SiteName) or (StrToIntDef(PortNo, 0) < 1) or (StrToIntDef(PortNo, 0) > 65535)) then
    begin
      MsgBox('Enter a valid site name and HTTPS port (1-65535).', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if not ValidCertificateHostName(CertificateHostName) or not ValidCertificateThumbprint(CertificateThumbprint) then
    begin
      MsgBox('Enter a DNS hostname covered by the certificate and its 40-digit hexadecimal thumbprint.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if not IsUpgrade then
    begin
      InstallDir := 'C:\inetpub\wwwroot\' + SiteName;
      WizardForm.DirEdit.Text := InstallDir;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  Result := '';
  if FileExists(StatePath) or FileExists(InstallStatePath) then
  begin
    Result := 'An unfinished installation or upgrade journal exists. Recover it before retrying.';
    Exit;
  end;
  if not ValidSiteName(SiteName) or (StrToIntDef(PortNo, 0) < 1) or (StrToIntDef(PortNo, 0) > 65535) or
     not ValidCertificateHostName(CertificateHostName) or not ValidCertificateThumbprint(CertificateThumbprint) or
     (Pos('"', ExpandConstant('{app}')) > 0) then
  begin
    Result := 'Invalid IIS or HTTPS certificate settings.';
    Exit;
  end;
  if CompareText(ExpandConstant('{app}'), InstallDir) <> 0 then
  begin
    Result := 'Setup must use the dedicated NovaFleet directory shown in the site settings.';
    Exit;
  end;
  ExtractTemporaryFile('Upgrade-Iis.ps1');
  ExtractTemporaryFile('Install-Iis.ps1');
  ExtractTemporaryFile('Installer-Common.ps1');
  ExtractTemporaryFile('NovaFleet.payload.json');
  if IsUpgrade then
  begin
    if UpgradePrepared then Exit;
    UpgradePrepared := RunUpgradePhase('Prepare');
    if not UpgradePrepared then Result := 'Upgrade preflight or backup failed. No application files were replaced. Check ProgramData\NovaFleet for recovery state, payload ownership, trusted certificate and SQL Server backup permissions.';
  end
  else if not FreshPrepared then
  begin
    if not RunUpgradePhase('CheckNew') then
    begin
      Result := 'The destination, certificate, IIS site, pool or HTTPS port is not available. No IIS resources were changed.';
      Exit;
    end;
    FreshPrepared := RunFreshPhase('Prepare');
    if not FreshPrepared then Result := 'Fresh-install recovery preparation failed. No application files were copied; check the certificate and destination.';
  end;
end;

function GetDefaultDir(Param: string): string;
begin
  Result := InstallDir;
end;

function GetSetupUrl(Param: string): string;
begin
  Result := 'https://' + CertificateHostName + ':' + PortNo;
  if not IsUpgrade then Result := Result + '/setup';
end;

function SavedSiteName(Param: string): string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'SiteName', Result) then Result := 'NovaFleet';
end;

function SavedPort(Param: string): string;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Port', Result) then Result := '443';
end;

function InstallationSucceeded: Boolean;
begin
  Result := InstallSucceeded;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Success: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    if IsUpgrade then Success := RunUpgradePhase('Finish')
    else
    begin
      InstallAttempted := True;
      Success := RunFreshPhase('Install');
    end;
    if not Success then RaiseException('NovaFleet did not pass HTTPS verification. Recovery will be attempted; keep ProgramData\NovaFleet backups until an administrator confirms the result.');
    InstallSucceeded := True;
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'SiteName', SiteName);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Port', PortNo);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'CertificateHostName', CertificateHostName);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'CertificateThumbprint', CertificateThumbprint);
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'AppDir', ExpandConstant('{app}'));
    RegWriteStringValue(HKLM, 'SOFTWARE\NovaFleet\Install', 'Version', '{#AppVersion}');
    if IsUpgrade then DeleteFile(StatePath);
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := not (FileExists(StatePath) or FileExists(InstallStatePath));
  if not Result then MsgBox('Recover the unfinished NovaFleet installation or upgrade before uninstalling.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\NovaFleet\Install');
end;

procedure DeinitializeSetup;
begin
  if InstallSucceeded then Exit;
  if UpgradePrepared and not RunUpgradePhase('Recover') then
    MsgBox('Automatic upgrade recovery could not complete safely. Keep the site offline. Use the backup and recovery journal in ProgramData\NovaFleet with your administrator.', mbError, MB_OK)
  else if (FreshPrepared or InstallAttempted or FileExists(InstallStatePath)) and not RunFreshPhase('Recover') then
    MsgBox('Automatic fresh-install recovery could not complete safely. Keep the site offline. Use the install journal in ProgramData\NovaFleet with your administrator.', mbError, MB_OK);
end;
