<# Creates an ECDSA P-256 signing key for Builder updates (PowerShell 7+).
 Usage: pwsh -File .\New-UpdateSigningKey.ps1 -PrivateKeyPath C:\secure\builder-update-private.pem
 Keep the private PEM out of the project, backups, and cloud shares. #>
param(
    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath
)
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) { throw "PowerShell 7 or later is required. Run this script with pwsh." }
if (Test-Path -LiteralPath $PrivateKeyPath) { throw "Refusing to overwrite existing key: $PrivateKeyPath" }
$dir = Split-Path -Parent $PrivateKeyPath
if (-not $dir) { throw "PrivateKeyPath must include a directory" }
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
try {
    [System.IO.File]::WriteAllText($PrivateKeyPath, $key.ExportECPrivateKeyPem())
    Write-Output "Private key written to $PrivateKeyPath"
    Write-Output "Paste this public key into builder/Settings/UpdateSignature.cs (PublicKeyPem):"
    Write-Output $key.ExportSubjectPublicKeyInfoPem()
}
finally { $key.Dispose() }
