<#
  Builds and signs the gitHub Demo MSIX package.

    powershell -ExecutionPolicy Bypass -File packaging\build-msix.ps1 [-Version 2.0.0.0]

  Output (packaging\out):
    gitHubDemo-<version>.msix   the package (per-user install, appears in Settings > Apps, uninstalls cleanly)
    gitHubDemo.cer              the public certificate PCs must trust once (see install.ps1)
    install.ps1                 trusts the certificate (asks for admin) and installs the package

  The package is signed with a self-signed certificate kept in packaging\signing.pfx
  (created on first run, never committed). Use the same .pfx for every release, or
  installed copies won't accept the update. A purchased code-signing certificate can
  replace it later: pass -Pfx and -PfxPassword.
#>
param(
    [string]$Version = "",
    [string]$Publisher = "CN=gitHub Demo",
    [string]$Pfx = "$PSScriptRoot\signing.pfx",
    [string]$PfxPassword = "githubdemo-signing"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = "$PSScriptRoot\out"
$stage = "$out\stage"

# Windows SDK tools
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64" -Directory | Sort-Object Name -Descending |
    Where-Object { Test-Path "$($_.FullName)\makeappx.exe" } | Select-Object -First 1
if (-not $sdk) { throw "The Windows 10/11 SDK (makeappx.exe, signtool.exe) isn't installed." }
$makeappx = "$($sdk.FullName)\makeappx.exe"
$signtool = "$($sdk.FullName)\signtool.exe"

if (-not $Version) {
    [xml]$proj = Get-Content "$root\client\client_side.csproj"
    $Version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version + ".0"
}
Write-Host "Building gitHub Demo $Version"

# 1. self-contained publish (no .NET install needed on the PC)
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
& dotnet publish "$root\client\client_side.csproj" -c Release -r win-x64 --self-contained true -o $stage --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2. manifest and logos
Copy-Item -Recurse "$PSScriptRoot\Images" "$stage\Images"
(Get-Content "$PSScriptRoot\AppxManifest.xml" -Raw).Replace("{Version}", $Version).Replace("{Publisher}", $Publisher) |
    Set-Content "$stage\AppxManifest.xml" -Encoding UTF8

# 3. pack
$msix = "$out\gitHubDemo-$Version.msix"
& $makeappx pack /d $stage /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# 4. signing certificate: created once, in memory (nothing is added to Windows' certificate stores here)
if (-not (Test-Path $Pfx)) {
    Write-Host "Creating a self-signed signing certificate ($Pfx)"
    $rsa = [System.Security.Cryptography.RSA]::Create(3072)
    $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest($Publisher, $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $usages = New-Object System.Security.Cryptography.OidCollection
    $usages.Add((New-Object System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.3"))) | Out-Null   # code signing
    $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($usages, $false)))
    $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $true)))
    $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true)))
    $cert = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(10))
    [IO.File]::WriteAllBytes($Pfx, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $PfxPassword))
}
$signing = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $PfxPassword)
if ($signing.Subject -ne $Publisher) { throw "The certificate is for '$($signing.Subject)' but the package says '$Publisher'." }
[IO.File]::WriteAllBytes("$out\gitHubDemo.cer", $signing.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

# 5. sign
& $signtool sign /fd SHA256 /f $Pfx /p $PfxPassword $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
Copy-Item "$PSScriptRoot\install.ps1" "$out\install.ps1" -Force

$size = [math]::Round((Get-Item $msix).Length / 1MB, 1)
Write-Host "Done: $msix ($size MB)"
Write-Host "Give people gitHubDemo-$Version.msix, gitHubDemo.cer and install.ps1 (or put the .msix next to the server as its download)."
