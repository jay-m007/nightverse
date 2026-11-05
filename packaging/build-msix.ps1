<#
.SYNOPSIS
  Builds Nightverse and packs it into an .msix for the Microsoft Store (or for local testing).

.EXAMPLE
  # Store build (values from Partner Center > Product identity). Upload the .msix; don't sign it.
  ./packaging/build-msix.ps1 -IdentityName "12345JayMaurya.Nightverse" -Publisher "CN=ABCD1234-..." -PublisherDisplayName "Jay Maurya"

.EXAMPLE
  # Local test install: builds, signs with a throwaway self-signed certificate and installs it.
  ./packaging/build-msix.ps1 -Sideload
#>
param(
  [string]$IdentityName = "Nightverse.Dev",
  [string]$Publisher = "CN=NightverseDev",
  [string]$PublisherDisplayName = "Jay Maurya",
  [string]$Version = "1.0.0.0",
  [ValidateSet("x64", "arm64")][string]$Arch = "x64",
  [switch]$Sideload
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "out"
$stage = Join-Path $out "msix-$Arch"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "==> Publishing app ($Arch, self-contained)" -ForegroundColor Cyan
dotnet publish (Join-Path $root "apps/desktop/Nightverse.Desktop.csproj") -c Release -r "win-$Arch" --self-contained true `
  -p:PublishReadyToRun=true -p:Version=$($Version.Substring(0, $Version.LastIndexOf('.'))) -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Writing manifest + assets" -ForegroundColor Cyan
$manifest = Get-Content (Join-Path $PSScriptRoot "AppxManifest.xml") -Raw
$manifest = $manifest.Replace('$IDENTITY_NAME$', $IdentityName).Replace('$PUBLISHER$', $Publisher).
  Replace('$PUBLISHER_DISPLAY_NAME$', $PublisherDisplayName).Replace('$VERSION$', $Version).Replace('$ARCH$', $Arch)
Set-Content -Path (Join-Path $stage "AppxManifest.xml") -Value $manifest -Encoding UTF8
New-Item -ItemType Directory -Force -Path (Join-Path $stage "Assets") | Out-Null
Copy-Item (Join-Path $PSScriptRoot "Assets\*") (Join-Path $stage "Assets") -Recurse -Force

# Locate the newest Windows SDK tools
$kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$sdk = Get-ChildItem $kits -Directory | Where-Object { $_.Name -match '^10\.' -and (Test-Path "$($_.FullName)\x64\makeappx.exe") } |
  Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw "Windows SDK not found. Install it with: winget install Microsoft.WindowsSDK.10.0.22621" }
$tools = "$($sdk.FullName)\x64"

Write-Host "==> Building resources.pri (scaled logos)" -ForegroundColor Cyan
Push-Location $stage
& "$tools\makepri.exe" createconfig /cf priconfig.xml /dq en-US /o | Out-Null
& "$tools\makepri.exe" new /pr . /cf priconfig.xml /of resources.pri /o | Out-Null
Remove-Item priconfig.xml
Pop-Location

$msix = Join-Path $out "Nightverse_$($Version)_$Arch.msix"
Write-Host "==> Packing $msix" -ForegroundColor Cyan
& "$tools\makeappx.exe" pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($Sideload) {
  Write-Host "==> Signing with a self-signed test certificate" -ForegroundColor Cyan
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq $Publisher | Select-Object -First 1
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName "Nightverse test" `
      -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
  }
  & "$tools\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix
  Write-Host "Trusting the test certificate needs an admin prompt (one time)." -ForegroundColor Yellow
  $cer = Join-Path $out "nightverse-test.cer"
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -Command Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
  Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
  Write-Host "Installed. Find 'Nightverse' in the Start menu." -ForegroundColor Green
} else {
  Write-Host "Done: $msix  (upload this in Partner Center; the Store signs it)" -ForegroundColor Green
}
