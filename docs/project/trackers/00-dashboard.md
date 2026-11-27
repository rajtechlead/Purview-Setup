# 00 — Project dashboard

The single status source of truth for the Purview Governance Copilot. Every other document links
here; no requirement count or status is duplicated elsewhere. Last verified **27 November 2026**.

| Field | Value |
| --- | --- |
| Version | 1.0.0 |
| Production target | .NET 10 LTS (supported to 2028-11-14) |
| Branch state | `main` |
| Requirements tracked | 157 (135 migrated, 22 added for the current master prompt) |
| Automated tests | 275 total: 269 pass, 6 Windows-only DPAPI tests skipped on non-Windows hosts |
| Release decision | **Not releasable.** Gates G5, G6, G12 and G13 in [07](07-release-gates.md) fail; G4, G10 and G11 are open. |

## Baseline audit — 27 November 2026

### What genuinely works

- **Solution shape.** Six projects (Domain, Application, Infrastructure, UI, Cli, Tests) with a
  clean dependency direction, a manual composition root and no duplicate abstractions. Release
  builds of Domain, Application and Infrastructure succeed on .NET 10.
- **Assessment engine.** 16 modules, 50 evidence-based controls with unique ids, deterministic
  seven-score output, explainable findings with remediation and framework mappings. Enforced by
  `ScoringAndModuleTests`.
- **Change safety.** Plan building, ten-section change impact, three-stage approval with history,
  DPAPI-encrypted backups, simulation, six-phase rollout, fail-closed live executor and
  post-deployment validation. All covered by tests; no live tenant was available.
- **AI privacy guard.** Layered detector (PII, secrets, tenant and customer identifiers), four
  treatment modes, mandatory pre-egress gate, prompt review and a hash-chained audit log.
- **Reporting.** PDF, DOCX, XLSX, HTML, JSON, CSV and Markdown writers implemented without an
  external report library; audience profiles and framework coverage.
- **Operations.** Drift detection, alerting with HTTPS webhook, Task Scheduler script generation,
  licence optimisation, MSP tenant profiles with per-customer roles.

### Verified state changes in this cycle

| Change | Evidence |
| --- | --- |
| .NET 9 → .NET 10 LTS on all projects, SDK pinned | `global.json`, Release build, `PLT-05` |
| High-severity SQLite advisory closed (GHSA-2m69-gcr7-jv3q) | `Microsoft.Data.Sqlite` 10.0.15 + `SQLitePCLRaw` 2.1.14; `dotnet list package --vulnerable` clean |
| NuGet audit advisories now fail the build | `Directory.Build.props` (`NU1901`–`NU1904` as errors) |
| Windows lifecycle gate replaces a single build floor | `PlatformSupport` + 14 tests (`PLT-07`) |
| DPAPI tests no longer fail the suite on non-Windows agents | `WindowsFact`, 6 skipped with a stated reason |
| Canonical tracker suite replaces three overlapping trackers | this directory; old 07/08/09 deleted |

### Gaps and duplication found

- **Multi-user collaboration is partially delivered.** The customer-hosted service, shared store,
  seven-role server-side RBAC, tenant isolation, concurrency stamps, durable approvals and shared
  audit are implemented and tested over HTTP ([service doc](../../11-collaboration-service.md)).
  The desktop client is not wired to it yet (`SRV-07`), SQL Server has not been run against a real
  instance, and the schema is migration-created but has no SQLite-to-SQL Server data copy tool.
- **No interactive Entra SSO.** MSAL/WAM sign-in and claim-to-role mapping are missing; the service
  enforces all seven roles from its membership store, but the desktop app still has four local
  roles (`IDN-01`, `IDN-02`).
- **Provisioned permissions do not match collector needs.** The provisioning wizard requests six
  Graph permissions, while role-management, Conditional Access and application-inventory reads
  need more. Captured in [03](03-entra-permissions.csv) and `R-07`.
- **No Data Map / Unified Catalog, Adaptive Protection, Information Barriers, PAM or Data Security
  Investigations** (`DSC-15`, `DSC-16`). DSPM and DSPM for AI evidence is unavailable from a stable
  API used here (`R-06`).
- **Approval hardening.** Plan-hash binding, a 7-day expiry, self-approval refusal, a
  pre-execution drift recheck and a per-stage quorum of distinct approvers are enforced and tested
  (`CHG-03`, `CHG-04`). The service host still uses the default single approver per stage.
- **Release engineering.** CycloneDX SBOM and a hash/commit provenance manifest are generated
  and published by CI (`SEC-17`). Authenticode signing, attested provenance and plugin signing
  remain open (`SEC-11`, `SEC-18`); installers in `dist/` are unsigned and framework-dependent.
- **Documentation drift corrected.** README claimed Windows 10/11 blanket support and .NET 9; the
  permission list did not match the provisioner; three trackers restated status independently.

### Obsolete dependencies and risks

| Item | Finding | Action |
| --- | --- | --- |
| .NET 9 | End of support 2026-11-10 | Retargeted to .NET 10 LTS |
| SQLitePCLRaw 2.1.11 | High-severity advisory GHSA-2m69-gcr7-jv3q | Pinned 2.1.14; advisories now break the build |
| Azure.Identity 1.13.1 | Older but no advisory | Left unchanged; upgrade is a separate change |
| WPF shell | Works, UI-agnostic core | Kept; WinUI 3 remains unsupported (`ARC-02`) |

### Unverified or live-only paths

Tenant discovery, certificate provisioning against Entra, Key Vault, live deployment execution,
object restore, webhook delivery and every AI provider call are exercised only against mocks or
simulations. They are recorded as `Simulated`, never as `Live-verified`.

## Milestones

| ID | Milestone | Status | Evidence |
| --- | --- | --- | --- |
| M0 | Baseline audit and verification | Complete | This page; Release build; 171 tests |
| M1 | .NET 10 LTS, dependency advisory, lifecycle platform gate | Complete | Commit `a879e87`; `PLT-05`, `PLT-07` |
| M2 | Canonical tracker suite and documentation reset | Complete | This directory; `PLT-06` |
| M2.1 | Approval hardening: plan hash, expiry, self-approval block | Complete | `ChangePlanHasher`, `WorkflowTests`; quorum remains open under `CHG-03` |
| M2.2 | Pre-execution drift recheck and stale-approval invalidation | Complete | `DeploymentDriftRecheck`, `TenantSnapshotFingerprint`, `DeploymentDriftRecheckTests` |
| M3 | Self-hosted multi-user service, central store, server RBAC | In progress — service delivered and tested; SQL Server and desktop wiring pending | `SRV-01`–`SRV-05`, `SRV-07`; [service doc](../../11-collaboration-service.md) |
| M4 | Entra SSO (WAM), seven app roles, provisioning hand-off | Not started | `IDN-01`–`IDN-07` |
| M5 | Approval hardening: plan hash, expiry, quorum, drift recheck | Not started | `CHG-03`, `CHG-04` |
| M6 | Data Map / Unified Catalog and missing workload coverage | Not started | `DSC-15`, `DSC-16` |
| M7 | Signed release: Authenticode, SBOM, provenance | Blocked on certificate | `SEC-11`, `SEC-17` |

## Progress

| Status | Count | Share |
| --- | --- | --- |
| Tested | 67 | 43% |
| Implemented (build-verified or documented) | 46 | 29% |
| Simulated (code + tests, no live verification) | 19 | 12% |
| Partial | 16 | 10% |
| Unsupported | 8 | 5% |
| Blocked | 1 | 1% |
| **Total** | **157** | |

"Done" means working behaviour plus reproducible proof. Only **Tested** meets that bar; the
dashboard does not count Implemented, Simulated or Partial as complete.

## Health

| Area | Health | Note |
| --- | --- | --- |
| Build | Green | Domain, Application and Infrastructure build in Release on .NET 10; WPF requires Windows |
| Tests | Green | 269 pass, 6 skipped on non-Windows, 0 fail |
| Dependencies | Green | No vulnerable packages; audit advisories are build errors |
| Security | Amber | Strong local controls; unsigned binaries, no SBOM, no live verification |
| Coverage | Amber | Shared service delivered; SSO, desktop wiring and Data Map are absent |
| Documentation | Amber | Canonical trackers now exist; user-facing gaps are labelled |

## Trackers

| Document | Purpose |
| --- | --- |
| [01 Requirements](01-requirements.csv) | Every requirement with status, verification, code path and evidence |
| [02 Purview capabilities](02-purview-capabilities.csv) | Workload × capability, API/cmdlet, status, role, licence, limits |
| [03 Entra permissions](03-entra-permissions.csv) | Exact permissions, rationale, consent, RBAC and credential lifecycle |
| [04 Test evidence](04-test-evidence.csv) | Test level, expected result, automation and last result |
| [05 Risks and decisions](05-risks-decisions.csv) | Risks, decisions, mitigation, owner and status |
| [06 Platform support](06-platform-support.csv) | Windows release × edition × build × architecture × .NET support |
| [07 Release gates](07-release-gates.md) | Security, signing, installer, upgrade, SBOM and sign-off |

Narrative architecture, design, security and guides remain in `docs/01`–`docs/05` and
`docs/10-security-assurance.md`. [Roadmap](../../06-roadmap.md) links here for status.
