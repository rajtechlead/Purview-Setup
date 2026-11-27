# 10 — Security assurance record

This record maps the project's security development lifecycle (Microsoft SDL) and NIST SSDF practices to controls that exist in this repository. It is evidence for SEC-01 and SEC-04, not a claim of independent certification.

## 1. SDL practices

| SDL phase | Practice | Evidence in this repository |
| --- | --- | --- |
| Training | Security and privacy awareness | Security expectations, the bug bar and the disclosure route are in `SECURITY.md`; the privacy guardrail has its own test suite (`PrivacyTests`). |
| Requirements | Security and privacy requirements | `docs/03-security-architecture.md` defines certificate-first authentication, DPAPI at rest, the fail-closed live-execution gate and the AI privacy guard. `docs/project/trackers/01-requirements.csv` tracks each with a status and proof. |
| Design | Threat modelling and attack-surface review | `docs/03-security-architecture.md` §Trust boundaries and `docs/01-solution-architecture.md` record the local store, token provider, Graph/PowerShell collectors, AI providers, plugins and the update channel. |
| Implementation | Approved patterns, no secrets in code | Two runtime NuGet dependencies (`Azure.Identity`, `Microsoft.Data.Sqlite`); Graph over REST; secrets arrive through process environment, DPAPI or Key Vault. `ScrubForLog` and the privacy detector cover log/prompt egress. |
| Verification | Static analysis, dependency and secret checks | `.github/workflows/codeql.yml` runs CodeQL for C#; `.github/workflows/ci.yml` builds, runs 155 tests, scans NuGet packages for known vulnerabilities and publishes without symbols. |
| Release | Signing, packaging and rollback | WiX MSI with `MajorUpgrade`, SHA-256 checksum, `wix msi validate`, and the shipped `Rollback-PurviewGovernanceCopilot.ps1`. Authenticode is wired in CI but no certificate exists here (SEC-11 partial). |
| Response | Vulnerability handling | `SECURITY.md` bug bar with severity, fix targets and disclosure contact. |
| Post-release | Monitoring and drift | Monitoring compares assessments for drift, exports a trend report and can deliver alerts to an operator webhook; the audit log is hash-chained and verified in Settings. |

## 2. NIST SSDF practice mapping

| Practice | Implementation | Evidence |
| --- | --- | --- |
| PO.1 — Security requirements | Requirements tracked with status and proof | `docs/project/trackers/01-requirements.csv` |
| PO.2 — Roles and responsibilities | Separation of duties, approver roles and per-customer RBAC | `SeparationOfDuties`, `TenantAccessPolicy`, approval stages |
| PO.3 — Toolchain | Pinned .NET 10 SDK, deterministic build settings, NuGet audit warnings promoted to errors, CI on `windows-latest` | `global.json`, `Directory.Build.props`, `.github/workflows/ci.yml` |
| PO.5 — Secure environments | Local-first store, DPAPI, no hosted tenant data | `DpapiSecretProtector`, `SqliteStore`, `docs/03` |
| PS.1 — Protect code | Branch protection and review are repository settings; changes are committed with reviewable diffs | Git history |
| PS.2 — Verify third-party components | Only two runtime packages; vulnerability scan in CI | `ci.yml` vulnerability step |
| PS.3 — Archive releases | Versioned MSI, SHA-256 and artifacts | `dist/`, CI artifact upload |
| PW.1 — Threat modelling | Trust boundaries and abuse cases documented | `docs/03-security-architecture.md` |
| PW.2 — Secure design review | Design decisions recorded before implementation | `docs/01`, `docs/02`, roadmap decisions |
| PW.4 — Reuse secure components | Platform crypto (CNG/DPAPI), Azure.Identity, no custom crypto except hashing for the audit chain | `WindowsCertificateService`, `DpapiSecretProtector` |
| PW.5 — Secure coding | Fail-closed gates, read-only discovery, parameterised SQL, HTML escaping in reports | `ChangeExecutor`, collectors, `SqliteStore`, report exporters |
| PW.6 — Build configuration | Release build, no debug symbols in publish, generated files excluded | `ci.yml`, `.gitignore` |
| PW.7 — Review code | Multi-axis review before merge; tests accompany behaviour changes | Test suite, PR history |
| PW.8 — Test code | 155 automated tests including privacy, audit chain, rollback, RBAC and packaging writers | `tests/PurviewCopilot.Tests` |
| PW.9 — Secure defaults | Live execution off, cloud AI optional, privacy review on, least-privilege Graph scopes | Settings defaults, `ChangeExecutor`, `CopilotService` |
| RV.1 — Identify vulnerabilities | CodeQL, dependency scan, disclosure channel | `codeql.yml`, `ci.yml`, `SECURITY.md` |
| RV.2 — Assess and remediate | Bug bar with fix targets; fixes at the cause | `SECURITY.md` |
| RV.3 — Root-cause analysis | Findings recorded in the tracker and changelog with the control that prevents recurrence | `CHANGELOG.md`, tracker evidence |

## 3. Threat model summary

| Asset | Threat | Control |
| --- | --- | --- |
| Tenant evidence and assessments | Theft from the local SQLite file | DPAPI-encrypted payloads at rest; tenant ids and scores remain indexed metadata |
| Certificate private key | Export or misuse | Non-exportable key by default; explicit export requires confirmation; no key in settings |
| Graph / compliance access | Over-privileged app | Certificate-first app-only auth, least-privilege scopes, admin consent recorded, separation of duties |
| Change execution | Accidental or malicious destructive change | Six-phase plan, simulation, per-phase backup, SoD approval, opt-in session-only live execution, post-validation, rollback package |
| AI prompt | PII, secret or customer data leaving the machine | Local deterministic detection, four redaction modes, optional prompt review, cloud AI switch, offline local-only mode, audit without values |
| Update channel | Tampered MSI | HTTPS-only manifest, version and URL validation, mandatory SHA-256 over the downloaded bytes, explicit operator install; signing pending |
| Plugins | Malicious local assembly | A `<name>.plugin.json` manifest must be signed by the declared publisher key, match the DLL's SHA-256, declare a version and request only `assessment:read`; unsigned, tampered or over-permissioned plugins are rejected before any code loads, and each assembly still loads in isolation |
| Reports | Formula or HTML injection | CSV quoting, HTML escaping, formula-prefix neutralisation where data is tabular |

## 4. Release security sign-off

Complete for each release and keep the record with the build.

| Check | Result for this build |
| --- | --- |
| Release build succeeds | Pass — 0 errors |
| Full test suite | Pass — 308 tests (302 pass, 6 Windows-only skipped) |
| Dependency vulnerability scan | Pass — no known vulnerable direct or transitive package at build time |
| CodeQL workflow present and green | Configured (`.github/workflows/codeql.yml`); first run happens on push to GitHub |
| Secrets or private keys in the tree | None found; `.gitignore` excludes local data and environment files |
| Live execution defaults | Fail-closed and opt-in per session |
| Rollback package and script | Generated for every plan; MSI rollback script shipped |
| Privacy guard tests | Pass |
| Software bill of materials | Pass — CycloneDX 1.7 SBOM generated for the solution (106 components) and published with the release manifest |
| Release provenance | Pass — `pgc provenance` records version, commit, SBOM digest and the SHA-256 of every shipped artefact; CI uploads both |
| Signing | Application signing not performed — no certificate available (SEC-11 partial). Plugin assemblies require a signed manifest (SEC-18). |
| Independent penetration test | Not performed — owner action before an enterprise GA claim |

Engineering owner: Belle-RajaCorp. Product owner sign-off is recorded outside this repository when a release is approved.
