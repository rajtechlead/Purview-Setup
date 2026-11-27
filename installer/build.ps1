<#
.SYNOPSIS
    Builds the publish output and the MSI for Purview Governance Copilot.

.DESCRIPTION
    Requires the .NET 10 SDK and WiX 5.0.2 (`dotnet tool install --global wix --version 5.0.2`).
    The install is framework-dependent, so targets need the .NET 10 Desktop Runtime.
    The MSI and SHA-256 are copied to dist/. Build the optional MSIX separately with
    installer/msix/Build-Msix.ps1 (it does not require a signing certificate).

.PARAMETER Configuration
    Build configuration. Default Release.

.PARAMETER SigningCertificate
    Optional path to a PFX. When supplied, the MSI and executable are signed with signtool.

.EXAMPLE
    .\installer\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$SigningCertificate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'

Write-Host 'Publishing application...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\PurviewCopilot.UI') `
    -c $Configuration -r win-x64 --self-contained false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host 'Building MSI...' -ForegroundColor Cyan
dotnet build (Join-Path $PSScriptRoot 'PurviewGovernanceCopilot.wixproj') `
    -c $Configuration -p:PublishDir="$publishDir\"

if ($LASTEXITCODE -ne 0) { throw 'WiX build failed.' }

if ($SigningCertificate) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK.' }

    Write-Host 'Signing binaries...' -ForegroundColor Cyan
    & $signtool.Source sign /fd SHA256 /f $SigningCertificate `
        /tr http://timestamp.digicert.com /td SHA256 `
        (Join-Path $publishDir 'PurviewGovernanceCopilot.exe')

    Get-ChildItem -Path (Join-Path $PSScriptRoot 'bin') -Filter *.msi -Recurse | ForEach-Object {
        & $signtool.Source sign /fd SHA256 /f $SigningCertificate `
            /tr http://timestamp.digicert.com /td SHA256 $_.FullName
    }
}

Write-Host 'Copying MSI and checksum to dist...' -ForegroundColor Cyan
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$msi = Get-ChildItem -Path (Join-Path $PSScriptRoot 'bin') -Filter '*.msi' -Recurse | Select-Object -First 1
if (-not $msi) { throw 'MSI was not produced.' }
$target = Join-Path $dist 'PurviewGovernanceCopilot-1.0.0.msi'
Copy-Item $msi.FullName $target -Force
$hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$target.sha256", "$hash  $(Split-Path $target -Leaf)`n", [Text.Encoding]::ASCII)

Write-Host "Build complete. MSI: $target" -ForegroundColor Green
