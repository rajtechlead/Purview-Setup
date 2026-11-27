# 07 — Release gates

No release may be published until every gate below is **Pass** or carries an owner-approved,
recorded exception. A gate is Pass only with reproducible evidence, never with a documentation
claim or a successful compile alone. Reviewed 27 November 2026.

| Gate | Requirement | Evidence | Status |
| --- | --- | --- | --- |
| G1 | Clean Release build | Domain, Application, Infrastructure build on .NET 10; WPF/MSI require a Windows agent | **Pass** (Windows WPF build not run here) |
| G2 | All tests pass | 297 pass, 6 Windows-only skipped, 0 fail (251 desktop + 46 service) | **Pass** |
| G3 | No unresolved critical or high security issue | `dotnet list package --vulnerable` clean; NuGet advisories fail the build | **Pass** |
| G4 | Every requirement maps to code and reproducible proof | 157 tracked; 69 Tested, 46 Implemented, 19 Simulated, 15 Partial, 7 Unsupported, 1 Blocked | **Open** — Simulated/Partial cannot be counted as done |
| G5 | Entra SSO and multi-client tenant-isolated approvals | Service, migration-created store, server RBAC, tenant-isolated approvals, audit and a typed client are tested over HTTP; the WPF screens are not bound to the client and Entra SSO is absent | **Open** (`SRV-07`, `IDN-01`) |
| G6 | Certificate-first provisioning, 12-month defaults, rotation and consent hand-off | Certificate-first with a 12-month default, 30-day warning, ordered overlap rotation and a tenant-specific admin-consent hand-off link are tested; the live grant still needs a tenant | **Pass, simulated only** |
| G7 | Production writes cannot bypass approval, backup, simulation and revalidation | Fail-closed executor plus gate tests; approvals bind to the plan hash, expire after seven days, refuse self-approval and repeat stage approvals, require a configurable quorum of distinct approvers, and execution is blocked unless a fresh drift recheck matches the approved fingerprint | **Pass, simulated only** |
| G8 | Privacy tests prove secrets/PII are blocked before egress and Local-only makes zero cloud calls | `PrivacyTests`, `ProviderSequenceTests` | **Pass** |
| G9 | AI failover, dashboards and advertised exports work | `AuditChainAndHealthTests`, `DashboardAnalyticsTests`, `OfficeReportTests`, `ReportTypeTests` | **Pass** |
| G10 | Current Windows matrix and a self-contained installer that builds, validates, upgrades and uninstalls | Matrix implemented and tested; MSI/MSIX unsigned and framework-dependent; upgrade/uninstall not executed here | **Open** |
| G11 | Documentation covers user, admin, security, API, permissions, operations, troubleshooting, build and install | Guides exist; API specification is consumed-surface only; no accessibility review | **Open** |
| G12 | Signed release with SBOM and provenance | CycloneDX SBOM and a hash/commit provenance manifest are generated and published by CI; no code-signing certificate, so nothing is signed and provenance is not attested | **Fail** (`SEC-11` only) |
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

**Current decision: do not release.** G5, G12 and G13 fail; G4, G10 and G11 are open.
