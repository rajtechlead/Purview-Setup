# 05 — Admin Guide: Deployment, Operations and Troubleshooting

## 1. Supported platforms

Windows 10 1809+ / Windows 11 / Windows Server 2019, 2022, 2025. The application refuses to start below build 17763 and states why. No administrator rights are required: certificates go to `CurrentUser\My`, data to `%LOCALAPPDATA%\PurviewGovernanceCopilot`.

Prerequisites:

- .NET 9 Desktop Runtime (x64) for the framework-dependent build; the self-contained publish needs nothing installed.
- Windows PowerShell 5.1 with `ExchangeOnlineManagement` (`Install-Module ExchangeOnlineManagement -Scope AllUsers`) for Purview configuration reads.
- Outbound HTTPS to `graph.microsoft.com`, `*.ps.compliance.protection.outlook.com`, and any AI endpoint you enable.

## 2. Build and publish

```powershell
dotnet restore PurviewGovernanceCopilot.sln
dotnet build PurviewGovernanceCopilot.sln -c Release
dotnet test tests/PurviewCopilot.Tests -c Release
dotnet publish src/PurviewCopilot.UI -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

Output: `artifacts/publish/PurviewGovernanceCopilot.exe` plus the .NET runtime payload.

## 3. Entra app registration (certificate auth)

1. **Entra admin center → App registrations → New registration**. Name `Purview Governance Copilot`, single tenant, no redirect URI (app-only).
2. **Certificates & secrets → Certificates → Upload certificate**: upload the `.cer` (public key) exported from the app's Settings page.
3. **API permissions → Microsoft Graph → Application permissions**:
   - `Organization.Read.All`
   - `User.Read.All`
   - `Group.Read.All`
   - `Application.Read.All`
   - `Domain.Read.All`
   - `RoleManagement.Read.Directory`
   - `Policy.Read.All`
   - `SecurityEvents.Read.All`
   - `DeviceManagementManagedDevices.Read.All`
   - `AuditLog.Read.All`
4. **Add permissions → APIs my organization uses → Office 365 Exchange Online → Application permission → `Exchange.ManageAsApp`**.
5. **Grant admin consent** for the tenant.
6. **Entra → Roles and administrators**: assign the app's service principal the **Compliance Administrator** role (or a custom read-only group). Do **not** use Global Administrator.
7. In the app: enter tenant ID, client ID and certificate thumbprint, then **Test connection**.

Least-privilege note: each permission maps to facts in `docs/02-detailed-design.md` §2. Removing a permission degrades those facts and adds a discovery warning; it does not break the assessment.

### Managed identity

For an Azure VM or managed workstation, choose `ManagedIdentity`, optionally supply the user-assigned client ID, and grant the same Graph app roles to the identity. Exchange `Exchange.ManageAsApp` still requires the service principal to exist and hold the compliance role.

## 4. Install options

### MSI (WiX)

Project: `installer/PurviewGovernanceCopilot.wixproj` + `Package.wxs`.

```powershell
dotnet tool install --global wix
dotnet build installer/PurviewGovernanceCopilot.wixproj -c Release `
  -p:PublishDir=..\artifacts\publish\
msiexec /i artifacts\PurviewGovernanceCopilot.msi /qn
```

Silent install: `/qn`. Per-machine install is possible by changing the `StandardDirectory` scope to `ProgramFiles64Folder` (already used) and running elevated; the default per-user deployment needs no elevation.

### MSIX

`installer/msix/AppxManifest.xml` with the `runFullTrust` capability. Package with MakeAppx and sign with your code-signing certificate:

```powershell
makeappx pack /d artifacts\publish /p artifacts\PurviewGovernanceCopilot.msix
signtool sign /fd SHA256 /f cert.pfx /p <password> artifacts\PurviewGovernanceCopilot.msix
```

### Intune (Win32 app)

1. Package the publish folder with the Intune Content Prep Tool (`IntuneWinAppUtil.exe`).
2. Install command: `PurviewGovernanceCopilot.msi /qn` (or `msiexec /i ... /qn`).
3. Uninstall command: `msiexec /x {ProductCode} /qn`.
4. Detection: MSI product code, or file `%ProgramFiles%\Purview Governance Copilot\PurviewGovernanceCopilot.exe`.
5. Requirement: 64-bit Windows 10 1809+.

### MECM/SCCM

Create an MSI application from the WiX output; use the same detection method. Deploy to a governance workstation collection — not to all users, because the tool holds assessment evidence and AI configuration.

## 5. Upgrade and rollback

- **Upgrade**: MSI uses `MajorUpgrade` (same UpgradeCode, higher version). Install over the existing version; settings and database remain in `%LOCALAPPDATA%`.
- **Rollback**: uninstall the new version and reinstall the previous MSI. The database schema is additive and created idempotently; older builds tolerate newer tables because they only query known ones.
- **Back up before upgrade**: copy `%LOCALAPPDATA%\PurviewGovernanceCopilot` (database plus generated scripts).

## 6. Operations runbook

| Cadence | Task |
| --- | --- |
| Daily (during rollout) | Review new findings and drift items; confirm no unexpected DLP/audit regression |
| Weekly | Run assessment; export the HTML report to the evidence store; review AI usage and cost |
| Monthly | Check certificate expiry warning; rotate if ≤30 days; review privileged role changes; tune DLP thresholds |
| Quarterly | Re-validate app registration permissions; review audit log export; test restore of a backup |
| Per change | Plan → simulate → approve (3 stages) → apply in window → validate → record evidence |

Evidence handling: reports and backups can contain tenant identifiers. Keep them in the compliance evidence store, not in email or git. The app writes nothing to the repo; `assessments/` is git-ignored for the CLI-style workflow.

## 7. Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| `AADSTS700027` / certificate not found | Certificate not in `CurrentUser\My`, or wrong thumbprint | Re-import the PFX; check Settings list; remove spaces from thumbprint |
| `AADSTS700016` | Wrong tenant or client ID | Re-copy from the app registration Overview |
| `Authorization_RequestDenied` on Graph | Missing application permission or consent | Add the permission and grant admin consent |
| Assessment shows Purview facts as zero | Missing `Exchange.ManageAsApp`, no compliance role, or module not installed | Run the generated `Get-PurviewFacts.ps1` manually to read the error; install `ExchangeOnlineManagement` |
| `Get-InsiderRiskPolicy` unrecognised | Tenant not licensed for Insider Risk | Expected; the finding reports the licensing gap |
| Discovery notes about sites/teams/devices | Missing Graph permissions | Add `Sites.Read.All`, `Team.ReadBasic.All`, `DeviceManagementManagedDevices.Read.All` and re-run |
| AI: "No AI providers are enabled" | All providers disabled, or offline mode with no local provider | Enable a provider; start Ollama on `http://localhost:11434` |
| AI: 401/403 from provider | Key missing or invalid | Set the environment variable named in the provider's key reference, restart the app |
| AI: 429/quota | Free tier exhausted | Failover should continue automatically; check the usage panel and provider priority |
| Prompt blocked | Block mode and a secret/tenant identifier detected | Remove the value, or switch to Tokenize for a reviewable sanitized prompt |
| PFX export fails | Certificate has no exportable private key | Recreate the certificate from the app (created exportable) |
| App will not start | OS below build 17763, or .NET runtime missing | Use a supported OS; install the .NET 9 Desktop Runtime or the self-contained build |
| Report looks stale | Assessment not re-run | Reports are generated from the current assessment only |

## 8. Logs and diagnostics

- Audit log: `audit_log` table (assessment, plan, approval, AI decision, export). Query with any SQLite client.
- AI usage: `ai_usage` table (provider, model, tokens, cost, success, error code).
- Generated PowerShell: `%LOCALAPPDATA%\PurviewGovernanceCopilot\scripts\Get-PurviewFacts.ps1`.
- No telemetry is collected or transmitted.

## 9. Uninstall

MSI: `msiexec /x {ProductCode} /qn`. MSIX: `Remove-AppxPackage`. Then optionally delete `%LOCALAPPDATA%\PurviewGovernanceCopilot` and the per-user certificates (`certmgr.msc` → Personal → Certificates → subject `Purview Governance Copilot`). Removing the certificate breaks authentication until a new one is registered.

## 10. Support

Belle-RajaCorp · contact@belle-rajacorp.com
