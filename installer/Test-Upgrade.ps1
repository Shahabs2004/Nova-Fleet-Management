$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Upgrade-Iis.ps1') -FunctionsOnly
. (Join-Path $PSScriptRoot 'Install-Iis.ps1') -FunctionsOnly
. (Join-Path $PSScriptRoot 'Uninstall-Iis.ps1') -FunctionsOnly

$script:testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NovaFleet-Upgrade-Tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:testRoot | Out-Null
$realCopy = ${function:Copy-UpgradeTree}
$realRestoreFiles = ${function:Restore-UpgradeFiles}
$realGetConnection = ${function:Get-UpgradeConnection}
$realWaitInstallHealth = ${function:Wait-InstallHealth}
$realWaitUpgradeHealth = ${function:Wait-UpgradeHealth}
$realTestInstallerSslBinding = ${function:Test-InstallerSslBinding}
$realGetInstallerSslThumbprint = ${function:Get-InstallerSslThumbprint}
$realTestInstallFirewallRule = ${function:Test-InstallFirewallRule}
$realNewInstallSite = ${function:New-InstallSite}
$checks = 0
function Assert-Upgrade($Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
}
function Assert-UpgradeThrows([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action } catch { $threw = $true }
    Assert-Upgrade $threw $Message
}
function New-UpgradeFixture {
    $script:fixture = Join-Path $script:testRoot ([Guid]::NewGuid().ToString('N'))
    $script:app = Join-Path $fixture 'app'
    $script:backup = Join-Path $fixture 'backup'
    $script:journal = Join-Path $fixture 'pending.json'
    New-Item -ItemType Directory -Path $app, (Join-Path $backup 'files') | Out-Null
    $script:originalConfig = '<configuration><system.webServer><aspNetCore processPath=".\NovaFleet.Api.exe"><environmentVariables><environmentVariable name="CUSTOM_SETTING" value="preserve" /></environmentVariables></aspNetCore></system.webServer></configuration>'
    foreach ($folder in @($app, (Join-Path $backup 'files'))) {
        Set-Content -LiteralPath (Join-Path $folder 'web.config') -Value $originalConfig
        Set-Content -LiteralPath (Join-Path $folder 'appsettings.json') -Value '{"custom":"preserve"}'
        Set-Content -LiteralPath (Join-Path $folder 'old.dll') -Value 'old'
    }
    $script:newPayload = Join-Path $fixture 'new-payload'
    New-Item -ItemType Directory -Path $newPayload | Out-Null
    Set-Content (Join-Path $newPayload 'new.dll') 'new'
    New-InstallerPayloadManifest (Join-Path $backup 'files') (Join-Path $backup 'old-payload.json')
    New-InstallerPayloadManifest $newPayload (Join-Path $backup 'new-payload.json')
    Copy-Item (Join-Path $backup 'old-payload.json') (Join-Path $app 'NovaFleet.payload.json') -Force
    $script:certificate = [pscustomobject]@{
        Thumbprint = 'A1B2C3'; HasPrivateKey = $true; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30)
        DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }); Subject = 'CN=server.example.test'
    }
    $script:events = [Collections.Generic.List[string]]::new()
    $script:installEvents = [Collections.Generic.List[string]]::new()
    $script:installResources = @{ Sites = @{}; Pools = @{}; Bindings = @{}; Firewall = @{}; Ssl = @{} }
    $script:healthFails = $false
    $script:backupFails = $false
    $script:restoreFails = $false
    $script:headerInvalid = $false
    $script:sharedPool = $false
    $script:upgradeHasHttpsBinding = $false
    $script:site = [pscustomobject]@{ physicalPath = $app; applicationPool = 'TestPool'; State = 'Started'; bindings = @{ Collection = @([pscustomobject]@{ protocol = 'https'; bindingInformation = '*:443:server.example.test' }) } }
    $script:state = [pscustomobject]@{
        Stage = 'copying'; SiteName = 'TestSite'; PoolName = 'TestPool'; AppDir = $app; TargetVersion = '1.1.0'; Port = 443
        CertificateThumbprint = 'A1B2C3'; CertificateHostName = 'server.example.test'; CreatedHttpsBinding = $false; CreatedSslBinding = $false
        OriginalSslThumbprint = ''; SslBindingModified = $false
        BackupDir = $backup; Database = 'NovaFleetSynthetic'; SqlBackup = 'C:\SQLBackups\synthetic.bak'; BackupName = 'synthetic'
        OldManifest = (Join-Path $backup 'old-payload.json'); NewManifest = (Join-Path $backup 'new-payload.json')
        HealthUrl = 'https://server.example.test:443/health'; HealthHost = 'server.example.test'; SiteWasStarted = $true; PoolWasStarted = $true
    }
    Save-UpgradeState $state $journal
}
function Stop-UpgradeSite($State) { $script:events.Add('stop'); Set-Content (Join-Path $State.AppDir 'app_offline.htm') 'maintenance' }
function Start-UpgradeSite($State) {
    $script:events.Add('start')
    Remove-Item (Join-Path $State.AppDir 'app_offline.htm') -Force -ErrorAction SilentlyContinue
}
function Wait-UpgradeHealth($State, [bool]$Verification) {
    Assert-Upgrade ($State.HealthUrl -eq 'https://server.example.test:443/health' -and $State.HealthHost -eq 'server.example.test') 'Upgrade health remains HTTPS-only on the certificate hostname'
    if ($script:upgradeHasHttpsBinding) {
        Assert-Upgrade ($script:installResources.Ssl['443'] -eq $State.CertificateThumbprint) 'Existing HTTPS binding uses the validated certificate before health verification'
    } else {
        Assert-Upgrade ($events.Contains('add-upgrade-https') -and $events.Contains('add-ssl:443')) 'HTTPS binding exists before upgrade health verification'
    }
    $script:events.Add("health:$Verification")
    [xml]$config = Get-Content (Join-Path $State.AppDir 'web.config') -Raw
    $flag = $config.SelectSingleNode('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]')
    Assert-Upgrade (($null -ne $flag) -eq $Verification) 'Verification flag matches the health phase'
    $saved = Get-Content $journal -Raw | ConvertFrom-Json
    Assert-Upgrade ($saved.Stage -eq $(if ($Verification) { 'verifying' } else { 'accepted' })) 'Recovery boundary persisted before starting traffic'
    if ($script:healthFails) { throw 'Simulated health failure' }
}
function Get-WebAppPoolState { return @{ Value = 'Stopped' } }
function Start-WebAppPool { param($Name) $script:events.Add('restore-pool') }
function Start-Website { param($Name) $script:events.Add('restore-site') }
function Get-Website {
    param($Name)
    if (-not $Name -and $script:sharedPool) { return @($script:site, $script:site) }
    return $script:site
}
function Get-WebApplication { return @() }
function Get-Item {
    param($Path, $LiteralPath)
    $target = if ($LiteralPath) { $LiteralPath } else { $Path }
    if ($target -like 'IIS:*') { return [pscustomobject]@{ environmentVariables = @{ Collection = @() } } }
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $target
}
function Protect-UpgradeDirectory($Path) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
function Get-UpgradeConnection($Root, $Pool) { return @{ Connection = 'synthetic-only'; Home = (Join-Path $script:app 'data'); Database = 'NovaFleetSynthetic' } }
function Copy-UpgradeTree($Source, $Destination, [switch]$Mirror) {
    Assert-Upgrade ($Source.StartsWith($script:testRoot) -and $Destination.StartsWith($script:testRoot)) 'All copy operations stay inside disposable fixtures'
    $script:events.Add($(if ($Mirror) { 'restore-files' } else { 'backup-files' }))
    & $realCopy $Source $Destination -Mirror:$Mirror
}
function Restore-UpgradeFiles($State) {
    Assert-Upgrade ($State.AppDir.StartsWith($script:testRoot) -and $State.BackupDir.StartsWith($script:testRoot)) 'All recovery copies stay inside disposable fixtures'
    $script:events.Add('restore-files')
    & $realRestoreFiles $State
}
function Invoke-UpgradeSql($ConnectionString, $Sql, $Parameters) {
    Assert-Upgrade ($ConnectionString -eq 'synthetic-only') 'No real SQL connection is used'
    $table = New-Object System.Data.DataTable
    if ($Sql -match 'SERVERPROPERTY') {
        [void]$table.Columns.Add('Path'); [void]$table.Rows.Add('C:\SQLBackups')
    } elseif ($Sql -match '^BACKUP DATABASE') {
        $script:events.Add('backup-sql')
        if ($script:backupFails) { throw 'Simulated backup failure' }
        Assert-Upgrade ($Sql.Contains('COPY_ONLY, CHECKSUM') -and $Sql.Contains('VERIFYONLY')) 'SQL backup is copy-only and verified'
    } elseif ($Sql -match 'HEADERONLY') {
        [void]$table.Columns.Add('DatabaseName'); [void]$table.Columns.Add('BackupName')
        [void]$table.Rows.Add($(if ($script:headerInvalid) { 'WrongDatabase' } else { 'NovaFleetSynthetic' }), 'synthetic')
    } elseif ($Sql -match 'ALTER DATABASE') {
        $script:events.Add('restore-sql')
        if ($script:restoreFails) { throw 'Simulated restore failure' }
    }
    return ,$table
}

$script:installResources = @{ Sites = @{}; Pools = @{}; Bindings = @{}; Firewall = @{}; Ssl = @{} }
function New-FreshInstallFixture {
    $script:fixture = Join-Path $script:testRoot ([Guid]::NewGuid().ToString('N'))
    $script:app = Join-Path $fixture 'app'
    $script:journal = Join-Path $fixture 'state\install-pending.json'
    New-Item -ItemType Directory -Path $app, (Join-Path $app 'installer'), (Join-Path $app 'data\.novafleet') -Force | Out-Null
    $script:originalConfig = '<configuration><system.webServer><aspNetCore processPath=".\NovaFleet.Api.exe"><environmentVariables><environmentVariable name="CUSTOM_SETTING" value="preserve" /><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" /><environmentVariable name="DOTNET_ENVIRONMENT" value="Development" /><environmentVariable name="NOVAFLEET_INSTALLER_VERIFY" value="1" /></environmentVariables></aspNetCore></system.webServer></configuration>'
    Set-Content (Join-Path $app 'web.config') $originalConfig
    Set-Content (Join-Path $app 'appsettings.json') '{"custom":"preserve"}'
    Set-Content (Join-Path $app 'data\user.db') 'preserve-user-data'
    Set-Content (Join-Path $app 'installer\web.config.template') (Get-Content (Join-Path $PSScriptRoot 'web.config.template') -Raw)
    $script:installEvents = [Collections.Generic.List[string]]::new()
    $script:installResources = @{ Sites = @{}; Pools = @{}; Bindings = @{}; Firewall = @{}; Ssl = @{} }
    $script:installFailAt = ''
    $script:certificate = [pscustomobject]@{
        Thumbprint = 'A1B2C3'; HasPrivateKey = $true; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30)
        DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }); Subject = 'CN=server.example.test'
    }
}
function New-PreparedFreshFixture {
    $script:fixture = Join-Path $script:testRoot ([Guid]::NewGuid().ToString('N'))
    $script:app = Join-Path $fixture 'app'
    $script:journal = Join-Path $fixture 'state\install-pending.json'
    $package = Join-Path $fixture 'package'
    $script:backup = Join-Path $fixture 'state\installs\prepared'
    New-Item -ItemType Directory -Path $app, (Join-Path $package 'installer'), $backup | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'web.config.template') (Join-Path $package 'installer\web.config.template')
    Set-Content (Join-Path $package 'new.dll') 'new'
    New-InstallerPayloadManifest $package (Join-Path $backup 'new-payload.json')
    Set-Content (Join-Path $backup 'empty-payload.json') '{"format":"novafleet-installer-manifest-v1","files":[]}'
    Copy-Item (Join-Path $package '*') $app -Recurse -Force
    Copy-Item (Join-Path $backup 'new-payload.json') (Join-Path $app 'NovaFleet.payload.json') -Force
    $script:installEvents = [Collections.Generic.List[string]]::new()
    $script:installResources = @{ Sites = @{}; Pools = @{}; Bindings = @{}; Firewall = @{}; Ssl = @{} }
    $script:installFailAt = ''
    $script:certificate = [pscustomobject]@{
        Thumbprint = 'A1B2C3'; HasPrivateKey = $true; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30)
        DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }); Subject = 'CN=server.example.test'
    }
    $script:state = [pscustomobject]@{
        Stage = 'prepared'; SiteName = 'TestSite'; PoolName = 'TestSite'; AppDir = $app; Port = 443
        CertificateThumbprint = 'A1B2C3'; CertificateHostName = 'server.example.test'; TargetVersion = '1.1.0'
        HealthUrl = 'https://server.example.test:443/health'; HealthHost = 'server.example.test'; BackupDir = $backup
        CreatedAppPool = $false; CreatedSite = $false; CreatedSslBinding = $false; CreatedFirewallRule = $false
        CreatedWebConfig = $false; WebConfigExisted = $false; DataDirectoryExisted = $false; HomeDirectoryExisted = $false
        AppDirectoryExisted = $true; PayloadPrepared = $true; EmptyManifest = (Join-Path $backup 'empty-payload.json'); NewManifest = (Join-Path $backup 'new-payload.json')
    }
    Save-InstallerState $state $journal
}
function Get-InstallAppCmd { return 'mock-appcmd.exe' }
function Initialize-IisPrerequisites { param($AppCmd, $BundlePath) $script:installEvents.Add('prerequisites') }
function Get-IisSite { param($Name) if ($script:installResources.Sites.ContainsKey($Name)) { return $script:installResources.Sites[$Name] } }
function Get-IisAppPool { param($Name) if ($script:installResources.Pools.ContainsKey($Name)) { return $script:installResources.Pools[$Name] } }
function New-InstallAppPool { param($Name) $script:installEvents.Add('create-pool'); $script:installResources.Pools[$Name] = $true }
function Set-InstallAppPoolSettings { param($Name, $AppCmd) $script:installEvents.Add('configure-pool') }
function New-InstallSite { param($Name, $Root, $PoolName, $Port, $HostName) $script:installEvents.Add('create-site'); $script:installResources.Sites[$Name] = $true; $script:installResources.Bindings[$Name] = @("https:*:${Port}:$HostName") }
function Add-IisHttpsBinding { param($Name, $HostName, $Port) $script:installEvents.Add("add-https:${HostName}:$Port"); $script:installResources.Bindings[$Name] += "https:*:${Port}:$HostName" }
function Test-InstallerSslBinding { param($Port) return $script:installResources.Ssl.ContainsKey([string]$Port) }
function Add-InstallerSslBinding { param($Port, $Thumbprint) $script:installEvents.Add("add-ssl:$Port"); $script:events.Add("add-ssl:$Port"); $script:installResources.Ssl[[string]$Port] = $Thumbprint }
function Remove-InstallerSslBinding { param($Port) $script:installEvents.Add("remove-ssl:$Port"); $script:events.Add("remove-ssl:$Port"); $script:installResources.Ssl.Remove([string]$Port) }
function Get-UpgradeHttpsBinding { param($SiteName, $Port, $HostName) if ($script:upgradeHasHttpsBinding) { return [pscustomobject]@{ bindingInformation = '*:443:server.example.test' } } }
function Get-InstallerSslThumbprint { param($Port) if ($script:installResources.Ssl.ContainsKey([string]$Port)) { return $script:installResources.Ssl[[string]$Port] } }
function Add-UpgradeHttpsBinding { param($SiteName, $HostName, $Port, $Thumbprint) $script:events.Add('add-upgrade-https'); Add-InstallerSslBinding $Port $Thumbprint }
function Remove-UpgradeHttpsBinding { param($SiteName, $HostName, $Port) $script:events.Add('remove-upgrade-https') }
function Grant-InstallDataAccess { param($Path, $SiteName) $script:installEvents.Add('grant-data') }
function Test-InstallFirewallRule { param($Name) return $script:installResources.Firewall.ContainsKey($Name) }
function New-InstallFirewallRule { param($Name, $Port) if ($script:installFailAt -eq 'firewall') { throw 'Simulated firewall failure' }; $script:installEvents.Add('create-firewall'); $script:installResources.Firewall[$Name] = $Port }
function Remove-InstallFirewallRule { param($Name) $script:installEvents.Add('remove-firewall'); $script:installResources.Firewall.Remove($Name) }
function Start-InstallSite { param($SiteName) $script:installEvents.Add('start') }
function Stop-IisSite { param($Name) $script:installEvents.Add('stop-site') }
function Stop-IisAppPool { param($Name) $script:installEvents.Add('stop-pool') }
function Remove-IisSite { param($Name) $script:installEvents.Add('remove-site'); $script:installResources.Sites.Remove($Name); $script:installResources.Bindings.Remove($Name) }
function Remove-IisAppPool { param($Name) $script:installEvents.Add('remove-pool'); $script:installResources.Pools.Remove($Name) }
function Protect-InstallerDirectory { param($Path) New-Item -ItemType Directory -Path $Path -Force | Out-Null }
function Wait-InstallHealth {
    param($State)
    $script:installEvents.Add("health:$($State.HealthUrl):$($State.Stage)")
    Assert-Upgrade ($State.HealthUrl -eq 'https://server.example.test:443/health') 'Fresh health uses the certificate hostname over HTTPS only'
    [xml]$config = Get-Content (Join-Path $State.AppDir 'web.config') -Raw
    $flag = $config.SelectSingleNode('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]')
    Assert-Upgrade (($null -ne $flag) -eq ($State.Stage -eq 'verifying')) 'Fresh verification flag follows the recovery boundary'
    if ($script:installFailAt -eq 'health' -and $State.Stage -eq 'verifying') { throw 'Simulated fresh health failure' }
    if ($script:installFailAt -eq 'accepted-health' -and $State.Stage -eq 'accepted') { throw 'Simulated accepted health failure' }
}

try {
    Assert-Upgrade ((Convert-UpgradeVersion '1.2.3') -eq (Convert-UpgradeVersion '1.2.3.0')) 'Three and four-part versions agree'
    Assert-Upgrade ((Convert-UpgradeVersion '1.10.0') -gt (Convert-UpgradeVersion '1.9.0')) 'Versions are numeric rather than lexical'
    Assert-UpgradeThrows { Convert-UpgradeVersion 'broken' } 'Invalid versions fail closed'

    Assert-Upgrade (Test-InstallerCertificateHostname 'server.example.test' @('server.example.test')) 'Certificate exact DNS name matches'
    Assert-Upgrade (Test-InstallerCertificateHostname 'SERVER.example.test' @('server.example.test')) 'Certificate DNS matching ignores case'
    Assert-Upgrade (Test-InstallerCertificateHostname 'fleet.example.test' @('*.example.test')) 'Certificate wildcard covers one DNS label'
    Assert-Upgrade (-not (Test-InstallerCertificateHostname 'deep.fleet.example.test' @('*.example.test'))) 'Certificate wildcard does not cover multiple labels'
    Assert-Upgrade (-not (Test-InstallerCertificateHostname 'example.test' @('*.example.test'))) 'Certificate wildcard does not match the bare domain'
    Assert-Upgrade (-not (Test-InstallerCertificateHostname 'other.test' @('server.example.test'))) 'Certificate hostname mismatch is rejected'

    $script:certificate = [pscustomobject]@{
        Thumbprint = 'A1B2C3'; HasPrivateKey = $true; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30)
        DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }); Subject = 'CN=server.example.test'
    }
    function Get-InstallerCertificate { param([string]$Thumbprint) return $script:certificate }
    Assert-Upgrade ((Assert-InstallerCertificate 'a1 b2 c3' 'server.example.test').Thumbprint -eq 'A1B2C3') 'Valid machine certificate with private key is accepted'
    Assert-UpgradeThrows { Assert-InstallerCertificate '' 'server.example.test' } 'Missing certificate thumbprint fails closed'
    $script:certificate = $null
    Assert-UpgradeThrows { Assert-InstallerCertificate 'A1B2C3' 'server.example.test' } 'Unknown certificate fails closed'
    $script:certificate = [pscustomobject]@{ Thumbprint = 'A1B2C3'; HasPrivateKey = $false; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30); DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }) }
    Assert-UpgradeThrows { Assert-InstallerCertificate 'A1B2C3' 'server.example.test' } 'Certificate without a private key is rejected'
    $script:certificate.HasPrivateKey = $true; $script:certificate.NotAfter = [DateTime]::UtcNow.AddMinutes(-1)
    Assert-UpgradeThrows { Assert-InstallerCertificate 'A1B2C3' 'server.example.test' } 'Expired certificate is rejected'
    $script:certificate.NotAfter = [DateTime]::UtcNow.AddDays(30); $script:certificate.NotBefore = [DateTime]::UtcNow.AddDays(1)
    Assert-UpgradeThrows { Assert-InstallerCertificate 'A1B2C3' 'server.example.test' } 'Not-yet-valid certificate is rejected'
    $script:certificate.NotBefore = [DateTime]::UtcNow.AddDays(-1); $script:certificate.DnsNameList = @([pscustomobject]@{ Value = 'other.example.test' })
    Assert-UpgradeThrows { Assert-InstallerCertificate 'A1B2C3' 'server.example.test' } 'Certificate must cover the installer hostname'

    $script:netshExitCode = 0
    $script:netshText = @('SSL-Zertifikatbindung:', 'Zertifikathash: A1B2C3A1B2C3A1B2C3A1B2C3A1B2C3A1B2C3A1B2')
    function netsh.exe { $global:LASTEXITCODE = $script:netshExitCode; return $script:netshText }
    Assert-Upgrade ((& $realGetInstallerSslThumbprint 443) -eq 'A1B2C3A1B2C3A1B2C3A1B2C3A1B2C3A1B2C3A1B2') 'SSL thumbprint parsing does not depend on localized netsh labels'
    Assert-Upgrade (& $realTestInstallerSslBinding 443) 'Existing SSL binding detection does not depend on localized netsh labels'
    Assert-Upgrade (& $realTestInstallFirewallRule 'NovaFleet-TestSite-443') 'Existing firewall rule detection does not depend on localized netsh labels'
    $script:netshExitCode = 1
    Assert-Upgrade (-not (& $realTestInstallFirewallRule 'NovaFleet-TestSite-443')) 'Missing firewall rule is detected from the native exit code'
    Remove-Item Function:\netsh.exe -ErrorAction SilentlyContinue

    $script:createdSiteRequest = $null
    function New-Website {
        param([string]$Name, [string]$PhysicalPath, [string]$ApplicationPool, [int]$Port, [switch]$Ssl, [string]$HostHeader)
        $script:createdSiteRequest = [pscustomobject]@{ Name = $Name; Port = $Port; Ssl = $Ssl.IsPresent; HostHeader = $HostHeader }
    }
    & $realNewInstallSite 'TestSite' 'C:\temp\NovaFleet' 'TestPool' 443 'server.example.test'
    Assert-Upgrade ($script:createdSiteRequest.Port -eq 443 -and $script:createdSiteRequest.Ssl -and $script:createdSiteRequest.HostHeader -eq 'server.example.test') 'New IIS sites are created directly with HTTPS and the validated host header'
    Remove-Item Function:\New-Website -ErrorAction SilentlyContinue

    Assert-Upgrade ($null -ne (Get-Command Remove-UninstallSslBinding -ErrorAction SilentlyContinue)) 'Uninstall exposes safe SSL binding cleanup'
    $script:installEvents = [Collections.Generic.List[string]]::new()
    $script:events = [Collections.Generic.List[string]]::new()
    $script:installResources.Ssl['8443'] = 'A1B2C3'
    $noSites = @()
    Remove-UninstallSslBinding 8443 $noSites
    Assert-Upgrade (-not $script:installResources.Ssl.ContainsKey('8443')) 'Uninstall removes an unused installer SSL binding'
    $script:installResources.Ssl['8443'] = 'A1B2C3'
    $otherSite = [pscustomobject]@{ bindings = [pscustomobject]@{ Collection = @([pscustomobject]@{ protocol = 'https'; bindingInformation = '*:8443:other.example.test' }) } }
    Remove-UninstallSslBinding 8443 @($otherSite)
    Assert-Upgrade ($script:installResources.Ssl['8443'] -eq 'A1B2C3') 'Uninstall preserves an SSL binding still used by another site'
    $script:installResources.Ssl.Remove('8443')

    $template = Get-Content (Join-Path $PSScriptRoot 'web.config.template') -Raw
    Assert-Upgrade ($template -match 'name="ASPNETCORE_ENVIRONMENT" value="Production"') 'Installer template selects Production'
    Assert-Upgrade ($template -notmatch 'name="ASPNETCORE_ENVIRONMENT" value="Development"') 'Installer template has no Development fallback'

    $duplicateRoot = Join-Path $script:testRoot 'duplicate-environment'
    New-Item -ItemType Directory -Path $duplicateRoot | Out-Null
    $duplicateConfig = '<configuration><system.webServer><aspNetCore><environmentVariables><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" /><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Staging" /><environmentVariable name="DOTNET_ENVIRONMENT" value="Development" /><environmentVariable name="DOTNET_ENVIRONMENT" value="Staging" /><environmentVariable name="NOVAFLEET_INSTALLER_VERIFY" value="1" /><environmentVariable name="NOVAFLEET_INSTALLER_VERIFY" value="1" /></environmentVariables></aspNetCore></system.webServer></configuration>'
    Set-Content -LiteralPath (Join-Path $duplicateRoot 'web.config') -Value $duplicateConfig
    Set-InstallerProductionEnvironment $duplicateRoot 443
    Set-InstallerVerification $duplicateRoot $false
    [xml]$duplicateXml = Get-Content -LiteralPath (Join-Path $duplicateRoot 'web.config') -Raw
    foreach ($name in @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')) {
        $entries = @($duplicateXml.SelectNodes('//environmentVariable[@name="' + $name + '"]'))
        Assert-Upgrade ($entries.Count -eq 2 -and @($entries | Where-Object { $_.value -ne 'Production' }).Count -eq 0) "Duplicate $name entries cannot override Production"
    }
    Assert-Upgrade (@($duplicateXml.SelectNodes('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]')).Count -eq 0) 'Disabling installer verification removes every duplicate flag'

    $script:healthRequests = [Collections.Generic.List[object]]::new()
    function Invoke-WebRequest {
        param([string]$Uri, [hashtable]$Headers, [switch]$UseBasicParsing, [int]$TimeoutSec, [int]$MaximumRedirection, [switch]$SkipCertificateCheck)
        $script:healthRequests.Add([pscustomobject]@{ Uri = $Uri; MaximumRedirection = $MaximumRedirection; SkipCertificateCheck = [bool]$SkipCertificateCheck })
        return [pscustomobject]@{
            StatusCode = 200
            Content = '{"status":"ok","version":"1.1.0","installerVerification":true,"databaseConfigured":true}'
        }
    }
    $healthState = [pscustomobject]@{ HealthUrl = 'https://server.example.test:443/health'; HealthHost = 'server.example.test'; TargetVersion = '1.1.0'; Stage = 'verifying' }
    & $realWaitInstallHealth $healthState
    & $realWaitUpgradeHealth $healthState $true
    Assert-Upgrade ($healthRequests.Count -eq 2) 'Real health loops call the HTTPS endpoint without redirects'
    Assert-Upgrade (@($healthRequests | Where-Object { $_.Uri -ne 'https://server.example.test:443/health' -or $_.MaximumRedirection -ne 0 -or $_.SkipCertificateCheck }).Count -eq 0) 'Health checks enforce TLS validation and disable redirects'

    Assert-Upgrade (Test-InstallerProtectedPath '.novafleet/bootstrap.json') 'Installer manifest protects a root data home'
    Assert-Upgrade (Test-InstallerProtectedPath 'data/.novafleet/bootstrap.json') 'Installer manifest protects application data'
    Assert-Upgrade (Test-InstallerProtectedPath 'logs/stdout.txt') 'Installer manifest protects logs'
    Assert-Upgrade (Test-InstallerProtectedPath 'uploads/mission.jpg') 'Installer manifest protects uploads'
    Assert-Upgrade (Test-InstallerProtectedPath 'web.config') 'Installer manifest protects IIS configuration'
    Assert-Upgrade (Test-InstallerProtectedPath 'appsettings.Production.json') 'Installer manifest protects application configuration'
    Assert-Upgrade (Test-InstallerProtectedPath 'app_offline.htm') 'Installer manifest protects the maintenance marker'
    Assert-Upgrade (-not (Test-InstallerProtectedPath 'wwwroot/assets/app.js')) 'Installer manifest can own frontend payload files'

    $manifestFixture = Join-Path $script:testRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $manifestFixture 'wwwroot\assets'), (Join-Path $manifestFixture 'data'), (Join-Path $manifestFixture 'logs') -Force | Out-Null
    Set-Content (Join-Path $manifestFixture 'old.dll') 'old'
    Set-Content (Join-Path $manifestFixture 'wwwroot\assets\app.js') 'asset'
    Set-Content (Join-Path $manifestFixture 'data\user.db') 'user'
    Set-Content (Join-Path $manifestFixture 'logs\stdout.txt') 'log'
    Set-Content (Join-Path $manifestFixture 'web.config') 'config'
    Set-Content (Join-Path $manifestFixture 'appsettings.json') 'settings'
    Set-Content (Join-Path $manifestFixture 'app_offline.htm') 'offline'
    $manifestStore = Join-Path $script:testRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $manifestStore | Out-Null
    $oldManifest = Join-Path $manifestStore 'old-payload.json'
    $newManifest = Join-Path $manifestStore 'new-payload.json'
    New-InstallerPayloadManifest $manifestFixture $oldManifest
    $manifest = Read-InstallerPayloadManifest $oldManifest $manifestFixture
    Assert-Upgrade ($manifest.format -eq 'novafleet-installer-manifest-v1') 'Payload manifest has an explicit ownership format'
    Assert-Upgrade ($manifest.files.Count -eq 2) 'Payload manifest excludes data, logs, configuration and maintenance files'
    $newPayload = Join-Path $manifestStore 'new-package'
    New-Item -ItemType Directory -Path (Join-Path $newPayload 'wwwroot\assets') -Force | Out-Null
    Set-Content (Join-Path $newPayload 'new.dll') 'new'
    Set-Content (Join-Path $newPayload 'wwwroot\assets\app.js') 'asset-new'
    Copy-Item (Join-Path $newPayload 'new.dll') (Join-Path $manifestFixture 'new.dll') -Force
    Set-Content (Join-Path $manifestFixture 'wwwroot\assets\app.js') 'asset-new'
    Set-Content (Join-Path $manifestFixture 'uploads') 'not-a-directory'
    New-InstallerPayloadManifest $newPayload $newManifest
    $obsolete = @(Get-ObsoletePayloadFiles $oldManifest $newManifest $manifestFixture)
    Assert-Upgrade ($obsolete.Count -eq 1 -and $obsolete[0].path -eq 'old.dll') 'Only files absent from the new manifest are obsolete'
    $removed = Remove-ObsoletePayload $manifestFixture $oldManifest $newManifest
    Assert-Upgrade ($removed -eq 1) 'Obsolete cleanup reports the exact removal count'
    Assert-Upgrade (-not (Test-Path (Join-Path $manifestFixture 'old.dll'))) 'Proven obsolete payload is removed'
    Assert-Upgrade ((Get-Content (Join-Path $manifestFixture 'wwwroot\assets\app.js') -Raw).Trim() -eq 'asset-new') 'Current payload files are retained'
    Assert-Upgrade ((Get-Content (Join-Path $manifestFixture 'data\user.db') -Raw).Trim() -eq 'user') 'Payload cleanup preserves user data'
    Assert-Upgrade ((Get-Content (Join-Path $manifestFixture 'logs\stdout.txt') -Raw).Trim() -eq 'log') 'Payload cleanup preserves logs'
    Assert-Upgrade (Test-Path (Join-Path $manifestFixture 'web.config')) 'Payload cleanup preserves IIS configuration'
    Assert-Upgrade (Test-Path (Join-Path $manifestFixture 'appsettings.json')) 'Payload cleanup preserves application settings'
    Assert-Upgrade (Test-Path (Join-Path $manifestFixture 'app_offline.htm')) 'Payload cleanup preserves the maintenance marker'
    Assert-Upgrade (Test-Path (Join-Path $manifestFixture 'uploads')) 'Payload cleanup preserves unrecognized user content'

    Set-Content (Join-Path $manifestFixture 'modified.dll') 'original'
    New-InstallerPayloadManifest $manifestFixture $oldManifest
    Set-Content (Join-Path $manifestFixture 'modified.dll') 'user-modified'
    Set-Content (Join-Path $newPayload 'other-new.dll') 'new'
    Copy-Item (Join-Path $newPayload 'other-new.dll') (Join-Path $manifestFixture 'other-new.dll') -Force
    New-InstallerPayloadManifest $newPayload $newManifest
    Assert-UpgradeThrows { Remove-ObsoletePayload $manifestFixture $oldManifest $newManifest } 'Modified installer-owned files fail closed instead of being deleted'
    Assert-Upgrade ((Get-Content (Join-Path $manifestFixture 'modified.dll') -Raw).Trim() -eq 'user-modified') 'Hash mismatch preserves the modified file'

    foreach ($badPath in @('../outside.dll', 'data/user.db', 'C:\outside.dll')) {
        $bad = Join-Path $manifestStore 'bad-payload.json'
        Set-Content $bad ('{"format":"novafleet-installer-manifest-v1","files":[{"path":"' + $badPath + '","length":1,"sha256":"' + ('a' * 64) + '"}]}')
        Assert-UpgradeThrows { Read-InstallerPayloadManifest $bad $manifestFixture } "Unsafe manifest path is rejected: $badPath"
    }

    New-UpgradeFixture
    $fixtureData = Join-Path $app 'data'
    New-Item -ItemType Directory -Path (Join-Path $fixtureData '.novafleet') | Out-Null
    $poolVariables = @('NOVAFLEET_ConnectionStrings__Default', 'ConnectionStrings__Default', 'DOTNET_ENVIRONMENT', 'SQL_HOST', 'DB_NAME', 'APP_LOGIN', 'APP_PASSWORD') | ForEach-Object { [pscustomobject]@{ name = $_; value = '' } }
    $poolVariables += [pscustomobject]@{ name = 'NOVAFLEET_HOME'; value = $fixtureData }
    $poolVariables += [pscustomobject]@{ name = 'ASPNETCORE_ENVIRONMENT'; value = 'UpgradeTest' }
    $testPool = [pscustomobject]@{ environmentVariables = @{ Collection = $poolVariables } }
    Set-Content (Join-Path $app 'appsettings.json') '{"ConnectionStrings":{"Default":"Server=(localdb)\\MSSQLLocalDB;Database=SyntheticBase;Integrated Security=True"}}'
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticBase') 'Base application configuration resolves without opening SQL'
    Set-Content (Join-Path $app 'appsettings.UpgradeTest.json') '{"ConnectionStrings":{"Default":"Server=(localdb)\\MSSQLLocalDB;Database=SyntheticEnvironment;Integrated Security=True"}}'
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticEnvironment') 'Environment-specific settings override base settings'
    ($poolVariables | Where-Object name -eq 'ConnectionStrings__Default').value = 'Server=(localdb)\MSSQLLocalDB;Database=SyntheticPool;Integrated Security=True'
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticPool') 'Pool environment overrides JSON settings'
    $fixtureBootstrap = Join-Path $fixtureData '.novafleet\bootstrap.json'
    Set-Content $fixtureBootstrap '{"connectionString":"Server=(localdb)\\MSSQLLocalDB;Database=SyntheticBootstrap;Integrated Security=True"}'
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticBootstrap') 'Bootstrap overrides standard connection environment'
    ($poolVariables | Where-Object name -eq 'NOVAFLEET_ConnectionStrings__Default').value = 'Server=(localdb)\MSSQLLocalDB;Database=SyntheticPrefixed;Integrated Security=True'
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticPrefixed') 'Prefixed connection environment overrides bootstrap'
    [xml]$connectionConfig = Get-Content (Join-Path $app 'web.config') -Raw
    $connectionEntry = $connectionConfig.CreateElement('environmentVariable')
    $connectionEntry.SetAttribute('name', 'NOVAFLEET_ConnectionStrings__Default')
    $connectionEntry.SetAttribute('value', 'Server=(localdb)\MSSQLLocalDB;Database=SyntheticWeb;Integrated Security=True')
    [void]$connectionConfig.SelectSingleNode('//environmentVariables').AppendChild($connectionEntry)
    $connectionConfig.Save((Join-Path $app 'web.config'))
    Assert-Upgrade ((& $realGetConnection $app $testPool).Database -eq 'SyntheticWeb') 'Web configuration overrides the pool environment'
    $connectionEntry.SetAttribute('value', 'Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True')
    $connectionConfig.Save((Join-Path $app 'web.config'))
    Assert-UpgradeThrows { & $realGetConnection $app $testPool } 'System databases cannot be upgraded'
    ($poolVariables | Where-Object name -eq 'NOVAFLEET_HOME').value = 'relative-data'
    Assert-UpgradeThrows { & $realGetConnection $app $testPool } 'Relative data directories cannot be upgraded'

    New-UpgradeFixture
    Set-UpgradeVerification $app
    Set-UpgradeVerification $app
    [xml]$config = Get-Content (Join-Path $app 'web.config') -Raw
    Assert-Upgrade ($config.SelectNodes('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]').Count -eq 1) 'Verification flag is idempotent'
    Assert-Upgrade ($config.SelectSingleNode('//environmentVariable[@name="CUSTOM_SETTING"]').value -eq 'preserve') 'Custom IIS environment survives'
    Set-Content (Join-Path $app 'appsettings.Production.json') '{"custom":"created-after-backup"}'
    Set-Content (Join-Path $app 'appsettings.json') '{"custom":"overwritten"}'
    Copy-Item (Join-Path $newPayload 'new.dll') (Join-Path $app 'new.dll') -Force
    New-Item -ItemType Directory -Path (Join-Path $app 'data\.novafleet'), (Join-Path $app 'logs'), (Join-Path $app 'uploads') -Force | Out-Null
    Set-Content (Join-Path $app 'data\user.db') 'user-data'
    Set-Content (Join-Path $app 'logs\stdout.txt') 'user-log'
    Set-Content (Join-Path $app 'uploads\photo.jpg') 'user-upload'
    Set-Content (Join-Path $app 'user-notes.txt') 'user-notes'
    [xml]$legacyConfig = Get-Content (Join-Path $backup 'files\web.config') -Raw
    foreach ($name in @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')) {
        $entry = $legacyConfig.CreateElement('environmentVariable')
        $entry.SetAttribute('name', $name); $entry.SetAttribute('value', 'Development')
        [void]$legacyConfig.SelectSingleNode('//environmentVariables').AppendChild($entry)
    }
    $legacyConfig.Save((Join-Path $backup 'files\web.config'))
    $legacyConfig.Save((Join-Path $app 'web.config'))
    Finish-Upgrade $journal
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'old.dll'))) 'Successful upgrade prunes proven obsolete payload after verification'
    Assert-Upgrade (Test-Path (Join-Path $app 'new.dll')) 'Successful upgrade retains the new payload'
    Assert-Upgrade ((Get-Content (Join-Path $app 'data\user.db') -Raw).Trim() -eq 'user-data') 'Upgrade cleanup preserves data'
    Assert-Upgrade ((Get-Content (Join-Path $app 'logs\stdout.txt') -Raw).Trim() -eq 'user-log') 'Upgrade cleanup preserves logs'
    Assert-Upgrade ((Get-Content (Join-Path $app 'uploads\photo.jpg') -Raw).Trim() -eq 'user-upload') 'Upgrade cleanup preserves uploads'
    Assert-Upgrade ((Get-Content (Join-Path $app 'user-notes.txt') -Raw).Trim() -eq 'user-notes') 'Upgrade cleanup preserves unowned user files'
    Assert-Upgrade ((Get-Content (Join-Path $app 'appsettings.Production.json') -Raw | ConvertFrom-Json).custom -eq 'created-after-backup') 'Upgrade preserves configuration created after the backup instead of deleting it'
    Assert-Upgrade ((Get-Content (Join-Path $app 'appsettings.json') -Raw | ConvertFrom-Json).custom -eq 'preserve') 'Existing application settings survive payload replacement'
    [xml]$upgradedConfig = Get-Content (Join-Path $app 'web.config') -Raw
    Assert-Upgrade ($upgradedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]').value -eq 'Production') 'Upgrade replaces a legacy Development ASP.NET environment with Production'
    Assert-Upgrade ($upgradedConfig.SelectSingleNode('//environmentVariable[@name="DOTNET_ENVIRONMENT"]').value -eq 'Production') 'Upgrade cannot leave a higher-priority Development environment active'
    Assert-Upgrade ($upgradedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_HTTPS_PORT"]').value -eq '443') 'Upgrade tells HTTPS redirection the validated non-default-capable port'
    Assert-Upgrade ($upgradedConfig.SelectSingleNode('//environmentVariable[@name="CUSTOM_SETTING"]').value -eq 'preserve') 'Upgrade preserves custom IIS environment settings'
    Assert-Upgrade ((Get-Content $journal -Raw | ConvertFrom-Json).Stage -eq 'completed') 'Successful upgrade commits its journal'
    Restore-Upgrade $journal
    Assert-Upgrade (-not (Test-Path $journal)) 'Completed journal can be cleared without rollback'
    Assert-Upgrade (-not $events.Contains('restore-sql')) 'Success never restores SQL'

    New-UpgradeFixture
    $upgradeHasHttpsBinding = $true
    $installResources.Ssl['443'] = 'OLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLD'
    Finish-Upgrade $journal
    Assert-Upgrade ($installResources.Ssl['443'] -eq 'A1B2C3') 'Upgrade applies a renewed certificate to an existing HTTPS binding'
    Assert-Upgrade (-not $events.Contains('add-upgrade-https')) 'Certificate renewal does not add a duplicate IIS binding'

    New-UpgradeFixture
    $upgradeHasHttpsBinding = $true
    $oldThumbprint = 'OLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLDOLD'
    $installResources.Ssl['443'] = $oldThumbprint
    $healthFails = $true
    Assert-UpgradeThrows { Finish-Upgrade $journal } 'Failed health after certificate renewal remains recoverable'
    Restore-Upgrade $journal
    Assert-Upgrade ($installResources.Ssl['443'] -eq $oldThumbprint) 'Failed upgrade restores the previous SSL certificate'

    New-UpgradeFixture
    Set-Content (Join-Path $app 'new.dll') 'new'
    Set-Content (Join-Path $app 'late-user-notes.txt') 'created-after-backup'
    $state.Stage = 'verifying'; Save-UpgradeState $state $journal
    Restore-Upgrade $journal
    Assert-Upgrade ($events.IndexOf('restore-sql') -lt $events.IndexOf('restore-files')) 'SQL restores before old application files'
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'new.dll'))) 'Rollback removes files introduced by the failed version'
    Assert-Upgrade ((Get-Content (Join-Path $app 'old.dll') -Raw).Trim() -eq 'old') 'Rollback restores the previous installer-owned payload'
    Assert-Upgrade ((Get-Content (Join-Path $app 'late-user-notes.txt') -Raw).Trim() -eq 'created-after-backup') 'Rollback preserves unrelated files created after the backup'
    Assert-Upgrade ((Get-Content (Join-Path $app 'NovaFleet.payload.json') -Raw) -eq (Get-Content $state.OldManifest -Raw)) 'Rollback restores the previous payload ownership manifest'
    Assert-Upgrade (-not (Test-Path $journal)) 'Successful recovery clears the pending journal'

    New-UpgradeFixture
    Set-Content (Join-Path $app 'new.dll') 'modified-after-install'
    $state.Stage = 'verifying'; Save-UpgradeState $state $journal
    Assert-UpgradeThrows { Restore-Upgrade $journal } 'Rollback refuses to delete a modified new payload file'
    Assert-Upgrade ((Get-Content (Join-Path $app 'new.dll') -Raw).Trim() -eq 'modified-after-install') 'Failed rollback preserves the modified file for administrator review'
    Assert-Upgrade (Test-Path $journal) 'Unsafe rollback retains its recovery journal'

    New-UpgradeFixture
    Restore-Upgrade $journal
    Assert-Upgrade (-not $events.Contains('restore-sql')) 'Copy-phase interruption does not restore an unchanged database'
    Assert-Upgrade ($events.Contains('restore-files')) 'Copy-phase interruption restores files'

    foreach ($failure in @('restoreFails', 'headerInvalid')) {
        New-UpgradeFixture
        Set-Variable -Name $failure -Value $true -Scope Script
        $state.Stage = 'verifying'; Save-UpgradeState $state $journal
        Assert-UpgradeThrows { Restore-Upgrade $journal } "$failure blocks unsafe recovery"
        Assert-Upgrade (-not $events.Contains('restore-files') -and -not $events.Contains('restore-pool')) 'Failed SQL recovery never starts old code against new schema'
        Assert-Upgrade (Test-Path $journal) 'Failed recovery retains its journal'
    }

    New-UpgradeFixture
    $state.Stage = 'accepted'; Save-UpgradeState $state $journal
    Assert-UpgradeThrows { Restore-Upgrade $journal } 'Automatic SQL rollback is forbidden after live traffic opens'
    Assert-Upgrade (-not $events.Contains('restore-sql')) 'Potential new user writes are never discarded automatically'

    New-UpgradeFixture
    $healthFails = $true
    Assert-UpgradeThrows { Finish-Upgrade $journal } 'Failed verification stops the upgrade'
    Assert-Upgrade ((Get-Content $journal -Raw | ConvertFrom-Json).Stage -eq 'verifying') 'Failed verification remains recoverable'
    Restore-Upgrade $journal
    Assert-Upgrade ($events.Contains('restore-sql')) 'Migration failure recovers the database'
    Assert-Upgrade ($events.Contains('remove-upgrade-https') -and $events.Contains('remove-ssl:443')) 'Failed upgrade removes only HTTPS bindings created during verification'
    Assert-Upgrade (-not $installResources.Ssl.ContainsKey('443')) 'Failed upgrade leaves no installer-created SSL binding'

    New-UpgradeFixture
    Assert-UpgradeThrows { Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443 } 'Pending upgrade cannot be overwritten'
    Remove-Item $journal
    $sharedPool = $true
    Assert-UpgradeThrows { Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443 } 'Shared pool is rejected before changes'
    Assert-Upgrade ($events.Count -eq 0) 'Preflight failure leaves the site running'

    New-UpgradeFixture
    Remove-Item $journal
    Assert-UpgradeThrows { Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint '' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443 } 'Upgrade certificate preflight fails before backup or IIS changes'
    Assert-Upgrade ($events.Count -eq 0 -and -not (Test-Path $journal)) 'Missing upgrade certificate leaves the running site and journal untouched'
    Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443
    $prepared = Get-Content $journal -Raw | ConvertFrom-Json
    Assert-Upgrade ($prepared.HealthUrl -eq 'https://server.example.test:443/health') 'HTTPS-only existing site upgrades without an HTTP health dependency'
    Assert-Upgrade ($events.IndexOf('stop') -lt $events.IndexOf('backup-files')) 'Workers stop before file backup'
    Assert-Upgrade ($events.Contains('backup-sql')) 'SQL backup is required before replacing files'
    Assert-Upgrade ((Get-Content $journal -Raw | ConvertFrom-Json).Stage -eq 'copying') 'Only verified backups permit copying'

    New-UpgradeFixture
    Remove-Item $journal
    Set-Content (Join-Path $app 'old.dll') 'user-modified-before-upgrade'
    Assert-UpgradeThrows { Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443 } 'Modified installer-owned payload blocks upgrade before backup'
    Assert-Upgrade ($events.Count -eq 0 -and -not (Test-Path $journal)) 'Payload integrity preflight leaves the site and journal untouched'

    New-UpgradeFixture
    Remove-Item $journal
    $backupFails = $true
    Assert-UpgradeThrows { Prepare-Upgrade 'TestSite' $app '1.1.0' $journal -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -NewManifestPath $newManifest -HttpsPort 443 } 'Backup failure aborts upgrade'
    Assert-Upgrade (-not (Test-Path $journal)) 'Preflight failure recovers original site state'
    Assert-Upgrade (-not $events.Contains('restore-sql')) 'Backup failure never restores SQL'

    New-FreshInstallFixture
    Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal
    Assert-Upgrade (-not (Test-Path $journal)) 'Successful fresh install clears its pending journal'
    Assert-Upgrade ($installResources.Bindings['TestSite'].Count -eq 1 -and $installResources.Bindings['TestSite'][0] -like 'https:*:443:server.example.test') 'Fresh site has only the validated HTTPS binding'
    Assert-Upgrade ($installResources.Ssl['443'] -eq 'A1B2C3') 'Fresh HTTPS port is registered with the validated certificate'
    Assert-Upgrade ($installResources.Firewall.ContainsKey('NovaFleet-TestSite-443')) 'Fresh firewall rule allows only the HTTPS port'
    [xml]$acceptedConfig = Get-Content (Join-Path $app 'web.config') -Raw
    Assert-Upgrade ($acceptedConfig.SelectSingleNode('//environmentVariable[@name="CUSTOM_SETTING"]').value -eq 'preserve') 'Fresh install restores prior custom IIS configuration after acceptance'
    Assert-Upgrade ($acceptedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]').value -eq 'Production') 'Fresh install cannot leave a legacy Development environment active'
    Assert-Upgrade ($acceptedConfig.SelectSingleNode('//environmentVariable[@name="DOTNET_ENVIRONMENT"]').value -eq 'Production') 'Fresh install forces both host environment selectors to Production'
    Assert-Upgrade ($acceptedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_HTTPS_PORT"]').value -eq '443') 'Fresh install publishes the HTTPS redirect port after restoring configuration'
    Assert-Upgrade ($null -eq $acceptedConfig.SelectSingleNode('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]')) 'Fresh install disables verification before accepted traffic'
    Assert-Upgrade ((Get-Content (Join-Path $app 'data\user.db') -Raw).Trim() -eq 'preserve-user-data') 'Fresh install preserves user data'
    Assert-Upgrade ((Get-Content (Join-Path $app 'appsettings.json') -Raw).Trim() -eq '{"custom":"preserve"}') 'Fresh install preserves application configuration'
    Assert-Upgrade (-not $installEvents.Contains('remove-site') -and -not $installEvents.Contains('remove-pool')) 'Successful fresh install does not run destructive recovery'

    New-FreshInstallFixture
    Remove-Item (Join-Path $app 'web.config') -Force
    Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal
    Assert-Upgrade (Test-Path (Join-Path $app 'web.config')) 'Fresh install keeps the generated web.config for normal traffic'
    [xml]$generatedConfig = Get-Content (Join-Path $app 'web.config') -Raw
    Assert-Upgrade ($generatedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]').value -eq 'Production') 'Generated fresh-install configuration remains Production'
    Assert-Upgrade ($generatedConfig.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_HTTPS_PORT"]').value -eq '443') 'Generated fresh-install configuration publishes the HTTPS redirect port'
    Assert-Upgrade ($null -eq $generatedConfig.SelectSingleNode('//environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]')) 'Fresh install disables verification mode before normal traffic'

    New-FreshInstallFixture
    $installFailAt = 'health'
    Assert-UpgradeThrows { Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal } 'Fresh health failure triggers recovery'
    Assert-Upgrade (-not (Test-Path $journal)) 'Recovered fresh install clears its pending journal'
    Assert-Upgrade (-not $installResources.Sites.ContainsKey('TestSite') -and -not $installResources.Pools.ContainsKey('TestSite')) 'Fresh recovery removes only the site and pool it created'
    Assert-Upgrade (-not $installResources.Firewall.ContainsKey('NovaFleet-TestSite-443') -and -not $installResources.Ssl.ContainsKey('443')) 'Fresh recovery removes its firewall and SSL bindings'
    Assert-Upgrade ((Get-Content (Join-Path $app 'web.config') -Raw).Trim() -eq $originalConfig) 'Fresh recovery restores the prior web.config'
    Assert-Upgrade ((Get-Content (Join-Path $app 'data\user.db') -Raw).Trim() -eq 'preserve-user-data') 'Fresh recovery leaves user data untouched'
    Assert-Upgrade ($installEvents.IndexOf('remove-site') -lt $installEvents.IndexOf('remove-pool')) 'Fresh recovery removes the site before its pool'

    New-FreshInstallFixture
    $installFailAt = 'firewall'
    Assert-UpgradeThrows { Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal } 'Mid-install IIS failure triggers recovery'
    Assert-Upgrade (-not $installResources.Sites.ContainsKey('TestSite') -and -not $installResources.Pools.ContainsKey('TestSite') -and -not $installResources.Ssl.ContainsKey('443')) 'Mid-install recovery removes every created IIS resource'

    New-FreshInstallFixture
    $installResources.Sites['Unrelated'] = $true; $installResources.Pools['Unrelated'] = $true
    Assert-UpgradeThrows { Install-FleetIis -SiteName 'Unrelated' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal } 'Fresh install refuses an existing site or pool'
    Assert-Upgrade (-not (Test-Path $journal) -and $installResources.Sites['Unrelated'] -and $installResources.Pools['Unrelated']) 'Pre-existing IIS resources are neither journaled nor removed'
    Assert-Upgrade (-not $installEvents.Contains('remove-site') -and -not $installEvents.Contains('remove-pool')) 'Preflight failure never removes unrelated resources'

    New-FreshInstallFixture
    $script:certificate = $null
    Assert-UpgradeThrows { Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal } 'Fresh install requires a valid certificate before changing IIS'
    Assert-Upgrade (-not (Test-Path $journal) -and $installEvents.Count -eq 0) 'Certificate preflight failure leaves IIS and the journal untouched'

    New-FreshInstallFixture
    Remove-Item (Join-Path $app 'web.config') -Force
    Remove-Item (Join-Path $app 'data') -Recurse -Force
    $installFailAt = 'firewall'
    Assert-UpgradeThrows { Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal } 'Fresh recovery also handles a directory without prior configuration'
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'web.config'))) 'Fresh recovery removes web.config only when it created the file'
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'data'))) 'Fresh recovery removes only empty data directories it created'
    Assert-Upgrade (Test-Path (Join-Path $app 'appsettings.json')) 'Fresh recovery never deletes application configuration'

    New-FreshInstallFixture
    $script:certificate = [pscustomobject]@{
        Thumbprint = 'A1B2C3'; HasPrivateKey = $true; NotBefore = [DateTime]::UtcNow.AddDays(-1); NotAfter = [DateTime]::UtcNow.AddDays(30)
        DnsNameList = @([pscustomobject]@{ Value = 'server.example.test' }); Subject = 'CN=server.example.test'
    }
    $preparedApp = Join-Path $fixture 'prepared-app'
    $package = Join-Path $fixture 'package'
    $prepareJournal = Join-Path $fixture 'state\prepare-pending.json'
    $packageManifest = Join-Path $fixture 'new-payload.json'
    New-Item -ItemType Directory -Path $package | Out-Null
    Set-Content (Join-Path $package 'new.dll') 'new'
    New-InstallerPayloadManifest $package $packageManifest
    Prepare-FreshInstall -SiteName 'TestSite' -Port 443 -AppDir $preparedApp -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $prepareJournal -NewManifestPath $packageManifest
    $prepared = Get-Content $prepareJournal -Raw | ConvertFrom-Json
    Assert-Upgrade ($prepared.Stage -eq 'prepared' -and $prepared.PayloadPrepared -and -not $prepared.AppDirectoryExisted) 'Fresh prepare journals ownership before Setup copies files'
    Assert-Upgrade ((Test-Path (Join-Path $prepared.BackupDir 'Install-Iis.ps1')) -and (Test-Path $prepared.NewManifest)) 'Fresh prepare saves recovery scripts and the new package manifest'
    Copy-Item (Join-Path $package '*') $preparedApp -Recurse -Force
    Copy-Item $packageManifest (Join-Path $preparedApp 'NovaFleet.payload.json') -Force
    New-Item -ItemType Directory -Path (Join-Path $preparedApp 'data\.novafleet') -Force | Out-Null
    Restore-FreshInstall $prepareJournal
    Assert-Upgrade (-not (Test-Path $preparedApp)) 'Fresh cancellation removes an app directory created by prepare after payload cleanup'
    Assert-Upgrade (-not (Test-Path $prepareJournal)) 'Direct fresh prepare and cancellation clears its journal'

    New-PreparedFreshFixture
    Install-FleetIis -SiteName 'TestSite' -Port 443 -AppDir $app -CertificateThumbprint 'A1B2C3' -CertificateHostName 'server.example.test' -Version '1.1.0' -StateFile $journal
    Assert-Upgrade (-not (Test-Path $journal)) 'Fresh install completes from a journal prepared before file copy'
    Assert-Upgrade (Test-Path (Join-Path $app 'new.dll')) 'Successful prepared install retains the new payload'
    Assert-Upgrade ($installResources.Bindings['TestSite'].Count -eq 1 -and $installResources.Bindings['TestSite'][0] -like 'https:*:443:server.example.test') 'Prepared fresh install exposes only HTTPS'

    New-PreparedFreshFixture
    Set-Content (Join-Path $app 'late-user-notes.txt') 'created-after-prepare'
    $state.Stage = 'prepared'; Save-InstallerState $state $journal
    Restore-FreshInstall $journal
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'new.dll'))) 'Cancelled fresh install removes hash-matching payload copied after prepare'
    Assert-Upgrade (-not (Test-Path (Join-Path $app 'NovaFleet.payload.json'))) 'Cancelled fresh install removes its copied ownership manifest'
    Assert-Upgrade ((Get-Content (Join-Path $app 'late-user-notes.txt') -Raw).Trim() -eq 'created-after-prepare') 'Fresh cancellation preserves unrelated files created after prepare'
    Assert-Upgrade (-not (Test-Path $journal)) 'Successful fresh cancellation clears the pending journal'

    New-PreparedFreshFixture
    Set-Content (Join-Path $app 'new.dll') 'modified-after-copy'
    $state.Stage = 'prepared'; Save-InstallerState $state $journal
    Assert-UpgradeThrows { Restore-FreshInstall $journal } 'Fresh cancellation refuses to delete modified installer payload'
    Assert-Upgrade ((Get-Content (Join-Path $app 'new.dll') -Raw).Trim() -eq 'modified-after-copy') 'Unsafe fresh cancellation preserves the modified file'
    Assert-Upgrade (Test-Path $journal) 'Unsafe fresh cancellation retains its recovery journal'

    New-PreparedFreshFixture
    Set-Content (Join-Path $app 'NovaFleet.payload.json') '{"format":"tampered","files":[]}'
    $state.Stage = 'prepared'; Save-InstallerState $state $journal
    Assert-UpgradeThrows { Restore-FreshInstall $journal } 'Fresh cancellation rejects a modified ownership manifest before cleanup'
    Assert-Upgrade (Test-Path (Join-Path $app 'new.dll')) 'Modified ownership metadata leaves all payload files untouched'
    Assert-Upgrade (Test-Path $journal) 'Modified ownership metadata retains the recovery journal'

    Write-Host "PASS: $checks installer upgrade checks (mock IIS/SQL, real isolated file copies)."
} finally {
    Remove-Item -LiteralPath $script:testRoot -Recurse -Force
}
exit 0
