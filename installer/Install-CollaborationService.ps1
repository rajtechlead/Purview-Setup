<#
.SYNOPSIS
    Installs the Purview Governance Copilot collaboration service as a Windows service.

.DESCRIPTION
    Publishes are expected to be produced separately (or pass -Publish). The script copies the
    published output to ProgramData, registers the service, enables restart-on-failure and starts it.
    It never writes secrets: configure the connection string, authority and audience as machine
    environment variables before running it.

    TLS is not configured here. Bind a certificate to the port with netsh, or reverse-proxy the
    service and keep Service__RequireHttps=true.

.PARAMETER PublishDir
    Directory containing PurviewCopilot.Service.dll.

.PARAMETER ServiceName
    Windows service name. Default PurviewGovernanceCopilotCollab.

.PARAMETER Port
    HTTPS port the service listens on. Default 8443.

.PARAMETER WhatIf
    Show the actions without changing the machine.

.EXAMPLE
    .\Install-CollaborationService.ps1 -PublishDir ..\artifacts\service -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$ServiceName = 'PurviewGovernanceCopilotCollab',
    [int]$Port = 8443
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $PublishDir 'PurviewCopilot.Service.dll'))) {
    throw "PurviewCopilot.Service.dll was not found in '$PublishDir'. Publish the service first."
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

$target = Join-Path $env:ProgramData 'PurviewGovernanceCopilot\service'
$exe = Join-Path $target 'PurviewCopilot.Service.exe'
$required = @('AzureAd__Authority', 'AzureAd__Audience', 'ASPNETCORE_URLS')

$missing = @()
foreach ($name in $required) {
    $value = [Environment]::GetEnvironmentVariable($name, 'Machine')
    if (-not $value) { $value = [Environment]::GetEnvironmentVariable($name, 'User') }
    if (-not $value) { $missing += $name }
}

if ($missing) {
    Write-Warning ("Missing machine environment variables: {0}. The service fails closed at startup without them." -f ($missing -join ', '))
}

if ($PSCmdlet.ShouldProcess($target, 'Copy published service')) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $PublishDir '*') -Destination $target -Recurse -Force

    # The service account only needs read/execute; tighten the default inherited ACL.
    icacls $target /inheritance:r /grant:r 'SYSTEM:(OI)(CI)RX' 'Administrators:(OI)(CI)F' | Out-Null
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existing) {
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop existing service')) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe config $ServiceName binPath= "`"$exe`" --urls https://+:$Port" | Out-Null
        Start-Service -Name $ServiceName
    }
}
elseif ($PSCmdlet.ShouldProcess($ServiceName, 'Create and start service')) {
    sc.exe create $ServiceName binPath= "`"$exe`" --urls https://+:$Port" start= auto DisplayName= 'Purview Governance Copilot Collaboration' | Out-Null
    sc.exe description $ServiceName 'Customer-hosted collaboration API for Purview Governance Copilot. Serves HTTPS only.' | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Start-Service -Name $ServiceName
}

Write-Host ''
Write-Host "Service: $ServiceName" -ForegroundColor Cyan
Write-Host "Binary:  $exe" -ForegroundColor Cyan
Write-Host "Health:  https://localhost:$Port/health (bind a certificate to the port first)" -ForegroundColor Cyan
Write-Host 'Set AzureAd__Authority, AzureAd__Audience and ConnectionStrings__Collaboration as machine environment variables before first start.' -ForegroundColor Yellow
