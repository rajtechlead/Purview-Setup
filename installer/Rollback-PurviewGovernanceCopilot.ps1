<#
.SYNOPSIS
    Rolls Purview Governance Copilot back to a named previous MSI.

.DESCRIPTION
    Finds every per-machine installation of Purview Governance Copilot, uninstalls it with
    msiexec, then installs the supplied previous MSI. Settings, the database and generated
    scripts in %LOCALAPPDATA%\PurviewGovernanceCopilot are not touched.

    Run from an elevated PowerShell prompt. Use -WhatIf to see the plan first.

.PARAMETER PreviousMsi
    Full path to the earlier signed MSI to restore.

.PARAMETER LogDirectory
    Directory for verbose msiexec logs. Defaults to %TEMP%.

.PARAMETER KeepCurrent
    Install the previous MSI without uninstalling the current build (only useful when the
    current package has already been removed).

.EXAMPLE
    .\Rollback-PurviewGovernanceCopilot.ps1 -PreviousMsi C:\packages\1.0.0\PurviewGovernanceCopilot.msi -WhatIf

.EXAMPLE
    .\Rollback-PurviewGovernanceCopilot.ps1 -PreviousMsi C:\packages\1.0.0\PurviewGovernanceCopilot.msi
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PreviousMsi,

    [string]$LogDirectory = $env:TEMP,

    [switch]$KeepCurrent
)

$ErrorActionPreference = 'Stop'
$productName = 'Purview Governance Copilot'
$uninstallRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)

if (-not (Test-Path -LiteralPath $LogDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$uninstallLog = Join-Path $LogDirectory "PurviewCopilot-uninstall-$stamp.log"
$installLog = Join-Path $LogDirectory "PurviewCopilot-rollback-$stamp.log"

function Get-InstalledProducts {
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem $root | ForEach-Object {
            $entry = Get-ItemProperty $_.PSPath
            if ($entry.DisplayName -like "$productName*") {
                [pscustomobject]@{
                    ProductCode = $_.PSChildName
                    DisplayName = $entry.DisplayName
                    Version     = $entry.DisplayVersion
                }
            }
        }
    }
}

function Invoke-MsiExec {
    param([string[]]$Arguments, [string]$Action)

    $process = Start-Process msiexec.exe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -notin 0, 3010) {
        throw "$Action failed with msiexec exit code $($process.ExitCode). See $uninstallLog / $installLog."
    }
    if ($process.ExitCode -eq 3010) {
        Write-Warning "$Action succeeded but needs a restart."
    }
}

$installed = @(Get-InstalledProducts)
if ($installed.Count -eq 0 -and -not $KeepCurrent) {
    Write-Warning "No installed $productName package was found; installing the previous MSI anyway."
}

if (-not $KeepCurrent) {
    foreach ($product in $installed) {
        if ($PSCmdlet.ShouldProcess("$($product.DisplayName) $($product.Version)", 'Uninstall')) {
            Write-Host "Uninstalling $($product.DisplayName) $($product.Version) ($($product.ProductCode))..."
            Invoke-MsiExec -Action 'Uninstall' -Arguments @(
                '/x', $product.ProductCode, '/qn', '/norestart', '/l*v', "`"$uninstallLog`""
            )
        }
    }
}

if ($PSCmdlet.ShouldProcess($PreviousMsi, "Install previous $productName package")) {
    Write-Host "Installing $PreviousMsi..."
    Invoke-MsiExec -Action 'Rollback install' -Arguments @(
        '/i', "`"$PreviousMsi`"", '/qn', '/norestart', '/l*v', "`"$installLog`""
    )
}

$after = @(Get-InstalledProducts)
if ($after.Count -eq 0) {
    throw "Rollback finished but no $productName installation is registered. Check $installLog."
}

$after | Format-Table DisplayName, Version, ProductCode -AutoSize
Write-Host "Rollback complete. Logs: $uninstallLog ; $installLog"
