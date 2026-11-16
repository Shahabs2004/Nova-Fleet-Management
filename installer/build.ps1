# Builds the NovaFleet Setup.exe end to end.
# Prerequisites: .NET SDK (global.json), Node 20+, Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$Version = if ($env:VERSION) { $env:VERSION.TrimStart('v') } else { (cmd /c "git describe --tags --abbrev=0 2>nul") }
if (-not $Version) { $Version = '0.0.0-dev' }

Write-Host "== version $Version =="

Write-Host '::group build frontend'
Push-Location web
npm ci
npm run build
if (-not (Test-Path dist/index.html)) { throw 'web/dist build missing' }
if (Test-Path ../src/NovaFleet.Api/wwwroot) { Remove-Item ../src/NovaFleet.Api/wwwroot -Recurse -Force }
Copy-Item dist ../src/NovaFleet.Api/wwwroot -Recurse
Pop-Location
Write-Host '::endgroup'

Write-Host '::group dotnet self-contained publish'
dotnet test tests/NovaFleet.UnitTests -c Release --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'unit tests failed' }
dotnet publish src/NovaFleet.Api -c Release -r win-x64 --self-contained true -p:Version=$Version -o installer/stage
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
# the installer renders its own web.config from the template
Remove-Item installer/stage/web.config -ErrorAction SilentlyContinue
Write-Host '::endgroup'

Write-Host '::group inno setup compile'
# Windows PowerShell 5.1 misreads UTF-8 without BOM and breaks the Persian strings
foreach ($ps in Get-ChildItem "$PSScriptRoot\*.ps1","$PSScriptRoot\*.template") {
    $text = [System.IO.File]::ReadAllText($ps.FullName, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($ps.FullName, $text, [System.Text.UTF8Encoding]::new($true))
}
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Run: winget install JRSoftware.InnoSetup' }
& $iscc "/DAppVersion=$Version" installer\NovaFleet.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
Write-Host "::endgroup"

Write-Host "OK -> installer/output/NovaFleet-Setup-$Version.exe"
