# Builds the NovaFleet Setup.exe end to end.
# Prerequisites: .NET SDK (global.json), Node 20+, Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$Version = if ($env:VERSION) { $env:VERSION.TrimStart('v') } else {
    $described = & git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0) { ([string]$described).Trim().TrimStart('v') } else { '' }
}
if (-not $Version) { $Version = '0.0.0-dev' }
# MSBuild inherits environment variables as properties. Normalize release tags so
# the Version property remains a valid NuGet version (MSB4181).
$env:VERSION = $Version

# bundled .NET 10 IIS Hosting Bundle prerequisite; bump both when the runtime patch changes
# https://dotnet.microsoft.com/en-us/download/dotnet/10.0 + release-notes .sha512 sidecar
$HBundleVersion = '10.0.15'
$HBundleName = "dotnet-hosting-$HBundleVersion-win.exe"
$HBundleUrl = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/$HBundleVersion/$HBundleName"
$HBundleSha512 = '48DD82516CAE1296FE8E6E6C0FBEB4A00033951966373BC1E8309ECD1E02F9F344DB701DADB2344568D7AE86EF968439A4708FEF5DF3CF742D207CF585B5BF5E'

Write-Host "== version $Version =="
Write-Host '::group installer verification'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-Upgrade.ps1')
if ($LASTEXITCODE -ne 0) { throw 'installer upgrade tests failed' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-UpgradeSql.ps1')
if ($LASTEXITCODE -ne 0) { throw 'guarded LocalDB installer SQL recovery tests failed' }
Write-Host 'Guarded LocalDB SQL backup/recovery test executed; no shared or production SQL server is used.'
Write-Host '::endgroup'

Write-Host '::group ensure hosting bundle prereq'
$prereqDir = Join-Path $PSScriptRoot 'prereq'
$prereqFile = Join-Path $prereqDir $HBundleName
$have = (Test-Path $prereqFile) -and ((Get-FileHash $prereqFile -Algorithm SHA512).Hash -eq $HBundleSha512)
if (-not $have) {
    New-Item -ItemType Directory -Path $prereqDir -Force | Out-Null
    Remove-Item $prereqFile -ErrorAction SilentlyContinue
    Write-Host "downloading $HBundleName"
    Invoke-WebRequest $HBundleUrl -OutFile "$prereqFile.tmp" -UseBasicParsing
    $actual = (Get-FileHash "$prereqFile.tmp" -Algorithm SHA512).Hash
    if ($actual -ne $HBundleSha512) { Remove-Item "$prereqFile.tmp" -Force; throw "hosting bundle sha512 mismatch: $actual" }
    Move-Item "$prereqFile.tmp" $prereqFile -Force
}
Write-Host '::endgroup'

Write-Host '::group build frontend'
Push-Location web
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed; stop the Vite dev server if it locks node_modules' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' }
    if (-not (Test-Path dist/index.html)) { throw 'web/dist build missing' }
    if (Test-Path ../src/NovaFleet.Api/wwwroot) { Remove-Item ../src/NovaFleet.Api/wwwroot -Recurse -Force }
    Copy-Item dist ../src/NovaFleet.Api/wwwroot -Recurse
} finally {
    Pop-Location
}
Write-Host '::endgroup'

Write-Host '::group restore, build and test'
dotnet restore NovaFleet.sln --nologo
if ($LASTEXITCODE -ne 0) { throw 'restore failed' }
dotnet build NovaFleet.sln -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'solution build failed' }
dotnet test tests/NovaFleet.UnitTests/NovaFleet.UnitTests.csproj -c Release --no-build --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'unit tests failed' }
Write-Host 'Environment-dependent integration SQL tests run only when their explicit isolated-database variables are configured; otherwise they are reported as skipped.'
dotnet test tests/NovaFleet.IntegrationTests/NovaFleet.IntegrationTests.csproj -c Release --no-build --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'integration tests failed' }
Write-Host '::endgroup'

Write-Host '::group dotnet self-contained publish'
$stage = Join-Path $PSScriptRoot 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
dotnet publish src/NovaFleet.Api -c Release -r win-x64 --self-contained true -p:Version=$Version -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
# The installer renders its own web.config from the template.
Remove-Item (Join-Path $stage 'web.config') -ErrorAction SilentlyContinue
$installerPayload = Join-Path $stage 'installer'
New-Item -ItemType Directory -Path $installerPayload | Out-Null
foreach ($file in @('Install-Iis.ps1', 'Installer-Common.ps1', 'Uninstall-Iis.ps1', 'web.config.template')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $installerPayload
}
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$manifest = Join-Path $stage 'NovaFleet.payload.json'
New-InstallerPayloadManifest $stage $manifest
[void](Read-InstallerPayloadManifest $manifest $stage)
Write-Host '::endgroup'

Write-Host '::group inno setup compile'
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Run: winget install JRSoftware.InnoSetup' }
& $iscc "/DAppVersion=$Version" "/DHBundleName=$HBundleName" installer\NovaFleet.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
Write-Host '::endgroup'

Write-Host "OK -> installer/output/NovaFleet-Setup-$Version.exe"
