$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Upgrade-Iis.ps1') -FunctionsOnly

$database = 'NovaFleet_Upgrade_Test_' + [Guid]::NewGuid().ToString('N')
$connection = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Database=' + $database
$directory = Join-Path ([IO.Path]::GetTempPath()) $database
$created = $false
New-Item -ItemType Directory -Path $directory | Out-Null
try {
    $parsed = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connection
    if ($parsed.DataSource -ne '(localdb)\MSSQLLocalDB' -or $parsed.InitialCatalog -notmatch '^NovaFleet_Upgrade_Test_[a-f0-9]{32}$') { throw 'Unsafe test database target.' }
    [void](Invoke-UpgradeSql $connection "CREATE DATABASE [$database]")
    $created = $true
    [void](Invoke-UpgradeSql $connection "CREATE TABLE [$database].dbo.UpgradeProbe (Id int PRIMARY KEY, Value nvarchar(30)); INSERT INTO [$database].dbo.UpgradeProbe VALUES (1, N'original');")
    $state = [pscustomobject]@{ Database = $database; SqlBackup = (Join-Path $directory 'original.bak'); BackupName = $database }
    Backup-UpgradeDatabase $connection $state
    $header = Invoke-UpgradeSql $connection 'RESTORE HEADERONLY FROM DISK = @path' @{ '@path' = $state.SqlBackup }
    if (-not $header.Rows[0].IsCopyOnly -or -not $header.Rows[0].HasBackupChecksums) { throw 'Backup is not verified copy-only with checksums.' }
    [void](Invoke-UpgradeSql $connection "ALTER TABLE [$database].dbo.UpgradeProbe ADD AddedByUpgrade int NULL; UPDATE [$database].dbo.UpgradeProbe SET Value = N'changed';")
    $originalName = $state.BackupName
    $state.BackupName = 'wrong-backup'
    $blocked = $false
    try { Restore-UpgradeDatabase $connection $state } catch { $blocked = $true }
    if (-not $blocked) { throw 'Wrong backup identity was accepted.' }
    $state.BackupName = $originalName
    Restore-UpgradeDatabase $connection $state
    $result = Invoke-UpgradeSql $connection "SELECT Value FROM [$database].dbo.UpgradeProbe WHERE Id = 1"
    if ($result.Rows[0].Value -ne 'original') { throw 'Original row was not restored.' }
    $schema = Invoke-UpgradeSql $connection "SELECT COUNT(*) AS Added FROM [$database].sys.columns WHERE name = N'AddedByUpgrade'"
    if ($schema.Rows[0].Added -ne 0) { throw 'Upgraded schema was not rolled back.' }
    $access = Invoke-UpgradeSql $connection 'SELECT user_access_desc AS AccessMode FROM sys.databases WHERE name = @name' @{ '@name' = $database }
    if ($access.Rows[0].AccessMode -ne 'MULTI_USER') { throw 'Recovered database was left single-user.' }
    Write-Host 'PASS: LocalDB copy-only backup/checksum, wrong-backup rejection, schema/data recovery and multi-user restoration.'
} finally {
    if ($created) {
        [System.Data.SqlClient.SqlConnection]::ClearAllPools()
        [void](Invoke-UpgradeSql $connection "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]")
    }
    Remove-Item -LiteralPath $directory -Recurse -Force
}
