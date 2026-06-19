Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$owner = "SlimeQ"
$repo = "keebs"
$apiUrl = "https://api.github.com/repos/$owner/$repo/releases/latest"
$downloadDir = Join-Path ([System.IO.Path]::GetTempPath()) "keebs-install"

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "Finding latest Keebs release..."
$release = Invoke-RestMethod -Uri $apiUrl -Headers @{
    "Accept" = "application/vnd.github+json"
    "User-Agent" = "keebs-installer"
}

$installerAsset = $release.assets |
    Where-Object { $_.name -like "*.msi" -and $_.name -like "*win-x64*" } |
    Select-Object -First 1

if (-not $installerAsset) {
    $installerAsset = $release.assets |
        Where-Object { $_.name -like "*.msi" } |
        Select-Object -First 1
}

if (-not $installerAsset) {
    throw "No MSI installer asset found on latest release '$($release.tag_name)'."
}

New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
$installerPath = Join-Path $downloadDir $installerAsset.name

Write-Host "Downloading $($installerAsset.name) from $($release.tag_name)..."
Invoke-WebRequest -Uri $installerAsset.browser_download_url -OutFile $installerPath -Headers @{
    "User-Agent" = "keebs-installer"
}

$digestProperty = $installerAsset.PSObject.Properties["digest"]

if ($digestProperty -and $digestProperty.Value -like "sha256:*") {
    $expectedHash = $digestProperty.Value.Substring("sha256:".Length)
    $actualHash = (Get-FileHash -Algorithm SHA256 -Path $installerPath).Hash.ToLowerInvariant()

    if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
        throw "Downloaded installer hash mismatch."
    }
}

Write-Host "Installing Keebs $($release.tag_name)..."
$arguments = @(
    "/i"
    "`"$installerPath`""
    "/passive"
    "/norestart"
)

Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Verb RunAs -Wait
Write-Host "Keebs install complete."
