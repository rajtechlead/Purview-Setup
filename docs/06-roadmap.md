# 06 — Roadmap and remaining work

Status source of truth: [00 — Project dashboard](project/trackers/00-dashboard.md). This page explains sequencing and why anything is still open.

**Current position (27 November 2026):** 157 tracked requirements — 69 tested, 46 implemented, 20 simulated, 14 partial, 7 unsupported, 1 blocked. Build succeeds on .NET 10; 323 tests (317 pass, 6 Windows-only skipped); MSI and unsigned MSIX are produced. No live tenant, signing certificate, Key Vault, webhook or AI key was available, so tenant-dependent paths remain unverified rather than claimed.

## Completed phases

### Phase 1.1 — Safety baseline

Complete. DPAPI payload encryption, hash-chained audit log with Settings verification, provider health probing and cooldown, and DPAPI-protected lab secrets are implemented and tested. Database page encryption (SQLCipher) remains open and is tracked under Phase 2.

### Phase 1.2 — Deployment depth

Complete except automated tenant restore.

- **Live executor** — done (unverified live): session opt-in, fail-closed by default, certificate-only, every phase gated by simulation, backup, separation-of-duties approval, rollback package, authentication, connectivity and risk acceptance.
- **Real operation payloads** — done: twelve controls map to real idempotent cmdlets; unsupported controls are explicit `MANUAL` steps, never guessed syntax.
- **Six-phase rollout and validation** — done: per-phase backup, approval, execution and post-deployment comparison of identity, audit, labels, DLP and retention.
- **Configuration-as-code** — done: plan JSON export/import with forced re-approval, PowerShell generation, and ARM/Bicep/Terraform for the Azure Purview account.
- **Object backups** — done (unverified live): read-only export of labels, label policies, auto-label rules, DLP, retention and compliance tags stored as an encrypted `objects/` backup.
- **Rollback** — **partial (DEP-03, SAF-02)**: a reversal script and stored package exist, but no automated tenant restore executes. This needs a live tenant and a reviewed restore design; it is the top engineering gap in this phase.

### Phase 1.2A — Authentication provisioning

Complete (unverified live). The Authentication Provisioning Center creates the app registration, service principal, certificate or lab secret, owners, least-privilege Graph roles and admin consent, validates the credential, produces a rollback script and saves the resulting settings. Dry run and separation of duties are mandatory. No tenant was available to exercise the Graph calls.

### Phase 1.3 — Reporting, MSP and monitoring

Complete.

- **Reporting** — six audience profiles (Full, Executive, Governance, Security, Compliance, Remediation); HTML, PDF, Markdown and JSON reflect the profile; CSV/XLSX remain raw data tables.
- **Persona dashboards** — Executive, Security, Governance and Compliance tabs with risk heatmap, trend series, maturity trail and portfolio.
- **MSP mode** — per-customer profiles with DPAPI-protected credentials and Viewer/Operator/Approver/Administrator access; portfolio and CSV export.
- **Monitoring** — drift, alerts, HTTPS webhook, in-app schedule and a generated Task Scheduler job for headless `pgc assess`. There is no Windows service or native email/Teams connector; that is a convenience gap, not a missing requirement.
- **Licence optimisation** — exact utilisation plus optional per-SKU pricing for monetary opportunity.
- **Greenfield and mode differentiation** — empty-tenant detection and distinct behaviour for all six modes.
- **Entra and workload discovery** — role/PIM/CA/service-principal signals plus Graph usage-report mailbox and OneDrive counts.
- **Installer** — MSI and unsigned MSIX built, checked and hashed; MSIX signing is supported but no certificate exists.
- **Per-module lifecycle** — complete: discovery rules, assessment, recommendations, module-tagged deployment operations, module-scoped validation, reporting, rollback traversal and module-tagged drift signals. The depth of DSPM/Defender evidence is tracked separately under DSC-08/DSC-11.

## Phase 2 — Remaining engineering

Ordered by risk to a commercial claim.

0. **Desktop wiring and Entra SSO (SRV-07, IDN-01–IDN-07).** The customer-hosted service now exists and is tested: shared store, seven-role server RBAC, tenant isolation, concurrency, durable approvals and a per-tenant audit chain ([service guide](11-collaboration-service.md)). Remaining: wire the WPF client to it, add MSAL/WAM sign-in and claim mapping, verify SQL Server against a real instance, and ship versioned migrations.
1. **Code signing and update trust (SEC-11, SEC-16).** Owner supplies an Authenticode certificate; CI signs the EXE/MSI/MSIX; publish a hosted HTTPS manifest and decide whether automatic install is allowed. The hash-verified download path already exists. Owner action, not code-complete here.
2. **Automated rollback restore (DEP-03, SAF-02).** Implement a reviewed restore of the exported object backup against a live tenant, execute it through the fail-closed executor, validate post-restore state and record the result. Blocked without a tenant; must not be faked.
3. **DSPM and AI-governance discovery (DSC-08) and Defender/Compliance Manager ingestion (DSC-11).** DSPM enablement and Compliance Manager scores have no stable public API used here; Defender alerts/incidents can be added, but the compliance-score half remains blocked. Keep these reporting as warnings, not false passes.
4. **Graph SDK and optional SQL Server (ARC-05, ARC-08).** The REST adapter is deliberate and tested; adopting the SDK is a dependency decision. SQL Server requires an additional store implementation and integration tests against a real instance.
5. **SQLCipher page encryption (SEC-14).** Payloads are DPAPI-encrypted; database pages and indexed metadata are not. Adding SQLCipher changes the storage dependency and migration path.
6. **WinUI 3 shell (ARC-02).** Not buildable in this environment. The Application layer is UI-agnostic, so a second shell remains feasible without rewriting domain logic.
7. **Security Copilot (AIA-02).** No generally available tenant-licensed API surface was available to integrate; do not invent one.

## Phase 3 — Product depth

- Custom sensitive information type authoring and validation.
- Localisation of UI strings and report templates.
- Signed plugin manifests with explicit permission declarations (the loader currently trusts the user's own plugin folder).
- Windows service host for monitoring, with native email/Teams delivery.
- High-fidelity UI designs and accessibility review against the implemented XAML.

## Explicitly out of scope

- Writing to tenants without an approved, signed executor profile.
- Storing AI provider keys in the application database.
- Sending raw tenant identifiers to any AI provider.
- Supporting end-of-life Windows versions.
