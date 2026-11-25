# Runbook 01 — Tenant Readiness Assessment

| Field | Value |
| --- | --- |
| Work item | `PS-010` (supports `PS-003`) |
| Phase | 1 — Assess & Baseline |
| Owner | Purview Lead |
| Risk | Read-only — no tenant changes |
| Duration | 20–30 minutes |
| Evidence | JSON + Markdown + CSV in `assessments/` |

## Purpose

Produce a defensible baseline of the tenant before any Purview configuration: what is licensed, whether audit is on, which role groups exist, and which labels, DLP, retention, eDiscovery and Insider Risk objects already exist. This is the evidence pack for gate **G1** and the input to licensing confirmation (`PS-003`).

## 1. Prerequisites

**Permissions** — one of:

- Compliance Administrator (read + configure later), or
- Global Reader + Security Reader (read-only, sufficient for this runbook).

The Graph licensing read needs `Organization.Read.All` consent. If admin consent is blocked, run with `-SkipGraph` and attach the SKU export from the M365 admin center instead.

**Modules** (per-machine, current user):

```powershell
Install-Module ExchangeOnlineManagement -Scope CurrentUser -Force
Install-Module Microsoft.Graph -Scope CurrentUser -Force   # optional but recommended
```

**Network**: `*.ps.compliance.protection.outlook.com` and `graph.microsoft.com` reachable. No special firewall rule is required for the assessment itself.

## 2. Execute

Clone the repo, then from the repository root:

```powershell
# Standard run: prompts for sign-in, collects Graph licensing too
.\scripts\Get-PurviewReadiness.ps1 -OutputPath .\assessments

# No Graph module / no consent:
.\scripts\Get-PurviewReadiness.ps1 -OutputPath .\assessments -SkipGraph

# Reuse an existing Exchange Online / Security & Compliance session:
.\scripts\Get-PurviewReadiness.ps1 -UseExistingSession
```

First run is blocked by execution policy on some machines:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Get-PurviewReadiness.ps1
```

Exit codes: `0` = no failed checks · `2` = at least one FAIL (review before G1) · `1` = could not connect or write output.

## 3. Interpret the output

Read the Markdown report first; the check table is the summary. Expected states:

| Check | PASS means | If not PASS |
| --- | --- | --- |
| Licensing | At least one E3/E5/A5/G5 SKU detected with Purview coverage | Capture the SKU list, confirm entitlements with procurement, raise R-01 |
| Audit ingestion | `UnifiedAuditLogIngestionEnabled = True` | Turn on audit in the Purview portal (Audit → Start recording user and admin activity); requires Compliance Admin; re-run |
| Audit retention | At least one retention policy exists | Record the gap; `PS-022` will address it |
| Role groups | Expected Purview groups present with named members | Compare to `RACI.md`; gaps become `PS-014`/`PS-023` |
| Existing labels / DLP / retention | Counts and names captured, even if zero | Zero is a valid finding — it means greenfield, not an error |
| Insider Risk / Comms Compliance | Cmdlet available and policies listed | `SKIP` is expected on E3-only tenants; record as unlicensed, not a failure |
| eDiscovery | Cases listed (may be zero) | Failure usually means missing role; re-run as Compliance Administrator |

**Do not** treat "0 policies" as a script failure. The assessment records state; the plan decides what to build.

## 4. Evidence handling

- `assessments/` is git-ignored. Do not force-add it.
- Redact tenant identifiers, UPNs and site URLs before attaching anything to a ticket or the gate pack.
- Retain the raw output in the compliance evidence store with the naming `G1-readiness-<tenant>-<yyyymmdd>`.
- Attach only the Markdown summary to the G1 pack; keep the JSON as the machine-readable record.

## 5. Exit criteria

- [ ] Script completed and exit code recorded.
- [ ] Markdown summary reviewed; every FAIL has an owner and an action.
- [ ] SKU list circulated to the sponsor; `PS-003` unblocked.
- [ ] Baseline counts (labels, DLP, retention, cases) copied into `STATUS.md` "In flight" notes.
- [ ] Evidence stored and referenced in `TEST_MATRIX.md` row `T-010`.

## 6. Known issues

| Symptom | Cause | Fix |
| --- | --- | --- |
| `Connect-IPPSSession` not recognized | Module not imported / not installed | `Install-Module ExchangeOnlineManagement`, restart the shell |
| `Get-MgSubscribedSku` unauthorized | Graph consent missing | Run `-SkipGraph`, use the admin-center SKU export |
| `Get-InsiderRiskPolicy` not recognized | Tenant not licensed for IRM, or older module | Expected `SKIP`; note licensing gap |
| Assessment JSON empty | `-OutputPath` not writable | Use a local path, e.g. `$env:TEMP\purview` |
| `Access denied` on DLP cmdlets | Role lacks Security/Compliance read | Re-run as Compliance Administrator |

## 7. Next step

With the baseline in hand, complete `PS-003` (licensing sign-off) and `PS-013` (full policy export), then start `PS-020` (sensitivity taxonomy v1).
