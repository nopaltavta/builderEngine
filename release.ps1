<# releases a new version: bumps csproj, publishes, zips, writes the manifest.
  usage:  pwsh -File .\release.ps1 -Version 0.3.0 -SigningKeyPath C:\secure\builder-update-private.pem
  you still upload 2 files to Drive by hand (script pauses and tells you when). #>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$SigningKeyPath
)
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) { throw "PowerShell 7 or later is required. Run this script with pwsh." }

trap {
    Write-Output ""
    Write-Output "FAILED: $_"
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        Read-Host "Press Enter to close" | Out-Null
    }
    break
}

if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "Version must look like 0.3.0 (got '$Version')" }
if (-not (Test-Path -LiteralPath $SigningKeyPath)) { throw "Signing key not found: $SigningKeyPath" }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root 'builder\builder.csproj'

Write-Output "== 1/4 version -> $Version =="
(Get-Content -LiteralPath $csproj -Raw) -replace '<Version>.*?</Version>', "<Version>$Version</Version>" |
    Set-Content -LiteralPath $csproj -NoNewline

Write-Output "== 2/4 publish (takes a bit) =="
& dotnet publish $csproj -p:PublishProfile=Windows-x64 -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Output "== 3/4 zip =="
$zip = Join-Path $root "builder-win-x64-$Version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $root 'builder\bin\publish\win-x64\*') -DestinationPath $zip
Write-Output "wrote $zip"

Write-Output ""
Write-Output "NOW YOU: upload that zip to Google Drive,"
Write-Output "share it as Anyone with the link."
$zipId = (Read-Host "Paste the ZIP file ID here (middle chunk of its link)").Trim()
if ($zipId.Length -lt 10) { throw "that ID looks too short, aborting (nothing uploaded yet, just re-run)" }
$notes = (Read-Host "One-line release notes (Enter to skip)").Trim()
$url = "https://drive.google.com/uc?export=download&id=$zipId"
$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$payload = "$Version`n$url`n$sha256`n$notes"
$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem([System.IO.File]::ReadAllText($SigningKeyPath))
    $signature = [Convert]::ToBase64String($key.SignData(
        [System.Text.Encoding]::UTF8.GetBytes($payload),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))
}
finally { $key.Dispose() }

$manifest = Join-Path $root 'builder-update.json'
@{ version = $Version
   url = $url
   sha256 = $sha256
   signature = $signature
   notes = $notes } | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding UTF8
Write-Output ""
Write-Output "wrote $manifest"
Write-Output "LAST STEP: upload builder-update.json to Drive (Anyone with the link). Friends"
Write-Output "paste ITS direct link into the app once; after that, releases are just this script."
