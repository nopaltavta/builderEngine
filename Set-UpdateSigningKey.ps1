<# Pins the public half of an existing update-signing key into the app source.
 Usage: pwsh -File .\Set-UpdateSigningKey.ps1 -PrivateKeyPath C:\secure\builder-update-private.pem #>
param(
    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath
)
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) { throw "PowerShell 7 or later is required. Run this script with pwsh." }
if (-not (Test-Path -LiteralPath $PrivateKeyPath)) { throw "Signing key not found: $PrivateKeyPath" }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'builder\Settings\UpdateSignature.cs'
$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem([System.IO.File]::ReadAllText($PrivateKeyPath))
    $publicPem = $key.ExportSubjectPublicKeyInfoPem().TrimEnd()
}
finally { $key.Dispose() }

$escapedPem = $publicPem.Replace('"', '""')
$text = [System.IO.File]::ReadAllText($source)
$replacement = 'private const string PublicKeyPem = @"' + $escapedPem + '";'
$updated = [System.Text.RegularExpressions.Regex]::Replace(
    $text,
    'private const string PublicKeyPem = (?:@".*?"|".*?");',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $replacement },
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if ($updated -eq $text) { throw "Couldn't locate PublicKeyPem in $source" }
[System.IO.File]::WriteAllText($source, $updated)
Write-Output "Pinned the public key in $source. Rebuild and publish the app."
