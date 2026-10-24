<#
  Installs gitHub Demo on this PC.

  Right-click > "Run with PowerShell" (in the folder with gitHubDemo-*.msix and gitHubDemo.cer).

  The package is signed with gitHub Demo's own certificate, which Windows doesn't know yet.
  The first time, this script asks for administrator rights to add that certificate to
  "Trusted People" on this computer; after that, installs and updates work like any app.
  Uninstall from Settings > Apps > gitHub Demo.
#>
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$cer = Join-Path $here "gitHubDemo.cer"
$msix = Get-ChildItem $here -Filter "gitHubDemo-*.msix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No gitHubDemo-*.msix next to this script." }

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $trusted) {
    Write-Host "Trusting the gitHub Demo certificate (administrator approval needed)..."
    $command = "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile", "-Command", $command
}

Write-Host "Installing $($msix.Name)..."
Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion
Write-Host "Installed. Start gitHub Demo from the Start menu."
