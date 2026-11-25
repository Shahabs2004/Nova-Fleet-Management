param(
    [string]$SiteName,
    [int]$Port,
    [switch]$FunctionsOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')

function Remove-UninstallSslBinding([int]$Port, $Sites) {
    if ($Port -lt 1 -or $Port -gt 65535) { throw 'A valid HTTPS port is required.' }
    if (-not (Test-InstallerSslBinding $Port)) { return }
    foreach ($site in @($Sites)) {
        foreach ($binding in @($site.bindings.Collection)) {
            if ($binding.protocol -eq 'https' -and $binding.bindingInformation -match ':(\d+):[^:]*$' -and [int]$Matches[1] -eq $Port) { return }
        }
    }
    Remove-InstallerSslBinding $Port
}

if (-not $FunctionsOnly) {
    $log = Join-Path $env:TEMP "NovaFleet-Uninstall-$SiteName.log"
    try {
        if ([string]::IsNullOrWhiteSpace($SiteName) -or $Port -lt 1 -or $Port -gt 65535) { throw 'A valid site name and HTTPS port are required.' }
        Import-Module WebAdministration
        if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
        if (Test-Path "IIS:\AppPools\$SiteName") {
            Stop-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
            Remove-WebAppPool -Name $SiteName
        }
        netsh advfirewall firewall delete rule name="NovaFleet-$SiteName-$Port" | Out-Null
        Remove-UninstallSslBinding $Port @(Get-Website)
        "removed site/pool/firewall/unused SSL binding for $SiteName" | Add-Content $log
    } catch {
        ("FATAL: " + $_.Exception.Message) | Add-Content $log
        exit 1
    }
}
