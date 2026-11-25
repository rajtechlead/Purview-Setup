<#
.SYNOPSIS
    Builds the publish output and the MSI for Purview Governance Copilot.

.DESCRIPTION
    Requires the .NET 9 SDK and the WiX toolset (`dotnet tool install --global wix`).
    The MSIX path is optional and requires a code-signing certificate.

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
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
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

    Get-ChildItem -Path (Join-Path $root 'artifacts') -Filter *.msi | ForEach-Object {
        & $signtool.Source sign /fd SHA256 /f $SigningCertificate `
            /tr http://timestamp.digicert.com /td SHA256 $_.FullName
    }
}

Write-Host 'Build complete. Output in artifacts\.' -ForegroundColor Green
