[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The native maintenance SQL Server fixture requires a Windows runner.' }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'sqlcmd is required before configuring SQL Server.' }

$service = Get-Service -Name MSSQLSERVER -ErrorAction SilentlyContinue
if (-not $service) {
    $installer = Join-Path $env:RUNNER_TEMP 'SQLEXPR_x64_ENU.exe'
    if (-not (Test-Path -LiteralPath $installer)) {
        Invoke-WebRequest -Uri 'https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe' -OutFile $installer -UseBasicParsing
    }
    $extract = Join-Path $env:RUNNER_TEMP ('NovaFleet-SqlSetup-' + [Guid]::NewGuid().ToString('N'))
    $setup = Start-Process -FilePath $installer -ArgumentList '/qs', "/x:$extract" -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "SQL Server extraction failed with exit code $($setup.ExitCode)." }
    $arguments = @(
        '/q', '/ACTION=Install', '/INSTANCENAME=MSSQLSERVER', '/FEATURES=SQLEngine',
        '/UPDATEENABLED=0', '/SQLSYSADMINACCOUNTS=BUILTIN\ADMINISTRATORS',
        '/TCPENABLED=1', '/IACCEPTSQLSERVERLICENSETERMS'
    )
    $install = Start-Process -FilePath (Join-Path $extract 'setup.exe') -ArgumentList $arguments -Wait -PassThru
    if ($install.ExitCode -notin @(0, 3010)) { throw "SQL Server installation failed with exit code $($install.ExitCode)." }
}

$instance = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer'
$tcp = "$instance\SuperSocketNetLib\Tcp"
if (-not (Test-Path -LiteralPath $tcp)) { throw 'SQL Server 2022 default-instance TCP settings were not found.' }
Set-ItemProperty -LiteralPath $tcp -Name Enabled -Value 1
Set-ItemProperty -LiteralPath "$tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty -LiteralPath "$tcp\IPAll" -Name TcpPort -Value 1433
Restart-Service -Name MSSQLSERVER -Force

& sqlcmd -S localhost -E -C -b -Q "IF CAST(SERVERPROPERTY('ProductMajorVersion') AS int) <> 16 THROW 50000, 'SQL Server 2022 is required.', 1; SELECT 1;"
if ($LASTEXITCODE -ne 0) { throw 'The real SQL Server fixture is not reachable as localhost with integrated security.' }
Write-Host 'PASS: native SQL Server 2022 is reachable as localhost:1433 with integrated security.'
