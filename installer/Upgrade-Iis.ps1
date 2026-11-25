param(
    [ValidateSet('CheckNew', 'Prepare', 'Finish', 'Recover')] [string]$Phase,
    [string]$SiteName,
    [string]$AppDir,
    [string]$TargetVersion,
    [string]$StateFile,
    [string]$CertificateThumbprint = '',
    [string]$CertificateHostName = '',
    [string]$NewManifestPath = '',
    [int]$HttpsPort,
    [switch]$FunctionsOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$script:UpgradeScriptPath = $PSCommandPath

function Save-UpgradeState($State, [string]$Path) { Save-InstallerState $State $Path }

function Copy-UpgradeTree([string]$Source, [string]$Destination, [switch]$Mirror) {
    $mode = if ($Mirror) { '/MIR' } else { '/E' }
    & robocopy.exe $Source $Destination $mode /COPY:DAT /DCOPY:DAT /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'File backup or recovery failed.' }
}

function Get-UpgradeConnection([string]$Root, $Pool) {
    $variables = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables('Machine').GetEnumerator()) { $variables[$entry.Key] = $entry.Value }
    foreach ($entry in $Pool.environmentVariables.Collection) { $variables[$entry.name] = $entry.value }
    [xml]$config = Get-Content -LiteralPath (Join-Path $Root 'web.config') -Raw
    foreach ($entry in $config.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) { $variables[$entry.name] = $entry.value }
    $dataHome = $variables['NOVAFLEET_HOME']
    if ([string]::IsNullOrWhiteSpace($dataHome) -or -not [IO.Path]::IsPathRooted($dataHome)) {
        throw 'An explicit NOVAFLEET_HOME is required for a safe upgrade.'
    }
    $connection = $variables['NOVAFLEET_ConnectionStrings__Default']
    $bootstrapPath = Join-Path $dataHome '.novafleet\bootstrap.json'
    if ([string]::IsNullOrWhiteSpace($connection) -and (Test-Path -LiteralPath $bootstrapPath)) {
        $connection = (Get-Content -LiteralPath $bootstrapPath -Raw | ConvertFrom-Json).connectionString
    }
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $environment = if ($variables['DOTNET_ENVIRONMENT']) { $variables['DOTNET_ENVIRONMENT'] } elseif ($variables['ASPNETCORE_ENVIRONMENT']) { $variables['ASPNETCORE_ENVIRONMENT'] } else { 'Production' }
        foreach ($name in @('appsettings.json', "appsettings.$environment.json")) {
            $path = Join-Path $Root $name
            if (Test-Path -LiteralPath $path) {
                $value = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).ConnectionStrings.Default
                if ($null -ne $value) { $connection = $value }
            }
        }
        foreach ($key in @('ConnectionStrings__Default', 'NOVAFLEET_ConnectionStrings__Default')) {
            if (-not [string]::IsNullOrWhiteSpace($variables[$key])) { $connection = $variables[$key] }
        }
    }
    if ([string]::IsNullOrWhiteSpace($connection) -and $variables['SQL_HOST'] -and $variables['DB_NAME'] -and $variables['APP_LOGIN'] -and $variables['APP_PASSWORD']) {
        $legacy = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        $legacy['Data Source'] = $variables['SQL_HOST']; $legacy['Initial Catalog'] = $variables['DB_NAME']
        $legacy['User ID'] = $variables['APP_LOGIN']; $legacy['Password'] = $variables['APP_PASSWORD']; $legacy['TrustServerCertificate'] = $true
        $connection = $legacy.ConnectionString
    }
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'No existing database connection was found. Upgrade was not started.' }
    $parsed = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connection
    if ([string]::IsNullOrWhiteSpace($parsed.InitialCatalog) -or $parsed.InitialCatalog -in @('master', 'model', 'msdb', 'tempdb')) {
        throw 'The database is not an application database.'
    }
    return @{ Connection = $parsed.ConnectionString; Home = $dataHome; Database = $parsed.InitialCatalog }
}

function Invoke-UpgradeSql([string]$ConnectionString, [string]$Sql, [hashtable]$Parameters = @{}) {
    $parsed = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    $parsed['Initial Catalog'] = 'master'
    $connection = New-Object System.Data.SqlClient.SqlConnection $parsed.ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 600
        foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
        $table = New-Object System.Data.DataTable
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        try { [void]$adapter.Fill($table) } finally { $adapter.Dispose(); $command.Dispose() }
        return ,$table
    } catch {
        throw 'SQL backup/recovery failed. Check backup permissions, server disk space and the SQL Server log; connection details are not logged.'
    } finally { $connection.Dispose() }
}

function Backup-UpgradeDatabase([string]$Connection, $State) {
    $database = '[' + $State.Database.Replace(']', ']]') + ']'
    [void](Invoke-UpgradeSql $Connection "BACKUP DATABASE $database TO DISK = @path WITH COPY_ONLY, CHECKSUM, NAME = @name; RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;" @{ '@path' = $State.SqlBackup; '@name' = $State.BackupName })
}

function Restore-UpgradeDatabase([string]$Connection, $State) {
    $header = Invoke-UpgradeSql $Connection 'RESTORE HEADERONLY FROM DISK = @path' @{ '@path' = $State.SqlBackup }
    if ($header.Rows.Count -ne 1 -or $header.Rows[0].DatabaseName -ne $State.Database -or $header.Rows[0].BackupName -ne $State.BackupName) { throw 'The recovery backup identity is invalid.' }
    [void](Invoke-UpgradeSql $Connection 'RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM' @{ '@path' = $State.SqlBackup })
    $database = '[' + $State.Database.Replace(']', ']]') + ']'
    [void](Invoke-UpgradeSql $Connection "ALTER DATABASE $database SET SINGLE_USER WITH ROLLBACK IMMEDIATE; BEGIN TRY RESTORE DATABASE $database FROM DISK = @path WITH REPLACE, CHECKSUM; ALTER DATABASE $database SET MULTI_USER; END TRY BEGIN CATCH ALTER DATABASE $database SET MULTI_USER; THROW; END CATCH;" @{ '@path' = $State.SqlBackup })
}

function Protect-UpgradeDirectory([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict backup directory permissions.' }
}

function Assert-UpgradeRegularFile([string]$Path, [string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'An upgrade recovery path escapes its trusted root.' }
    $parent = Split-Path $fullPath -Parent
    while ($parent) {
        if (Test-Path -LiteralPath $parent) {
            if ((Get-Item -LiteralPath $parent -Force).Attributes -match 'ReparsePoint') { throw 'A reparse point prevents safe upgrade recovery.' }
        }
        if ($parent.TrimEnd('\') -eq $rootPath) { break }
        $parent = Split-Path $parent -Parent
    }
    if (Test-Path -LiteralPath $fullPath) {
        $item = Get-Item -LiteralPath $fullPath -Force
        if ($item.PSIsContainer -or $item.Attributes -match 'ReparsePoint') { throw 'An upgrade recovery target is not a regular file.' }
    }
}

function Assert-UpgradePayload([string]$ManifestPath, [string]$Root) {
    $manifest = Read-InstallerPayloadManifest $ManifestPath $Root
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    foreach ($entry in $manifest.files) {
        $path = Join-Path $rootPath $entry.path.Replace('/', '\')
        Assert-UpgradeRegularFile $path $rootPath
        if (-not (Test-Path -LiteralPath $path)) { throw "An installer-owned payload file is missing: $($entry.path)" }
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Length -ne $entry.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "An installer-owned payload file was modified: $($entry.path)"
        }
    }
}

function Restore-UpgradeFiles($State) {
    $root = [IO.Path]::GetFullPath($State.AppDir).TrimEnd('\')
    $backup = [IO.Path]::GetFullPath($State.BackupDir).TrimEnd('\')
    $oldFiles = Join-Path $backup 'files'
    if ((Get-Item -LiteralPath $root -Force).Attributes -match 'ReparsePoint' -or (Get-Item -LiteralPath $oldFiles -Force).Attributes -match 'ReparsePoint') { throw 'A reparse point prevents safe upgrade recovery.' }
    $oldManifest = Read-InstallerPayloadManifest $State.OldManifest $root
    $newManifest = Read-InstallerPayloadManifest $State.NewManifest $root
    $oldPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $oldManifest.files) { [void]$oldPaths.Add($entry.path) }
    $restores = @()
    foreach ($entry in $oldManifest.files) {
        $source = Join-Path $oldFiles $entry.path.Replace('/', '\')
        $destination = Join-Path $root $entry.path.Replace('/', '\')
        Assert-UpgradeRegularFile $source $oldFiles
        Assert-UpgradeRegularFile $destination $root
        $item = Get-Item -LiteralPath $source -Force
        if ($item.Length -ne $entry.length -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'An installer backup file failed its recovery hash check.' }
        $restores += [pscustomobject]@{ Source = $source; Destination = $destination }
    }
    $removals = @()
    foreach ($entry in $newManifest.files) {
        if ($oldPaths.Contains($entry.path)) { continue }
        $destination = Join-Path $root $entry.path.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $destination)) { continue }
        Assert-UpgradeRegularFile $destination $root
        $item = Get-Item -LiteralPath $destination -Force
        if ($item.Length -ne $entry.length -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'A new payload file was modified and cannot be removed safely during recovery.' }
        $removals += $destination
    }
    $configRestores = @()
    foreach ($relative in @('web.config', 'NovaFleet.payload.json')) {
        $source = if ($relative -eq 'NovaFleet.payload.json') { $State.OldManifest } else { Join-Path $oldFiles $relative }
        $destination = Join-Path $root $relative
        if (Test-Path -LiteralPath $source) {
            Assert-UpgradeRegularFile $source $(if ($relative -eq 'NovaFleet.payload.json') { $backup } else { $oldFiles })
            Assert-UpgradeRegularFile $destination $root
            $configRestores += [pscustomobject]@{ Source = $source; Destination = $destination }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $oldFiles -Filter 'appsettings*.json' -File) {
        $destination = Join-Path $root $file.Name
        Assert-UpgradeRegularFile $file.FullName $oldFiles
        Assert-UpgradeRegularFile $destination $root
        $configRestores += [pscustomobject]@{ Source = $file.FullName; Destination = $destination }
    }
    foreach ($entry in $restores) {
        $directory = Split-Path $entry.Destination -Parent
        if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    }
    foreach ($entry in $configRestores) { Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force }
    foreach ($path in $removals) { Remove-Item -LiteralPath $path -Force }
}

function Stop-UpgradeSite($State) {
    Set-Content -LiteralPath (Join-Path $State.AppDir 'app_offline.htm') -Value '<html><body>NovaFleet maintenance in progress.</body></html>' -Encoding UTF8
    if ((Get-Website -Name $State.SiteName).State -ne 'Stopped') { Stop-Website -Name $State.SiteName }
    if ((Get-WebAppPoolState -Name $State.PoolName).Value -ne 'Stopped') { Stop-WebAppPool -Name $State.PoolName }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $workers = @(& "$env:windir\System32\inetsrv\appcmd.exe" list wp "/apppool.name:$($State.PoolName)" /text:pid)
        if ($LASTEXITCODE -ne 0) { throw 'Could not check the stopped worker process.' }
        if ((Get-WebAppPoolState -Name $State.PoolName).Value -eq 'Stopped' -and -not ($workers | Where-Object { $_ -match '\d' })) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Application pool did not stop; files have not been replaced.'
}

function Start-UpgradeSite($State) {
    Remove-Item -LiteralPath (Join-Path $State.AppDir 'app_offline.htm') -Force -ErrorAction SilentlyContinue
    if ((Get-WebAppPoolState -Name $State.PoolName).Value -ne 'Started') { Start-WebAppPool -Name $State.PoolName }
    if ((Get-Website -Name $State.SiteName).State -ne 'Started') { Start-Website -Name $State.SiteName }
}

function Convert-UpgradeVersion([string]$Value) {
    $version = [version]$Value
    return [version]('{0}.{1}.{2}.{3}' -f $version.Major, $version.Minor, [Math]::Max(0, $version.Build), [Math]::Max(0, $version.Revision))
}

function Wait-UpgradeHealth($State, [bool]$Verification) {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        try {
            $response = Invoke-WebRequest -Uri $State.HealthUrl -Headers @{ Host = $State.HealthHost } -UseBasicParsing -TimeoutSec 10 -MaximumRedirection 0
            $health = $response.Content | ConvertFrom-Json
            if ($response.StatusCode -eq 200 -and $health.status -eq 'ok' -and $health.installerVerification -eq $Verification -and $health.databaseConfigured -eq $true -and ((Convert-UpgradeVersion $health.version) -eq (Convert-UpgradeVersion $State.TargetVersion))) { return }
        } catch { }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The expected application version did not pass its health check.'
}

function Set-UpgradeVerification([string]$Root) { Set-InstallerVerification $Root $true }

function Get-UpgradeHttpsBinding($Site, [int]$Port, [string]$HostName) {
    foreach ($binding in @($Site.bindings.Collection)) {
        if ($binding.protocol -ne 'https' -or $binding.bindingInformation -notmatch '^([^:]*):(\d+):(.*)$') { continue }
        if ([int]$Matches[2] -eq $Port -and (-not $Matches[3] -or $Matches[3].Equals($HostName, [StringComparison]::OrdinalIgnoreCase))) { return $binding }
    }
    return $null
}

function Add-UpgradeHttpsBinding([string]$Name, [string]$HostName, [int]$Port, [string]$Thumbprint) {
    if (Test-InstallerSslBinding $Port) { throw "HTTPS port $Port already has an SSL certificate binding." }
    Add-IisHttpsBinding $Name $HostName $Port
    Add-InstallerSslBinding $Port $Thumbprint
}

function Remove-UpgradeHttpsBinding([string]$Name, [string]$HostName, [int]$Port) {
    $site = Get-Website -Name $Name
    $binding = Get-UpgradeHttpsBinding $site $Port $HostName
    if ($null -ne $binding) { Remove-WebBinding -Name $Name -BindingInformation $binding.bindingInformation -Protocol https }
}

function Prepare-Upgrade([string]$Name, [string]$Root, [string]$Version, [string]$Journal, [string]$CertificateThumbprint, [string]$CertificateHostName, [string]$NewManifestPath, [int]$HttpsPort) {
    if (Test-Path -LiteralPath $Journal) { throw 'An unfinished upgrade exists. Recover it before starting another upgrade.' }
    if ($HttpsPort -lt 1 -or $HttpsPort -gt 65535) { throw 'A valid HTTPS port is required.' }
    $certificate = Assert-InstallerCertificate $CertificateThumbprint $CertificateHostName
    $CertificateHostName = $CertificateHostName.Trim()
    $oldManifest = Join-Path $Root 'NovaFleet.payload.json'
    if (-not (Test-Path -LiteralPath $oldManifest)) { throw 'The installed payload manifest is missing; obsolete-file ownership cannot be proven.' }
    if (-not (Test-Path -LiteralPath $NewManifestPath)) { throw 'The new installer payload manifest is missing.' }
    Assert-UpgradePayload $oldManifest $Root
    [void](Read-InstallerPayloadManifest $NewManifestPath $Root)
    $site = Get-Website -Name $Name
    if ($null -eq $site -or [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($site.physicalPath)).TrimEnd('\') -ne [IO.Path]::GetFullPath($Root).TrimEnd('\')) { throw 'The registered installation path does not match IIS.' }
    $poolName = [string]$site.applicationPool
    if (@(Get-Website | Where-Object { $_.applicationPool -eq $poolName }).Count -ne 1 -or @(Get-WebApplication | Where-Object { $_.applicationPool -eq $poolName }).Count -gt 0) { throw 'The application pool is shared. A dedicated pool is required for upgrade.' }
    if (Test-Path -LiteralPath (Join-Path $Root 'app_offline.htm')) { throw 'The site is already in maintenance mode.' }
    if (@(Get-ChildItem -LiteralPath $Root -Recurse -Force -Attributes ReparsePoint).Count -gt 0) { throw 'Reparse points in the installation are not supported for safe backup.' }
    $pool = Get-Item "IIS:\AppPools\$poolName"
    $settings = Get-UpgradeConnection $Root $pool
    $dataHome = [IO.Path]::GetFullPath($settings.Home).TrimEnd('\')
    if (-not $dataHome.StartsWith([IO.Path]::GetFullPath($Root).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'External NOVAFLEET_HOME requires a separate backup procedure; no files have been changed.' }
    $directory = Join-Path (Split-Path $Journal -Parent) ('backups\' + [Guid]::NewGuid().ToString('N'))
    Protect-UpgradeDirectory (Split-Path $Journal -Parent)
    Protect-UpgradeDirectory $directory
    $state = [pscustomobject]@{
        Stage = 'preparing'; SiteName = $Name; PoolName = $poolName; AppDir = $Root; TargetVersion = $Version; Port = $HttpsPort
        CertificateThumbprint = $certificate.Thumbprint; CertificateHostName = $CertificateHostName
        CreatedHttpsBinding = $false; CreatedSslBinding = $false; OriginalSslThumbprint = ''; SslBindingModified = $false
        BackupDir = $directory; Database = $settings.Database; SqlBackup = ''; BackupName = 'NovaFleet-' + [Guid]::NewGuid().ToString('N')
        OldManifest = (Join-Path $directory 'old-payload.json'); NewManifest = (Join-Path $directory 'new-payload.json')
        HealthUrl = "https://${CertificateHostName}:$HttpsPort/health"; HealthHost = $CertificateHostName
        SiteWasStarted = ($site.State -eq 'Started'); PoolWasStarted = ((Get-WebAppPoolState -Name $poolName).Value -eq 'Started')
    }
    Save-UpgradeState $state $Journal
    try {
        Copy-Item -LiteralPath $script:UpgradeScriptPath -Destination (Join-Path $directory 'Upgrade-Iis.ps1')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer-Common.ps1') -Destination $directory
        Copy-Item -LiteralPath $NewManifestPath -Destination $state.NewManifest
        Stop-UpgradeSite $state
        Copy-UpgradeTree $Root (Join-Path $directory 'files')
        Copy-Item -LiteralPath $oldManifest -Destination $state.OldManifest
        Remove-Item -LiteralPath (Join-Path $directory 'files\app_offline.htm') -Force
        $result = Invoke-UpgradeSql $settings.Connection "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath')) AS Path"
        $backupRoot = [string]$result.Rows[0].Path
        if ([string]::IsNullOrWhiteSpace($backupRoot)) { throw 'SQL Server has no default backup directory.' }
        $separator = if ($backupRoot.Contains('/')) { '/' } else { '\' }
        $state.SqlBackup = $backupRoot.TrimEnd('\', '/') + $separator + $state.BackupName + '.bak'
        Save-UpgradeState $state $Journal
        Backup-UpgradeDatabase $settings.Connection $state
        $state.Stage = 'copying'
        Save-UpgradeState $state $Journal
        Save-UpgradeState $state (Join-Path $directory 'recovery.json')
    } catch {
        Restore-Upgrade $Journal
        throw
    }
}

function Restore-Upgrade([string]$Journal) {
    if (-not (Test-Path -LiteralPath $Journal)) { return }
    $state = Get-Content -LiteralPath $Journal -Raw | ConvertFrom-Json
    if ($state.Stage -eq 'completed') { Remove-Item -LiteralPath $Journal; return }
    Stop-UpgradeSite $state
    if ($state.Stage -eq 'accepted') { throw 'The site may have accepted new writes. Automatic database rollback is blocked. Keep the site stopped and recover with the administrator using the saved backups.' }
    if ($state.Stage -eq 'verifying') {
        $settings = Get-UpgradeConnection (Join-Path $state.BackupDir 'files') (Get-Item "IIS:\AppPools\$($state.PoolName)")
        if ($settings.Database -ne $state.Database) { throw 'The recovery database does not match the backed-up database.' }
        Restore-UpgradeDatabase $settings.Connection $state
    }
    if ($state.Stage -in @('copying', 'binding', 'verifying')) { Restore-UpgradeFiles $state }
    if ($state.CreatedHttpsBinding) { Remove-UpgradeHttpsBinding $state.SiteName $state.CertificateHostName $state.Port }
    if ($state.CreatedSslBinding) { Remove-InstallerSslBinding $state.Port }
    elseif ($state.SslBindingModified) { Set-InstallerSslBinding $state.Port $state.OriginalSslThumbprint $state.CertificateThumbprint }
    Remove-Item -LiteralPath (Join-Path $state.AppDir 'app_offline.htm') -Force -ErrorAction SilentlyContinue
    if ($state.PoolWasStarted -and (Get-WebAppPoolState -Name $state.PoolName).Value -ne 'Started') { Start-WebAppPool -Name $state.PoolName }
    if ($state.SiteWasStarted -and (Get-Website -Name $state.SiteName).State -ne 'Started') { Start-Website -Name $state.SiteName }
    $state.Stage = 'recovered'
    Save-UpgradeState $state (Join-Path $state.BackupDir 'recovery.json')
    Remove-Item -LiteralPath $Journal
}

function Finish-Upgrade([string]$Journal) {
    $state = Get-Content -LiteralPath $Journal -Raw | ConvertFrom-Json
    if ($state.Stage -ne 'copying') { throw 'Upgrade is not ready for verification.' }
    $oldFiles = Join-Path $state.BackupDir 'files'
    Copy-Item -LiteralPath (Join-Path $oldFiles 'web.config') -Destination (Join-Path $state.AppDir 'web.config') -Force
    Set-InstallerProductionEnvironment $state.AppDir $state.Port
    foreach ($file in Get-ChildItem -LiteralPath $oldFiles -Filter 'appsettings*.json' -File) { Copy-Item -LiteralPath $file.FullName -Destination $state.AppDir -Force }
    Set-UpgradeVerification $state.AppDir
    $state.Stage = 'binding'
    Save-UpgradeState $state $Journal
    $site = Get-Website -Name $state.SiteName
    if ($null -eq (Get-UpgradeHttpsBinding $site $state.Port $state.CertificateHostName)) {
        $state.CreatedHttpsBinding = $true; Save-UpgradeState $state $Journal
        if (-not (Test-InstallerSslBinding $state.Port)) { $state.CreatedSslBinding = $true; Save-UpgradeState $state $Journal }
        Add-UpgradeHttpsBinding $state.SiteName $state.CertificateHostName $state.Port $state.CertificateThumbprint
    } else {
        $currentThumbprint = Get-InstallerSslThumbprint $state.Port
        if (-not $currentThumbprint) { throw 'The existing IIS HTTPS binding has no readable SSL certificate.' }
        if ($currentThumbprint -ne $state.CertificateThumbprint) {
            $state.OriginalSslThumbprint = $currentThumbprint; $state.SslBindingModified = $true; Save-UpgradeState $state $Journal
            Set-InstallerSslBinding $state.Port $state.CertificateThumbprint $currentThumbprint
        }
    }
    $state.Stage = 'verifying'
    Save-UpgradeState $state $Journal
    Start-UpgradeSite $state
    Wait-UpgradeHealth $state $true
    [void](Remove-ObsoletePayload $state.AppDir $state.OldManifest $state.NewManifest)
    Stop-UpgradeSite $state
    Copy-Item -LiteralPath (Join-Path $oldFiles 'web.config') -Destination (Join-Path $state.AppDir 'web.config') -Force
    Set-InstallerProductionEnvironment $state.AppDir $state.Port
    $state.Stage = 'accepted'
    Save-UpgradeState $state $Journal
    Start-UpgradeSite $state
    Wait-UpgradeHealth $state $false
    if (-not $state.SiteWasStarted) { Stop-Website -Name $state.SiteName }
    if (-not $state.PoolWasStarted) { Stop-WebAppPool -Name $state.PoolName }
    $state.Stage = 'completed'
    Save-UpgradeState $state (Join-Path $state.BackupDir 'recovery.json')
    Save-UpgradeState $state $Journal
}

if (-not $FunctionsOnly) {
    try {
        if ($Phase -eq 'CheckNew') {
            if ($HttpsPort -lt 1 -or $HttpsPort -gt 65535) { throw 'A valid HTTPS port is required.' }
            [void](Assert-InstallerCertificate $CertificateThumbprint $CertificateHostName)
            if (Test-Path -LiteralPath $AppDir) {
                if (@(Get-ChildItem -LiteralPath $AppDir -Force).Count -gt 0) { throw 'The destination directory is not empty.' }
            }
            if (Test-Path "$env:windir\System32\inetsrv\appcmd.exe") {
                Import-Module WebAdministration
                if ((Test-Path "IIS:\Sites\$SiteName") -or (Test-Path "IIS:\AppPools\$SiteName")) { throw 'The IIS site or pool already exists.' }
            }
            exit 0
        }
        Import-Module WebAdministration
        switch ($Phase) {
            'Prepare' { Prepare-Upgrade $SiteName $AppDir $TargetVersion $StateFile $CertificateThumbprint $CertificateHostName $NewManifestPath $HttpsPort }
            'Finish' { Finish-Upgrade $StateFile }
            'Recover' { Restore-Upgrade $StateFile }
            default { throw 'An upgrade phase is required.' }
        }
        exit 0
    } catch {
        if ($Phase -eq 'Finish' -and (Test-Path -LiteralPath $StateFile)) {
            try { Stop-UpgradeSite (Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json) } catch { }
        }
        Write-Host 'NovaFleet upgrade failed. The site is kept offline if recovery is unsafe. Backups and recovery state are in ProgramData\NovaFleet. No connection strings are logged.'
        exit 1
    }
}
