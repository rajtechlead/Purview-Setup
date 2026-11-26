<#
.SYNOPSIS
    Builds a sideloadable MSIX package from the published application.

.DESCRIPTION
    Stages the publish output with AppxManifest.xml and the logo assets, downloads the Windows
    SDK BuildTools package when makeappx is not installed, and packs the MSIX. The package is
    unsigned unless -CertificatePath is supplied; an unsigned MSIX can be installed only with
    sideloading/developer mode enabled and a matching trusted certificate.

.PARAMETER PublishDir
    Directory containing PurviewGovernanceCopilot.exe. Defaults to artifacts/publish.

.PARAMETER Version
    Four-part package version. Defaults to 1.0.0.0.

.PARAMETER OutputDir
    Destination directory. Defaults to dist.

.PARAMETER CertificatePath
    Optional PFX used to sign the package. The password is read from PGC_MSIX_CERT_PASSWORD.
#>
[CmdletBinding()]
param(
    [string]$PublishDir = 'artifacts/publish',
    [string]$Version = '1.0.0.0',
    [string]$OutputDir = 'dist',
    [string]$CertificatePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publish = if ([IO.Path]::IsPathRooted($PublishDir)) { $PublishDir } else { Join-Path $root $PublishDir }
$output = if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $root $OutputDir }
$manifest = Join-Path $PSScriptRoot 'AppxManifest.xml'
$assets = Join-Path $PSScriptRoot 'Assets'
$layout = Join-Path ([IO.Path]::GetTempPath()) "pgc-msix-$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path (Join-Path $publish 'PurviewGovernanceCopilot.exe'))) {
    throw "Publish output not found at $publish. Run dotnet publish first."
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must be four numeric parts, for example 1.0.0.0."
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $layout -Force | Out-Null

try {
    Copy-Item (Join-Path $publish '*') $layout -Recurse -Force
    Get-ChildItem $layout -Filter '*.pdb' -Recurse | Remove-Item -Force
    Copy-Item $manifest (Join-Path $layout 'AppxManifest.xml') -Force
    Copy-Item $assets (Join-Path $layout 'Assets') -Recurse -Force

    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    (Get-Content $manifestPath -Raw) -replace 'Version="\d+\.\d+\.\d+\.\d+"', "Version=`"$Version`"" |
        Set-Content $manifestPath -Encoding UTF8

    $makeappx = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1 -ExpandProperty FullName

    if (-not $makeappx) {
        $cache = Join-Path $env:TEMP 'pgc-sdk-buildtools'
        $nupkg = Join-Path $cache 'sdk.zip'
        $sdkVersion = '10.0.26100.6584'
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        if (-not (Test-Path $nupkg)) {
            Write-Host "Downloading Microsoft.Windows.SDK.BuildTools $sdkVersion..."
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$sdkVersion/microsoft.windows.sdk.buildtools.$sdkVersion.nupkg" -OutFile $nupkg
            Expand-Archive $nupkg -DestinationPath (Join-Path $cache 'sdk') -Force
        }

        $makeappx = Get-ChildItem (Join-Path $cache 'sdk') -Recurse -Filter makeappx.exe |
            Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $makeappx) { throw 'makeappx.exe could not be located.' }

    $msix = Join-Path $output "PurviewGovernanceCopilot-$Version.msix"
    Write-Host "Packing $msix..."
    & $makeappx pack /o /d $layout /p $msix
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }

    if ($CertificatePath) {
        if (-not $env:PGC_MSIX_CERT_PASSWORD) { throw 'PGC_MSIX_CERT_PASSWORD is required when signing.' }
        $signtool = Get-ChildItem (Split-Path $makeappx) -Filter signtool.exe | Select-Object -First 1 -ExpandProperty FullName
        & $signtool sign /fd SHA256 /f $CertificatePath /p $env:PGC_MSIX_CERT_PASSWORD $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
    }
    else {
        Write-Warning 'MSIX is unsigned. Sideload it only with a trusted certificate or developer mode.'
    }

    $hash = (Get-FileHash $msix -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$msix.sha256", "$hash  $(Split-Path $msix -Leaf)`n", [Text.Encoding]::ASCII)
    Get-Item $msix | Select-Object FullName, Length
}
finally {
    Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
}
