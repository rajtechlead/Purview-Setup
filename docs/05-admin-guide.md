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

## 3. Entra app registration and authentication provisioning

### Automated (recommended)

Settings → **Authentication Provisioning Center** creates the app registration, enterprise application and service principal, certificate (RSA 2048/3072/4096 or ECC P-256/P-384; 6–120 months) or lab-only secret, owners, least-privilege Graph role assignments and admin consent, then validates the credential. The current connection is the administrator bootstrap.

1. Complete the existing connection fields (tenant, client and an admin-capable credential) and **Test connection**.
2. In the provisioning center choose **Dry run** and run it once; every check must pass and no tenant change occurs.
3. Enter an approver and role. The approver must differ from the signed-in operator and must be a change manager, CAB member, governance/compliance/risk officer or business owner. A 10-year certificate needs the acknowledgement checkbox.
4. Clear **Dry run** and **Provision authentication**. The step list shows each Graph action; success saves the new client id/thumbprint into settings and registers the tenant profile.
5. Save the generated **rollback script** before rollout. It removes the service principal, application and local certificate.

Client-secret mode is accepted only when Environment is **Lab**, stores the secret through the DPAPI-protected settings store and should not be used for production. Prefer certificate authentication.

### Manual fallback

1. **Entra admin center → App registrations → New registration**. Name `Purview Governance Copilot`, single tenant, no redirect URI (app-only).
2. **Certificates & secrets → Certificates → Upload certificate**: upload the `.cer` (public key) exported from the app's Settings page.
3. **API permissions → Microsoft Graph → Application permissions**:
   - `Organization.Read.All`
   - `Reports.Read.All` (mailbox and OneDrive usage counts)
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

- **Deployment rollback**: every phase is backed up before execution. Use **Mark rolled back** for the phase, export the reviewed reversal script from the plan, and restore the named backup. Re-run the assessment afterwards; validation compares labels, DLP, retention and audit against the pre-phase snapshot.
- **Upgrade**: MSI uses `MajorUpgrade` (same UpgradeCode, higher version). Install over the existing version; settings and database remain in `%LOCALAPPDATA%`.
- **Rollback**: run `Rollback-PurviewGovernanceCopilot.ps1 -PreviousMsi <path>` from an elevated PowerShell prompt. It finds every installed copy, uninstalls it with `msiexec`, installs the named previous MSI, and verifies the registered version. Add `-WhatIf` to review the steps. Both msiexec runs write verbose logs to `%TEMP%`; exit code 3010 means a restart is required. The database schema is additive and created idempotently; older builds tolerate newer tables because they only query known ones. The script ships beside the application in the install directory.
- **Manual rollback**: if the script is unavailable, uninstall the current version from Apps & features, then install the previous MSI and confirm the displayed version.
- **Back up before upgrade**: copy `%LOCALAPPDATA%\PurviewGovernanceCopilot` (database plus generated scripts).

### MSIX sideload

Build the package with `powershell -File installer/msix/Build-Msix.ps1 -PublishDir artifacts/publish -Version 1.0.0.0`. The output is `dist/PurviewGovernanceCopilot-<version>.msix` plus a SHA-256 file. The package is framework-dependent, so install the .NET 9 Desktop Runtime first.

Install with `Add-AppxPackage .\dist\PurviewGovernanceCopilot-1.0.0.0.msix`. Unsigned packages require developer mode or a trusted signing certificate whose subject matches `CN=Belle-RajaCorp`; supply `-CertificatePath` and set `PGC_MSIX_CERT_PASSWORD` to sign during the build. Remove with `Remove-AppxPackage BelleRajaCorp.PurviewGovernanceCopilot_<version>_x64__*`.

### Module plugins

Put compiled plugin assemblies in `%LOCALAPPDATA%\PurviewGovernanceCopilot\plugins` and restart the app. A plugin is a public class with a parameterless constructor that implements `PurviewCopilot.Application.Modules.IModule`; it must return a descriptor whose `Key` matches its `Key` property and is registered as `ModuleKey.Custom`. Broken assemblies are skipped and listed in the load result rather than stopping the assessment.

**Trust boundary:** loading a plugin executes its code as the signed-in user. Install only assemblies you built or reviewed, keep the folder out of shared profiles, and remove the DLL to disable it.

## 6. Operations runbook

| Cadence | Task |
| --- | --- |
| Daily (during rollout) | Review new findings and drift items; confirm no unexpected DLP/audit regression |
| Weekly | Run assessment; export the HTML report to the evidence store; review AI usage and cost |
| Monthly | Check certificate expiry warning; rotate if ≤30 days; review privileged role changes; tune DLP thresholds |
| Quarterly | Re-validate app registration permissions; review audit log export; test restore of a backup |
| Per change | Plan → simulate → per-phase backup → separation-of-duties approval → apply in window → validate → record evidence |
| Per phase | Confirm the previous phase validated; back up tenant objects; execute only the selected phase; export the validation result |

Evidence handling: reports and backups can contain tenant identifiers. Keep them in the compliance evidence store, not in email or git. The app writes nothing to the repo; `assessments/` is git-ignored for the CLI-style workflow.

### Phased production rollout

1. Run an assessment and build a change plan. Only recommendations with mapped operations can be executed; manual steps remain in the plan.
2. On Deployment choose a phase and **Simulate + backup phase**. The phase is failed if simulation fails.
3. **Back up tenant objects** for a full label/DLP/retention export, in addition to the automatic phase backup.
4. Enter an approver and role and **Approve phase**. Self-approval and tenant-administrator roles are rejected.
5. Review the change-management pack and tick **I acknowledge the what-if report**. Enable **live execution this session** only inside the change window; accept the risk when the phase contains destructive operations. **Execute approved phase (live)** runs the reviewed PowerShell with certificate environment variables. The build ships fail-closed and the opt-in is not persisted.
6. The app re-collects the tenant and validates the phase automatically. A failed phase blocks the next phase; correct it or roll back.
7. Repeat in order: discovery, audit/monitoring, information protection, DLP, records, insider risk/AI. Do not combine phases.

If any gate fails (simulation, approval, backup, rollback package, authentication, connectivity, risk), execution is blocked and the reason is listed. No bypass exists in the UI.

### Customer access roles (MSP mode)

When saving a customer tenant, the **Access** field takes `user=Role` assignments separated by semicolons:

| Role | Can do |
| --- | --- |
| Viewer | See the tenant in the list and its portfolio row; cannot activate or deploy |
| Operator | Activate the tenant, assess, plan and execute approved phases; cannot approve |
| Approver | Everything an Operator can do plus phase approval |
| Administrator | Manage the tenant record, assignments and all operations |

The signed-in user is always retained as Administrator, and only an Administrator can change the tenant. Separation of duties still applies: the approver must be a different person from the operator, even with Administrator access. Profiles created before this feature remain limited to their recorded owner.

### Unattended runs (headless CLI)

`src/PurviewCopilot.Cli` exposes the same pipeline for Task Scheduler or a CI job:

```powershell
# Acceptance smoke test with the built-in sample tenant
dotnet run --project src/PurviewCopilot.Cli -c Release -- assess --demo --format json --out C:\evidence\smoke.json

# Live read-only assessment, certificate from CurrentUser\My
dotnet run --project src/PurviewCopilot.Cli -c Release -- assess `
  --tenant <tenant-id> --client <app-id> --thumbprint <thumbprint> `
  --format pdf --out C:\evidence\purview.pdf --org "Contoso"

# Verify the hash-chained audit log
dotnet run --project src/PurviewCopilot.Cli -c Release -- verify-audit
```

Use `--data-dir` to point at a service account's evidence store. The CLI never applies changes, writes the same assessment rows as the desktop app, and returns exit code 0 on success, 1 on failure and 2 when the audit chain is invalid.

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
