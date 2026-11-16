param(
    [Parameter(Mandatory)] [string]$SiteName,
    [Parameter(Mandatory)] [int]$Port,
    [Parameter(Mandatory)] [string]$AppDir,
    [string]$BundlePath = ''
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP "NovaFleet-Install-$SiteName.log"
function Say([string]$m) { ($m + "`r`n") | Add-Content $log; Write-Host $m }

try {
    $appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
    if (-not (Test-Path $appcmd)) { throw 'IIS نصب نیست. Feature «Web-Server» را فعال کنید (Installer خودکار امتحان می‌کند).' }

    # 1) IIS features if missing (ANCM bundle is NOT installed here; it is checked separately)
    $features = @('IIS-WebServerRole','IIS-WebServer','IIS-WebServerManagementTools','IIS-ManagementConsole','IIS-ManagementScriptingTools','IIS-ApplicationDevelopment')
    $installed = dism /online /get-featureinfo /featurename:IIS-WebServerRole 2>$null | Out-String
    if ($installed -notmatch 'State : Enabled') {
        Say 'enabling IIS features'
        foreach ($f in $features) {
            dism /online /enable-feature /featurename:$f /quiet /norestart | Out-Null
        }
    }

    # 2) ASP.NET Core Module V2 must be registered AND new enough for .NET 10 (file version 20.x)
    #    if not, run the Hosting Bundle carried inside Setup.exe silently, then re-check
    $ancmPath = Join-Path ${env:ProgramFiles} 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    function AncmMajor {
        if (Test-Path $ancmPath) { [int]((Get-Item $ancmPath).VersionInfo.FileVersion.Split('.')[0]) } else { 0 }
    }
    $modCheck = (& $appcmd list module /name:AspNetCoreModuleV2 2>&1) -join ' '
    $ancmMajor = AncmMajor
    if (($modCheck -notmatch 'AspNetCoreModuleV2') -or ($ancmMajor -lt 20)) {
        if ($BundlePath -and (Test-Path $BundlePath)) {
            Say ('installing bundled Hosting Bundle: ' + (Split-Path $BundlePath -Leaf))
            $p = Start-Process $BundlePath -ArgumentList '/quiet /norestart' -Wait -PassThru
            if (($p.ExitCode -ne 0) -and ($p.ExitCode -ne 3010)) {
                Say ("bundled Hosting Bundle exited with code $($p.ExitCode)")
            }
            $modCheck = (& $appcmd list module /name:AspNetCoreModuleV2 2>&1) -join ' '
            $ancmMajor = AncmMajor
        }
    }
    if ($modCheck -notmatch 'AspNetCoreModuleV2') {
        throw 'ماژول ASP.NET Core (Hosting Bundle) روی این سرور نصب نیست. آخرین .NET 10 Hosting Bundle (windows x64) را از https://dotnet.microsoft.com/download/dotnet/10.0 نصب و IIS را reset کنید، سپس Installer را دوباره اجرا کنید.'
    }
    if ($ancmMajor -lt 20) {
        throw ('نسخه ماژول ASP.NET Core روی این سرور برای اجرای برنامه .NET 10 قدیمی است (aspnetcorev2 ' +
            $(if ($ancmMajor -gt 0) { "$ancmMajor.x" } else { 'نامعلوم' }) +
            '، حداقل 20.x لازم است). آخرین .NET 10 Hosting Bundle (windows x64) را از https://dotnet.microsoft.com/download/dotnet/10.0 یا با دستور ' +
            '«winget install --id Microsoft.DotNet.HostingBundle.10» نصب کنید؛ نصب، IIS را خودکار reset می‌کند.')
    }

    Import-Module WebAdministration
    Say 'WebAdministration loaded'

    # 3) idempotent: drop previous site/pool with the same name (files/DB untouched)
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if (Test-Path "IIS:\AppPools\$SiteName") { Remove-WebAppPool -Name $SiteName }

    # 4) app pool: no managed code, AlwaysRunning
    $pool = New-WebAppPool -Name $SiteName
    $pool.managedRuntimeVersion = ''
    $pool.managedPipelineMode = 'Integrated'
    $pool.startMode = 'AlwaysRunning'
    $pool | Set-Item
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.identityType -Value ApplicationPoolIdentity
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name startMode -Value AlwaysRunning
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name autoStart -Value $true
    # the app is x64; some servers carry a 32-bit applicationPoolDefaults and New-WebAppPool inherits it -> 500.32
    & $appcmd set apppool "$SiteName" /enable32BitAppOnWin64:false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "تنظیم ۶۴-بیتی بودن استخر برنامه ناموفق بود ($LASTEXITCODE)" }

    # 5) site + binding
    New-Website -Name $SiteName -PhysicalPath $AppDir -ApplicationPool $SiteName -Port $Port | Out-Null
    Say "site created on port $Port"

    # 6) data dir + ACL for the app pool identity (keys/bootstrap must be writable)
    $dataDir = Join-Path $AppDir 'data'
    New-Item -ItemType Directory -Path (Join-Path $dataDir '.novafleet') -Force | Out-Null
    & icacls $dataDir /grant ('IIS AppPool\' + $SiteName + ':(OI)(CI)M') /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls روی پوشه data ناموفق بود ($LASTEXITCODE)" }
    Say "data dir ready: $dataDir"

    # 7) web.config from template rendered by the installer package
    $template = Get-ChildItem -Path $AppDir -Recurse -Filter 'web.config.template' | Select-Object -First 1
    if (-not $template) { throw 'قالب web.config در بسته نصب پیدا نشد.' }
    $rendered = (Get-Content $template.FullName -Raw).Replace('{{DATA_DIR}}', $dataDir)
    [System.IO.File]::WriteAllText((Join-Path $AppDir 'web.config'), $rendered, (New-Object System.Text.UTF8Encoding($true)))
    Remove-Item $template.FullName -Force
    Say 'web.config rendered'

    # 8) firewall rule (single rule per site name)
    $ruleName = "NovaFleet-$SiteName-$Port"
    netsh advfirewall firewall delete rule name="NovaFleet-$SiteName-$Port" | Out-Null
    netsh advfirewall firewall delete rule name="NovaFleet-$SiteName" | Out-Null
    netsh advfirewall firewall add rule name=$ruleName dir=in action=allow protocol=TCP localport=$Port | Out-Null
    Say "firewall rule: $ruleName"

    # 9) start + wait for health (appcmd recycle: Recycle-WebAppPool is absent on some servers)
    Start-WebAppPool -Name $SiteName
    Start-Website -Name $SiteName
    & $appcmd recycle apppool /apppool.name:"$SiteName" | Out-Null

    $deadline = (Get-Date).AddSeconds(90)
    $healthy = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 10
            if ($r.StatusCode -eq 200) { $healthy = $true; break }
        } catch { Start-Sleep -Seconds 3 }
    }
    if (-not $healthy) { throw "سایت بالا نیامد؛ پوشه logs و Event Viewer IIS را بررسی کنید." }
    Say 'HEALTH OK'
    exit 0
} catch {
    Say ("FATAL: " + $_.Exception.Message)
    Write-Error $_
    exit 1
}
