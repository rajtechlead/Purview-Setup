# 07 — Release gates

No release may be published until every gate below is **Pass** or carries an owner-approved,
recorded exception. A gate is Pass only with reproducible evidence, never with a documentation
claim or a successful compile alone. Reviewed 27 November 2026.

| Gate | Requirement | Evidence | Status |
| --- | --- | --- | --- |
| G1 | Clean Release build | Domain, Application, Infrastructure build on .NET 10; WPF/MSI require a Windows agent | **Pass** (Windows WPF build not run here) |
| G2 | All tests pass | 170 pass, 6 Windows-only skipped, 0 fail | **Pass** |
| G3 | No unresolved critical or high security issue | `dotnet list package --vulnerable` clean; NuGet advisories fail the build | **Pass** |
| G4 | Every requirement maps to code and reproducible proof | 156 tracked; 58 Tested, 46 Implemented, 19 Simulated, 16 Partial, 16 Unsupported, 1 Blocked | **Open** — Simulated/Partial cannot be counted as done |
| G5 | Entra SSO and multi-client tenant-isolated approvals | No service, no SSO, no shared approvals | **Fail** (`SRV-01`–`SRV-05`, `IDN-01`) |
| G6 | Certificate-first provisioning, 12-month defaults, rotation and consent hand-off | Certificate default exists; default lifetime, overlap rotation and admin-consent hand-off do not | **Fail** (`IDN-04`, `IDN-07`) |
| G7 | Production writes cannot bypass approval, backup, simulation and revalidation | Fail-closed executor plus gate tests; approvals bind to the plan hash, expire after seven days and refuse self-approval | **Pass, simulated only** |
| G8 | Privacy tests prove secrets/PII are blocked before egress and Local-only makes zero cloud calls | `PrivacyTests`, `ProviderSequenceTests` | **Pass** |
| G9 | AI failover, dashboards and advertised exports work | `AuditChainAndHealthTests`, `DashboardAnalyticsTests`, `OfficeReportTests`, `ReportTypeTests` | **Pass** |
| G10 | Current Windows matrix and a self-contained installer that builds, validates, upgrades and uninstalls | Matrix implemented and tested; MSI/MSIX unsigned and framework-dependent; upgrade/uninstall not executed here | **Open** |
| G11 | Documentation covers user, admin, security, API, permissions, operations, troubleshooting, build and install | Guides exist; API specification is consumed-surface only; no accessibility review | **Open** |
| G12 | Signed release with SBOM and provenance | No certificate, no SBOM, no provenance | **Fail** (`SEC-11`, `SEC-17`) |
| G13 | Live verification on a pilot tenant | No tenant, certificate, vault or paid API key available | **Fail** (`R-01`) |

## Gate detail

### Security

- Threat model and OWASP/ASVS/CIS/SSDF mapping: `docs/03`, `docs/10`.
- Secret handling: provider keys from environment, lab client secret DPAPI-protected, no secret in
  logs; audit log hash-chained and verifiable.
- Open: Authenticode signing, SBOM, plugin manifests, accessibility review.

### Installer

- Primary MSI is WiX 5 (`installer/Package.wxs`); MSIX is an alternative package.
- Both currently require the .NET 10 Desktop Runtime and are **unsigned**. Development artifacts
  must be labelled unsigned in any hand-off.
- Upgrade, rollback (`installer/Rollback-PurviewGovernanceCopilot.ps1`) and uninstall must be
  executed on a clean Windows VM and recorded here before release.

### Upgrade and rollback

- Data-preserving upgrade depends on the SQLite schema migration path; a restore of
  `%LOCALAPPDATA%\PurviewGovernanceCopilot` is the fallback.
- Tenant rollback remains **partial**: a reversal script and backup exist; automated restore does
  not (`DEP-03`, `SAF-02`).

### SBOM and vulnerability

- Required: SBOM per release, SHA-256 for every artifact (present), provenance attestation.
- Vulnerability scan must be clean for critical/high at release time.

### Documentation sign-off

| Document | Owner | Status |
| --- | --- | --- |
| User guide | Raj Raja | Exists; needs accessibility and onboarding updates |
| Admin guide | Raj Raja | Exists; updated for .NET 10 and the lifecycle matrix |
| Security architecture | Raj Raja | Exists; needs SSO/multi-user sections when implemented |
| API and permission specification | Raj Raja | Permissions manifest added; no OpenAPI because the app exposes no inbound API |
| Build and install guide | Raj Raja | Exists; clean-Windows execution not evidenced |

## Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Product owner | Raj Raja | Pending | |
| Security lead | — | Pending | |
| QA lead | — | Pending | |
| Release manager | — | Pending | |

**Current decision: do not release.** G5, G6, G12 and G13 fail; G4, G10 and G11 are open.
