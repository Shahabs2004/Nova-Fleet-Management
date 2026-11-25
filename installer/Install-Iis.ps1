param(
    [string]$SiteName,
    [int]$Port,
    [string]$AppDir,
    [string]$CertificateThumbprint = '',
    [string]$CertificateHostName = '',
    [string]$BundlePath = '',
    [string]$Version = '',
    [string]$StateFile = '',
    [string]$NewManifestPath = '',
    [ValidateSet('Prepare', 'Install', 'Recover')] [string]$Phase = 'Install',
    [switch]$FunctionsOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')

function Get-InstallAppCmd {
    $path = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
    if (-not (Test-Path $path)) { throw 'IIS is not installed. Enable the Web-Server role first.' }
    return $path
}

function Initialize-IisPrerequisites([string]$AppCmd, [string]$BundlePath) {
    $features = @('IIS-WebServerRole','IIS-WebServer','IIS-WebServerManagementTools','IIS-ManagementConsole','IIS-ManagementScriptingTools','IIS-ApplicationDevelopment')
    $installed = dism /online /get-featureinfo /featurename:IIS-WebServerRole 2>$null | Out-String
    if ($installed -notmatch 'State : Enabled') {
        Write-Host 'Enabling IIS features.'
        foreach ($feature in $features) {
            dism /online /enable-feature /featurename:$feature /quiet /norestart | Out-Null
            if ($LASTEXITCODE -notin @(0, 3010)) { throw "Enabling IIS feature $feature failed." }
        }
    }

    $ancmPath = Join-Path ${env:ProgramFiles} 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    function Get-AncmMajor { if (Test-Path $ancmPath) { [int]((Get-Item $ancmPath).VersionInfo.FileVersion.Split('.')[0]) } else { 0 } }
    $module = (& $AppCmd list module /name:AspNetCoreModuleV2 2>&1) -join ' '
    $major = Get-AncmMajor
    if (($module -notmatch 'AspNetCoreModuleV2') -or ($major -lt 20)) {
        if ($BundlePath -and (Test-Path $BundlePath)) {
            Write-Host 'Installing the bundled .NET IIS Hosting Bundle.'
            $process = Start-Process $BundlePath -ArgumentList '/quiet /norestart' -Wait -PassThru
            if ($process.ExitCode -notin @(0, 3010)) { throw "The Hosting Bundle exited with code $($process.ExitCode)." }
            $module = (& $AppCmd list module /name:AspNetCoreModuleV2 2>&1) -join ' '
            $major = Get-AncmMajor
        }
    }
    if ($module -notmatch 'AspNetCoreModuleV2' -or $major -lt 20) {
        throw 'AspNetCoreModuleV2 version 20.x or newer is required. Install the .NET 10 IIS Hosting Bundle and retry.'
    }
    Import-Module WebAdministration
}

function Get-IisSite([string]$Name) {
    try { return Get-Website -Name $Name -ErrorAction SilentlyContinue } catch { return $null }
}

function Get-IisAppPool([string]$Name) {
    try { return Get-WebAppPool -Name $Name -ErrorAction SilentlyContinue } catch { return $null }
}

function New-InstallAppPool([string]$Name) { New-WebAppPool -Name $Name | Out-Null }

function Set-InstallAppPoolSettings([string]$Name, [string]$AppCmd) {
    $pool = Get-Item "IIS:\AppPools\$Name"
    $pool.managedRuntimeVersion = ''
    $pool.managedPipelineMode = 'Integrated'
    $pool.startMode = 'AlwaysRunning'
    $pool | Set-Item
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.identityType -Value ApplicationPoolIdentity
    Set-ItemProperty "IIS:\AppPools\$Name" -Name startMode -Value AlwaysRunning
    Set-ItemProperty "IIS:\AppPools\$Name" -Name autoStart -Value $true
    & $AppCmd set apppool "$Name" /enable32BitAppOnWin64:false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the 64-bit application pool.' }
}

function New-InstallSite([string]$Name, [string]$Root, [string]$PoolName, [int]$HttpsPort, [string]$HostName) {
    New-Website -Name $Name -PhysicalPath $Root -ApplicationPool $PoolName -Port $HttpsPort -Ssl -HostHeader $HostName | Out-Null
}

function Test-InstallFirewallRule([string]$Name) {
    & netsh.exe advfirewall firewall show rule "name=$Name" 2>&1 | Out-Null
    return $LASTEXITCODE -eq 0
}

function New-InstallFirewallRule([string]$Name, [int]$HttpsPort) {
    & netsh.exe advfirewall firewall add rule "name=$Name" dir=in action=allow protocol=TCP "localport=$HttpsPort" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the HTTPS firewall rule.' }
}

function Remove-InstallFirewallRule([string]$Name) { & netsh.exe advfirewall firewall delete rule "name=$Name" | Out-Null }

function Grant-InstallDataAccess([string]$Path, [string]$Name) {
    & icacls.exe $Path /grant ('IIS AppPool\' + $Name + ':(OI)(CI)M') /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the application pool access to the data directory.' }
}

function Protect-InstallerDirectory([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict installer recovery directory permissions.' }
}

function Render-InstallWebConfig([string]$Root, [int]$HttpsPort) {
    $template = Join-Path $Root 'installer\web.config.template'
    if (-not (Test-Path -LiteralPath $template)) { throw 'The installer web.config template is missing.' }
    $dataDirectory = Join-Path $Root 'data'
    $rendered = (Get-Content -LiteralPath $template -Raw).Replace('{{DATA_DIR}}', $dataDirectory).Replace('{{HTTPS_PORT}}', [string]$HttpsPort)
    [System.IO.File]::WriteAllText((Join-Path $Root 'web.config'), $rendered, (New-Object System.Text.UTF8Encoding($true)))
}

function Start-InstallSite([string]$SiteName) {
    Start-WebAppPool -Name $SiteName
    Start-Website -Name $SiteName
}

function Stop-IisSite([string]$Name) {
    $site = Get-IisSite $Name
    if ($null -ne $site -and $site.State -ne 'Stopped') { Stop-Website -Name $Name }
}

function Stop-IisAppPool([string]$Name) {
    $pool = Get-IisAppPool $Name
    if ($null -ne $pool) {
        $state = Get-WebAppPoolState -Name $Name
        if ($state.Value -ne 'Stopped') { Stop-WebAppPool -Name $Name }
    }
}

function Remove-IisSite([string]$Name) { if ($null -ne (Get-IisSite $Name)) { Remove-Website -Name $Name } }
function Remove-IisAppPool([string]$Name) { if ($null -ne (Get-IisAppPool $Name)) { Remove-WebAppPool -Name $Name } }

function Convert-InstallVersion([string]$Value) {
    $version = [version]$Value
    return [version]('{0}.{1}.{2}.{3}' -f $version.Major, $version.Minor, [Math]::Max(0, $version.Build), [Math]::Max(0, $version.Revision))
}

function Wait-InstallHealth($State) {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        try {
            $response = Invoke-WebRequest -Uri $State.HealthUrl -UseBasicParsing -TimeoutSec 10 -MaximumRedirection 0
            $health = $response.Content | ConvertFrom-Json
            $versionMatches = -not $State.TargetVersion -or ((Convert-InstallVersion $health.version) -eq (Convert-InstallVersion $State.TargetVersion))
            $verificationMatches = $health.installerVerification -eq ($State.Stage -eq 'verifying')
            if ($response.StatusCode -eq 200 -and $health.status -eq 'ok' -and $verificationMatches -and $versionMatches) { return }
        } catch { }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The expected application version did not pass its HTTPS health check.'
}

function Prepare-FreshInstall([string]$SiteName, [int]$Port, [string]$AppDir, [string]$CertificateThumbprint, [string]$CertificateHostName, [string]$Version, [string]$StateFile, [string]$NewManifestPath) {
    if ($SiteName -notmatch '^[A-Za-z0-9_ -]{1,64}$' -or $Port -lt 1 -or $Port -gt 65535) { throw 'Invalid IIS site name or HTTPS port.' }
    if (-not [System.IO.Path]::IsPathRooted($AppDir) -or $AppDir.TrimEnd('\') -eq [System.IO.Path]::GetPathRoot($AppDir).TrimEnd('\')) { throw 'A dedicated absolute application directory is required.' }
    if (-not (Test-Path -LiteralPath $NewManifestPath)) { throw 'The fresh-install payload manifest is missing.' }
    if (Test-Path -LiteralPath $StateFile) { throw 'An unfinished installation exists. Recover it before retrying.' }
    $AppDir = [System.IO.Path]::GetFullPath($AppDir).TrimEnd('\')
    $CertificateHostName = $CertificateHostName.Trim()
    $certificate = Assert-InstallerCertificate $CertificateThumbprint $CertificateHostName
    [void](Read-InstallerPayloadManifest $NewManifestPath $AppDir)
    $appDirectoryExisted = Test-Path -LiteralPath $AppDir
    if ($appDirectoryExisted) {
        if ((Get-Item -LiteralPath $AppDir -Force).Attributes -match 'ReparsePoint' -or @(Get-ChildItem -LiteralPath $AppDir -Force).Count -gt 0) { throw 'The fresh-install destination must be an empty regular directory.' }
    }
    $backupDirectory = Join-Path (Split-Path $StateFile -Parent) ('installs\' + [Guid]::NewGuid().ToString('N'))
    Protect-InstallerDirectory (Split-Path $StateFile -Parent)
    Protect-InstallerDirectory $backupDirectory
    try {
        if (-not $appDirectoryExisted) { New-Item -ItemType Directory -Path $AppDir | Out-Null }
        Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $backupDirectory 'Install-Iis.ps1')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer-Common.ps1') -Destination $backupDirectory
        Copy-Item -LiteralPath $NewManifestPath -Destination (Join-Path $backupDirectory 'new-payload.json')
        $emptyManifest = Join-Path $backupDirectory 'empty-payload.json'
        [System.IO.File]::WriteAllText($emptyManifest, '{"format":"novafleet-installer-manifest-v1","files":[]}', (New-Object System.Text.UTF8Encoding($false)))
        $state = [pscustomobject]@{
            Stage = 'prepared'; SiteName = $SiteName; PoolName = $SiteName; AppDir = $AppDir; Port = $Port
            CertificateThumbprint = $certificate.Thumbprint; CertificateHostName = $CertificateHostName; TargetVersion = $Version
            HealthUrl = "https://${CertificateHostName}:$Port/health"; HealthHost = $CertificateHostName
            BackupDir = $backupDirectory; CreatedAppPool = $false; CreatedSite = $false; CreatedSslBinding = $false; CreatedFirewallRule = $false
            CreatedWebConfig = $false; WebConfigExisted = $false; DataDirectoryExisted = $false; HomeDirectoryExisted = $false
            AppDirectoryExisted = $appDirectoryExisted; PayloadPrepared = $true; EmptyManifest = $emptyManifest; NewManifest = (Join-Path $backupDirectory 'new-payload.json')
        }
        Save-InstallerState $state $StateFile
    } catch {
        if (-not $appDirectoryExisted -and (Test-Path -LiteralPath $AppDir) -and @(Get-ChildItem -LiteralPath $AppDir -Force).Count -eq 0) { Remove-Item -LiteralPath $AppDir -Force }
        throw
    }
}

function Remove-FreshPayload($State) {
    if (-not $State.PayloadPrepared) { return }
    $root = [System.IO.Path]::GetFullPath($State.AppDir).TrimEnd('\')
    if ((Get-Item -LiteralPath $root -Force).Attributes -match 'ReparsePoint') { throw 'A reparse point prevents safe fresh-install recovery.' }
    $manifest = Read-InstallerPayloadManifest $State.NewManifest $root
    $installedManifest = Join-Path $root 'NovaFleet.payload.json'
    $installedManifestExists = Test-Path -LiteralPath $installedManifest
    if ($installedManifestExists) {
        $item = Get-Item -LiteralPath $installedManifest -Force
        if ($item.PSIsContainer -or $item.Attributes -match 'ReparsePoint' -or (Get-FileHash -LiteralPath $installedManifest -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $State.NewManifest -Algorithm SHA256).Hash) {
            throw 'The copied payload manifest was modified and cannot be removed safely.'
        }
    }
    [void](Remove-ObsoletePayload $root $State.NewManifest $State.EmptyManifest)
    if ($installedManifestExists) { Remove-Item -LiteralPath $installedManifest -Force }
    $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifest.files) {
        $directory = Split-Path (Join-Path $root $entry.path.Replace('/', '\')) -Parent
        while ($directory -and $directory.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            [void]$directories.Add($directory)
            $directory = Split-Path $directory -Parent
        }
    }
    foreach ($directory in ($directories | Sort-Object -Property Length -Descending)) {
        if ((Test-Path -LiteralPath $directory) -and -not ((Get-Item -LiteralPath $directory -Force).Attributes -match 'ReparsePoint') -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory -Force }
    }
}

function Install-FleetIis {
    param(
        [string]$SiteName, [int]$Port, [string]$AppDir, [string]$CertificateThumbprint,
        [string]$CertificateHostName, [string]$BundlePath = '', [string]$Version = '', [string]$StateFile
    )
    if ($SiteName -notmatch '^[A-Za-z0-9_ -]{1,64}$' -or $Port -lt 1 -or $Port -gt 65535) { throw 'Invalid IIS site name or HTTPS port.' }
    if (-not [System.IO.Path]::IsPathRooted($AppDir) -or $AppDir.TrimEnd('\') -eq [System.IO.Path]::GetPathRoot($AppDir).TrimEnd('\')) { throw 'A dedicated absolute application directory is required.' }
    $AppDir = [System.IO.Path]::GetFullPath($AppDir).TrimEnd('\')
    $CertificateHostName = $CertificateHostName.Trim()
    $certificate = Assert-InstallerCertificate $CertificateThumbprint $CertificateHostName
    $thumbprint = $certificate.Thumbprint
    if (-not (Test-Path -LiteralPath $AppDir)) { throw 'The application directory does not exist.' }
    if (Test-Path -LiteralPath (Join-Path $AppDir 'app_offline.htm')) { throw 'The application directory is in maintenance mode.' }
    $preparedState = $null
    if (Test-Path -LiteralPath $StateFile) {
        $preparedState = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
        if ($preparedState.Stage -ne 'prepared' -or -not $preparedState.PayloadPrepared -or
            $preparedState.SiteName -ne $SiteName -or $preparedState.AppDir -ne $AppDir -or $preparedState.Port -ne $Port -or
            $preparedState.CertificateHostName -ne $CertificateHostName -or $preparedState.CertificateThumbprint -ne $thumbprint -or
            $preparedState.TargetVersion -ne $Version -or -not (Test-Path -LiteralPath $preparedState.NewManifest) -or -not (Test-Path -LiteralPath $preparedState.EmptyManifest)) {
            throw 'The prepared installation does not match this request. Recover it before retrying.'
        }
    }
    if ($null -ne (Get-IisSite $SiteName) -or $null -ne (Get-IisAppPool $SiteName)) { throw 'An IIS site or pool with this name already exists; no resources were changed.' }
    $ruleName = "NovaFleet-$SiteName-$Port"
    if ((Test-InstallFirewallRule $ruleName) -or (Test-InstallerSslBinding $Port)) { throw 'The HTTPS port or firewall rule is already in use; no resources were changed.' }

    $appCmd = Get-InstallAppCmd
    $dataDirectory = Join-Path $AppDir 'data'
    $homeDirectory = Join-Path $dataDirectory '.novafleet'
    $backupDirectory = if ($preparedState) { $preparedState.BackupDir } else { Join-Path (Split-Path $StateFile -Parent) ('installs\' + [Guid]::NewGuid().ToString('N')) }
    Protect-InstallerDirectory (Split-Path $StateFile -Parent)
    Protect-InstallerDirectory $backupDirectory
    if ($preparedState) {
        $state = $preparedState
    } else {
        $state = [pscustomobject]@{
            Stage = 'preparing'; SiteName = $SiteName; PoolName = $SiteName; AppDir = $AppDir; Port = $Port
            CertificateThumbprint = $thumbprint; CertificateHostName = $CertificateHostName; TargetVersion = $Version
            HealthUrl = "https://${CertificateHostName}:$Port/health"; HealthHost = $CertificateHostName
            BackupDir = $backupDirectory; CreatedAppPool = $false; CreatedSite = $false; CreatedSslBinding = $false; CreatedFirewallRule = $false
            CreatedWebConfig = $false; WebConfigExisted = (Test-Path -LiteralPath (Join-Path $AppDir 'web.config'))
            DataDirectoryExisted = (Test-Path -LiteralPath $dataDirectory); HomeDirectoryExisted = (Test-Path -LiteralPath $homeDirectory)
            AppDirectoryExisted = $true; PayloadPrepared = $false; EmptyManifest = ''; NewManifest = ''
        }
        if ($state.WebConfigExisted) { Copy-Item -LiteralPath (Join-Path $AppDir 'web.config') -Destination (Join-Path $backupDirectory 'web.config.before') }
        Save-InstallerState $state $StateFile
    }
    try {
        if (-not $state.PayloadPrepared) {
            Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $backupDirectory 'Install-Iis.ps1')
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer-Common.ps1') -Destination $backupDirectory
        }
        $state.Stage = 'copying'; Save-InstallerState $state $StateFile
        Initialize-IisPrerequisites $appCmd $BundlePath

        $state.CreatedAppPool = $true; Save-InstallerState $state $StateFile
        New-InstallAppPool $SiteName
        Set-InstallAppPoolSettings $SiteName $appCmd
        $state.CreatedSite = $true; Save-InstallerState $state $StateFile
        New-InstallSite $SiteName $AppDir $SiteName $Port $CertificateHostName
        if (-not (Test-InstallerSslBinding $Port)) { $state.CreatedSslBinding = $true; Save-InstallerState $state $StateFile }
        elseif (Test-InstallerSslBinding $Port) { throw "HTTPS port $Port already has an SSL certificate binding." }
        Add-InstallerSslBinding $Port $thumbprint

        New-Item -ItemType Directory -Path $homeDirectory -Force | Out-Null
        Grant-InstallDataAccess $dataDirectory $SiteName
        if (-not $state.WebConfigExisted) { $state.CreatedWebConfig = $true; Save-InstallerState $state $StateFile }
        Render-InstallWebConfig $AppDir $Port
        $state.CreatedFirewallRule = $true; Save-InstallerState $state $StateFile
        New-InstallFirewallRule $ruleName $Port

        Set-InstallerVerification $AppDir $true
        $state.Stage = 'verifying'; Save-InstallerState $state $StateFile
        Start-InstallSite $SiteName
        Wait-InstallHealth $state
        Stop-IisSite $SiteName
        Stop-IisAppPool $SiteName
        if ($state.WebConfigExisted) { Copy-Item -LiteralPath (Join-Path $backupDirectory 'web.config.before') -Destination (Join-Path $AppDir 'web.config') -Force }
        Set-InstallerVerification $AppDir $false
        Set-InstallerProductionEnvironment $AppDir $Port
        $state.Stage = 'accepted'; Save-InstallerState $state $StateFile
        Start-InstallSite $SiteName
        Wait-InstallHealth $state
        $state.Stage = 'completed'; Save-InstallerState $state (Join-Path $backupDirectory 'recovery.json')
        Save-InstallerState $state $StateFile
        Remove-Item -LiteralPath $StateFile -Force
    } catch {
        Restore-FreshInstall $StateFile
        throw
    }
}

function Restore-FreshInstall([string]$StateFile) {
    if (-not (Test-Path -LiteralPath $StateFile)) { return }
    $state = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
    if ($state.Stage -eq 'completed') { Remove-Item -LiteralPath $StateFile -Force; return }
    if ($state.Stage -eq 'accepted') { throw 'Normal traffic has started. Automatic fresh-install rollback is blocked; keep the recovery journal for administrator review.' }
    if ($state.CreatedSite) { Stop-IisSite $state.SiteName }
    if ($state.CreatedAppPool) { Stop-IisAppPool $state.PoolName }
    if ($state.CreatedSslBinding) { Remove-InstallerSslBinding $state.Port }
    if ($state.CreatedFirewallRule) { Remove-InstallFirewallRule "NovaFleet-$($state.SiteName)-$($state.Port)" }
    if ($state.CreatedSite) { Remove-IisSite $state.SiteName }
    if ($state.CreatedAppPool) { Remove-IisAppPool $state.PoolName }
    if ($state.WebConfigExisted) {
        Copy-Item -LiteralPath (Join-Path $state.BackupDir 'web.config.before') -Destination (Join-Path $state.AppDir 'web.config') -Force
    } elseif ($state.CreatedWebConfig) {
        Remove-Item -LiteralPath (Join-Path $state.AppDir 'web.config') -Force -ErrorAction SilentlyContinue
    }
    Remove-FreshPayload $state
    $homeDirectory = Join-Path $state.AppDir 'data\.novafleet'
    $dataDirectory = Join-Path $state.AppDir 'data'
    if (-not $state.HomeDirectoryExisted -and (Test-Path -LiteralPath $homeDirectory) -and @(Get-ChildItem -LiteralPath $homeDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $homeDirectory -Force }
    if (-not $state.DataDirectoryExisted -and (Test-Path -LiteralPath $dataDirectory) -and @(Get-ChildItem -LiteralPath $dataDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $dataDirectory -Force }
    if (-not $state.AppDirectoryExisted -and (Test-Path -LiteralPath $state.AppDir) -and @(Get-ChildItem -LiteralPath $state.AppDir -Force).Count -eq 0) { Remove-Item -LiteralPath $state.AppDir -Force }
    $state.Stage = 'recovered'; Save-InstallerState $state (Join-Path $state.BackupDir 'recovery.json')
    Remove-Item -LiteralPath $StateFile -Force
}

if (-not $FunctionsOnly) {
    $log = Join-Path $env:TEMP "NovaFleet-Install-$SiteName.log"
    function Say([string]$Message) { ($Message + "`r`n") | Add-Content $log; Write-Host $Message }
    try {
        if ([string]::IsNullOrWhiteSpace($StateFile)) { $StateFile = Join-Path $env:ProgramData 'NovaFleet\install-pending.json' }
        if ($Phase -eq 'Recover') {
            Restore-FreshInstall $StateFile
            Say 'Fresh-install recovery completed.'
            exit 0
        }
        if ($Phase -eq 'Prepare') {
            Prepare-FreshInstall -SiteName $SiteName -Port $Port -AppDir $AppDir -CertificateThumbprint $CertificateThumbprint `
                -CertificateHostName $CertificateHostName -Version $Version -StateFile $StateFile -NewManifestPath $NewManifestPath
            Say 'Fresh-install recovery journal prepared.'
            exit 0
        }
        Install-FleetIis -SiteName $SiteName -Port $Port -AppDir $AppDir -CertificateThumbprint $CertificateThumbprint `
            -CertificateHostName $CertificateHostName -BundlePath $BundlePath -Version $Version -StateFile $StateFile
        Say 'HEALTH OK; production HTTPS installation completed.'
        exit 0
    } catch {
        Say ("FATAL: " + $_.Exception.Message)
        Write-Error $_
        exit 1
    }
}
