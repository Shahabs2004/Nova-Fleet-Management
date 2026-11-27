[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [Parameter(Mandatory = $true)]
    [string]$ApiDll
)

$ErrorActionPreference = 'Stop'
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
$allowedCatalogs = @(
    'NovaFleet_Login_Test_20261124',
    'NovaFleet_SmsQueue_Test_20261124',
    'NovaFleet_Allocation_Test_20261125',
    'NovaFleet_MissionChangeSnapshot_Test_20261124',
    'NovaFleet_Maintenance_Test_20261116'
)
$allowedCatalog = (
    $allowedCatalogs -contains $builder.InitialCatalog -or
    $builder.InitialCatalog -match '^NovaFleet_Migration_Test_[a-f0-9]{32}$'
)
$allowedDataSource = $builder.DataSource -eq '(localdb)\MSSQLLocalDB' -or $builder.DataSource -eq 'localhost'
if ((-not $allowedCatalog) -or (-not $allowedDataSource) -or (-not $builder.IntegratedSecurity) -or
    (-not [string]::IsNullOrWhiteSpace($builder.UserID)) -or
    (-not [string]::IsNullOrWhiteSpace($builder.Password)) -or
    (-not [string]::IsNullOrWhiteSpace($builder.AttachDBFilename))) {
    throw 'CI database initialization accepts only integrated-security LocalDB/localhost connections to an approved test catalog.'
}

$apiPath = (Resolve-Path -LiteralPath $ApiDll).Path
if (-not (Test-Path -LiteralPath $apiPath -PathType Leaf)) { throw "API DLL not found: $apiPath" }
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('NovaFleet-Ci-Migrate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()

$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'dotnet'
$start.ArgumentList.Add($apiPath)
$start.WorkingDirectory = Split-Path -Parent $apiPath
$start.UseShellExecute = $false
$start.Environment['ConnectionStrings__Default'] = $ConnectionString
$start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
$start.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
$start.Environment['NOVAFLEET_INSTALLER_VERIFY'] = '1'
$start.Environment['NOVAFLEET_HOME'] = $work
$start.Environment['Logging__LogLevel__Microsoft.EntityFrameworkCore'] = 'Warning'
$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    [void]$process.Start()
    $ready = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) { break }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -TimeoutSec 2 -Method Get
            if ($health.databaseConfigured -and $health.installerVerification) {
                $ready = $true
                break
            }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) {
        throw "Database migration did not become healthy for '$($builder.InitialCatalog)'. Exit=$($process.ExitCode). See the API process output above."
    }
    Write-Host "PASS: migrated isolated CI catalog $($builder.InitialCatalog)."
} finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $process.Dispose()
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
