<#
.SYNOPSIS
    Generates the release SBOM and provenance manifest.

.DESCRIPTION
    Produces a CycloneDX SBOM for the solution and a release manifest that records the
    commit, version, SHA-256 of every artefact and the SBOM digest. Run after building the
    installers so the manifest covers what is actually shipped.

.EXAMPLE
    ./scripts/New-ReleaseProvenance.ps1 -Version 1.0.0 -Files dist/PurviewGovernanceCopilot-1.0.0.0.msix
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Files = @('artifacts/publish/PurviewGovernanceCopilot.exe'),
    [string]$Commit = '',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    if (-not (Get-Command dotnet-CycloneDX -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing the CycloneDX tool (pinned)...'
        dotnet tool install --global CycloneDX --version 6.3.2 | Out-Null
    }

    $sbomDirectory = Join-Path $OutputDirectory 'sbom'
    New-Item -ItemType Directory -Force -Path $sbomDirectory | Out-Null

    dotnet CycloneDX PurviewGovernanceCopilot.sln -o $sbomDirectory -F json
    if ($LASTEXITCODE -ne 0) { throw "SBOM generation failed with exit code $LASTEXITCODE." }

    $sbom = Join-Path $sbomDirectory 'bom.json'
    if (-not (Test-Path $sbom)) { throw "Expected SBOM was not written to $sbom." }

    if ([string]::IsNullOrWhiteSpace($Commit)) {
        $Commit = (git rev-parse HEAD 2>$null)
        if ([string]::IsNullOrWhiteSpace($Commit)) { $Commit = 'unknown' }
    }

    $resolved = $Files | ForEach-Object { (Resolve-Path $_).Path }
    $manifest = Join-Path $OutputDirectory 'release-manifest.json'

    dotnet run --project src/PurviewCopilot.Cli -c Release -- provenance `
        --version $Version `
        --commit $Commit `
        --files ($resolved -join ';') `
        --sbom $sbom `
        --output $manifest

    if ($LASTEXITCODE -ne 0) { throw "Provenance generation failed with exit code $LASTEXITCODE." }

    Write-Host "SBOM:     $sbom"
    Write-Host "Manifest: $manifest"
}
finally {
    Pop-Location
}
