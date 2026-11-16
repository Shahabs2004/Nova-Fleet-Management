param(
    [Parameter(Mandatory)] [string]$SiteName,
    [Parameter(Mandatory)] [int]$Port
)

$ErrorActionPreference = 'Continue'
$log = Join-Path $env:TEMP "NovaFleet-Uninstall-$SiteName.log"
try {
    Import-Module WebAdministration
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if (Test-Path "IIS:\AppPools\$SiteName") {
        Stop-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
        Remove-WebAppPool -Name $SiteName
    }
    netsh advfirewall firewall delete rule name="NovaFleet-$SiteName-$Port" | Out-Null
    "removed site/pool/firewall for $SiteName" | Add-Content $log
} catch {
    ("FATAL: " + $_.Exception.Message) | Add-Content $log
    exit 1
}
