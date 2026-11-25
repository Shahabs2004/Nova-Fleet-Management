$ErrorActionPreference = 'Stop'

function Save-InstallerState($State, [string]$Path) {
    $directory = Split-Path $Path -Parent
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $temporary = "$Path.tmp"
    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Get-InstallerCertificate([string]$Thumbprint) {
    $normalized = ([Regex]::Replace($Thumbprint, '\s', '')).ToUpperInvariant()
    if (-not $normalized) { return $null }
    return Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $normalized } | Select-Object -First 1
}

function Get-InstallerCertificateNames($Certificate) {
    $names = @($Certificate.DnsNameList | ForEach-Object { [string]$_.Value } | Where-Object { $_ })
    if ($names.Count -eq 0) {
        $san = $Certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } | Select-Object -First 1
        if ($null -ne $san) {
            $names = @([Regex]::Matches($san.Format($false), 'DNS Name=([^,\r\n]+)') | ForEach-Object { $_.Groups[1].Value.Trim() })
        }
    }
    if ($names.Count -eq 0) {
        $name = $Certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false)
        if ($name) { $names = @($name) }
    }
    return $names
}

function Test-InstallerCertificateHostname([string]$HostName, [string[]]$DnsNames) {
    $hostname = $HostName.Trim().TrimEnd('.')
    if (-not $hostname -or [System.Uri]::CheckHostName($hostname) -ne [System.UriHostNameType]::Dns) { return $false }
    foreach ($name in $DnsNames) {
        $dns = ([string]$name).Trim().TrimEnd('.')
        if ($dns.Equals($hostname, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        if ($dns.StartsWith('*.') -and $dns.Substring(2).Contains('.')) {
            $suffix = $dns.Substring(1)
            if ($hostname.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
                $prefix = $hostname.Substring(0, $hostname.Length - $suffix.Length)
                if ($prefix -and -not $prefix.Contains('.')) { return $true }
            }
        }
    }
    return $false
}

function Assert-InstallerCertificate([string]$Thumbprint, [string]$HostName, [DateTime]$Now = [DateTime]::UtcNow) {
    if ([string]::IsNullOrWhiteSpace($Thumbprint)) { throw 'A trusted LocalMachine certificate thumbprint is required for production HTTPS.' }
    if ([System.Uri]::CheckHostName($HostName.Trim()) -ne [System.UriHostNameType]::Dns) { throw 'A DNS certificate hostname is required for production HTTPS.' }
    $certificate = Get-InstallerCertificate $Thumbprint
    if ($null -eq $certificate) { throw 'The certificate thumbprint was not found in LocalMachine\My.' }
    if (-not $certificate.HasPrivateKey) { throw 'The HTTPS certificate does not have an accessible private key.' }
    $utcNow = $Now.ToUniversalTime()
    if ($certificate.NotBefore.ToUniversalTime() -gt $utcNow) { throw 'The HTTPS certificate is not valid yet.' }
    if ($certificate.NotAfter.ToUniversalTime() -le $utcNow) { throw 'The HTTPS certificate is expired.' }
    if (-not (Test-InstallerCertificateHostname $HostName (Get-InstallerCertificateNames $certificate))) { throw 'The HTTPS certificate does not cover the installer hostname.' }
    return $certificate
}

function Add-IisHttpsBinding([string]$Name, [string]$HostName, [int]$HttpsPort) {
    New-WebBinding -Name $Name -Protocol https -Port $HttpsPort -IPAddress '*' -HostHeader $HostName -SslFlags 0 | Out-Null
}

function Test-InstallerSslBinding([int]$HttpsPort) {
    & netsh.exe http show sslcert "ipport=0.0.0.0:$HttpsPort" 2>&1 | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-InstallerSslThumbprint([int]$HttpsPort) {
    $output = & netsh.exe http show sslcert "ipport=0.0.0.0:$HttpsPort" 2>&1
    if ($LASTEXITCODE -ne 0) { return '' }
    $match = [Regex]::Match(($output -join "`n"), '\b[A-Fa-f0-9]{40}\b')
    if (-not $match.Success) { return '' }
    return $match.Value.ToUpperInvariant()
}

function Add-InstallerSslBinding([int]$HttpsPort, [string]$Thumbprint) {
    $appId = '{6F6C2C5A-7D1E-4BFA-9B5D-5BFA7B2E3A21}'
    & netsh.exe http add sslcert "ipport=0.0.0.0:$HttpsPort" "certhash=$Thumbprint" 'certstorename=MY' "appid=$appId" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not register the HTTPS certificate for the IIS port.' }
}

function Set-InstallerSslBinding([int]$HttpsPort, [string]$Thumbprint, [string]$PreviousThumbprint = '') {
    if (Test-InstallerSslBinding $HttpsPort) { Remove-InstallerSslBinding $HttpsPort }
    try { Add-InstallerSslBinding $HttpsPort $Thumbprint }
    catch {
        if ($PreviousThumbprint -and -not (Test-InstallerSslBinding $HttpsPort)) { Add-InstallerSslBinding $HttpsPort $PreviousThumbprint }
        throw
    }
}

function Remove-InstallerSslBinding([int]$HttpsPort) {
    & netsh.exe http delete sslcert "ipport=0.0.0.0:$HttpsPort" | Out-Null
    if ($LASTEXITCODE -ne 0 -and (Test-InstallerSslBinding $HttpsPort)) { throw 'Could not remove the installer-created SSL binding.' }
}

function Test-InstallerProtectedPath([string]$RelativePath) {
    $path = $RelativePath.Replace('/', '\').Trim()
    if ([System.IO.Path]::IsPathRooted($path) -or $path.Contains(':') -or ($path -split '\\' -contains '..')) { return $true }
    $parts = @($path -split '\\' | Where-Object { $_ })
    if ($parts.Count -eq 0) { return $true }
    $first = $parts[0].ToLowerInvariant()
    if ($first -in @('data', '.novafleet', 'logs', 'uploads', 'web.config', 'app_offline.htm', 'novafleet.payload.json')) { return $true }
    if ($first -like 'appsettings*.json') { return $true }
    return @($parts | Where-Object { $_.ToLowerInvariant() -in @('data', '.novafleet', 'logs', 'uploads') }).Count -gt 0
}

function Get-InstallerRelativePath([string]$Root, [string]$FullPath) {
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $path = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $path.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A payload path escapes the installation directory.' }
    return $path.Substring($rootPath.Length + 1).Replace('\', '/')
}

function New-InstallerPayloadManifest([string]$Root, [string]$OutputPath) {
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
    $files = @(Get-ChildItem -LiteralPath $rootPath -Recurse -Force -File | Where-Object {
        $_.FullName -ne $outputFullPath -and $_.Attributes -notmatch 'ReparsePoint' -and -not (Test-InstallerProtectedPath (Get-InstallerRelativePath $rootPath $_.FullName))
    } | ForEach-Object {
        [ordered]@{
            path = Get-InstallerRelativePath $rootPath $_.FullName
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } | Sort-Object { $_.path })
    $manifest = [ordered]@{ format = 'novafleet-installer-manifest-v1'; files = $files }
    $directory = Split-Path $OutputPath -Parent
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [System.IO.File]::WriteAllText($OutputPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
}

function Read-InstallerPayloadManifest([string]$Path, [string]$Root) {
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $data = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($data.format -ne 'novafleet-installer-manifest-v1' -or $null -eq $data.files -or $data.files -isnot [array]) { throw 'The installer payload manifest format is invalid.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = @()
    foreach ($entry in $data.files) {
        $relative = ([string]$entry.path).Replace('\', '/')
        $parts = @($relative -split '/' | Where-Object { $_ })
        if (-not $relative -or [System.IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or @($parts | Where-Object { $_ -eq '..' -or $_ -eq '.' }).Count -gt 0 -or (Test-InstallerProtectedPath $relative)) {
            throw 'The installer payload manifest contains an unsafe or protected path.'
        }
        $invalid = $false
        foreach ($part in $parts) { if ($part.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) { $invalid = $true } }
        if ($invalid -or -not $seen.Add($relative)) { throw 'The installer payload manifest contains an invalid or duplicate path.' }
        if ($entry.length -isnot [long] -and $entry.length -isnot [int] -or [long]$entry.length -lt 0 -or ([string]$entry.sha256) -notmatch '^[a-fA-F0-9]{64}$') { throw 'The installer payload manifest entry is invalid.' }
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $rootPath $relative.Replace('/', '\')))
        if (-not $fullPath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The installer payload manifest escapes the installation directory.' }
        $files += [pscustomobject]@{ path = $relative; length = [long]$entry.length; sha256 = ([string]$entry.sha256).ToUpperInvariant() }
    }
    return [pscustomobject]@{ format = $data.format; files = @($files | Sort-Object { $_.path }) }
}

function Get-ObsoletePayloadFiles([string]$OldManifestPath, [string]$NewManifestPath, [string]$Root) {
    $oldManifest = Read-InstallerPayloadManifest $OldManifestPath $Root
    $newManifest = Read-InstallerPayloadManifest $NewManifestPath $Root
    $current = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $newManifest.files) { [void]$current.Add($file.path) }
    return @($oldManifest.files | Where-Object { -not $current.Contains($_.path) })
}

function Remove-ObsoletePayload([string]$Root, [string]$OldManifestPath, [string]$NewManifestPath) {
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    if ((Get-Item -LiteralPath $rootPath).Attributes -match 'ReparsePoint') { throw 'A reparse point at the installation root prevents safe payload cleanup.' }
    $candidates = @()
    foreach ($entry in (Get-ObsoletePayloadFiles $OldManifestPath $NewManifestPath $rootPath)) {
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $rootPath $entry.path.Replace('/', '\')))
        if (-not $fullPath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'An obsolete payload path escapes the installation directory.' }
        if (-not (Test-Path -LiteralPath $fullPath)) { continue }
        $item = Get-Item -LiteralPath $fullPath -Force
        if ($item.PSIsContainer -or $item.Attributes -match 'ReparsePoint') { throw 'An obsolete payload path is no longer a regular installer-owned file.' }
        $parent = $item.Directory
        while ($null -ne $parent -and $parent.FullName.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
            if ($parent.Attributes -match 'ReparsePoint') { throw 'A reparse point prevents safe payload cleanup.' }
            if ($parent.FullName.TrimEnd('\') -eq $rootPath) { break }
            $parent = $parent.Parent
        }
        if ((Get-Item -LiteralPath $fullPath).Length -ne $entry.length -or (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "An obsolete payload file was modified and will not be deleted: $($entry.path)"
        }
        $candidates += $item
    }
    foreach ($item in $candidates) { Remove-Item -LiteralPath $item.FullName -Force }
    return $candidates.Count
}

function Set-InstallerProductionEnvironment([string]$Root, [int]$HttpsPort = 0) {
    $path = Join-Path $Root 'web.config'
    [xml]$config = Get-Content -LiteralPath $path -Raw
    $ancm = $config.SelectSingleNode('//aspNetCore')
    if ($null -eq $ancm) { throw 'The IIS configuration is not an ASP.NET Core application.' }
    $variables = $ancm.SelectSingleNode('environmentVariables')
    if ($null -eq $variables) { $variables = $config.CreateElement('environmentVariables'); [void]$ancm.AppendChild($variables) }
    $values = @{ ASPNETCORE_ENVIRONMENT = 'Production'; DOTNET_ENVIRONMENT = 'Production' }
    if ($HttpsPort -ge 1 -and $HttpsPort -le 65535) { $values['ASPNETCORE_HTTPS_PORT'] = [string]$HttpsPort }
    foreach ($name in $values.Keys) {
        $flags = @($variables.SelectNodes('environmentVariable[@name="' + $name + '"]'))
        if ($flags.Count -eq 0) {
            $flag = $config.CreateElement('environmentVariable'); $flag.SetAttribute('name', $name); [void]$variables.AppendChild($flag)
            $flags = @($flag)
        }
        foreach ($flag in $flags) { $flag.SetAttribute('value', $values[$name]) }
    }
    $config.Save($path)
}

function Set-InstallerVerification([string]$Root, [bool]$Enabled) {
    $path = Join-Path $Root 'web.config'
    [xml]$config = Get-Content -LiteralPath $path -Raw
    $ancm = $config.SelectSingleNode('//aspNetCore')
    if ($null -eq $ancm) { throw 'The IIS configuration is not an ASP.NET Core application.' }
    $variables = $ancm.SelectSingleNode('environmentVariables')
    if ($null -eq $variables) { $variables = $config.CreateElement('environmentVariables'); [void]$ancm.AppendChild($variables) }
    $flags = @($variables.SelectNodes('environmentVariable[@name="NOVAFLEET_INSTALLER_VERIFY"]'))
    if ($Enabled) {
        if ($flags.Count -eq 0) {
            $flag = $config.CreateElement('environmentVariable'); $flag.SetAttribute('name', 'NOVAFLEET_INSTALLER_VERIFY'); [void]$variables.AppendChild($flag)
            $flags = @($flag)
        }
        foreach ($flag in $flags) { $flag.SetAttribute('value', '1') }
    } else {
        foreach ($flag in $flags) { [void]$variables.RemoveChild($flag) }
    }
    $config.Save($path)
}
