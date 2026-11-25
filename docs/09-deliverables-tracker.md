# 09 — Deliverables Tracker

The specification names 22 deliverables. This tracker records where each one lives, whether it exists in a usable form, and the honest limitation.

**Result: 22 of 22 artifacts exist in the repository.** Two are weaker than the heading implies — the interface designs are text wireframes rather than high-fidelity screens, and the API specification covers consumed Graph/Exchange surfaces and internal contracts rather than an OpenAPI document. The product itself still has the 20 open requirements listed in [08 — Requirements Tracker](08-requirements-tracker.md); artifact completeness is not feature completeness.

| # | Deliverable | Status | Where | Notes and limitations |
| --- | --- | --- | --- | --- |
| 1 | Solution Architecture | **Delivered** | `docs/01-solution-architecture.md` | Goals, layer model, pipeline, module/plugin design, deployment modes, AI orchestration, persistence, non-functional characteristics. |
| 2 | Detailed Design | **Delivered** | `docs/02-detailed-design.md` | Schema, consumed API surface, internal contracts, UI information architecture, scoring specification, approval state machine, validation plan. |
| 3 | Folder Structure | **Delivered** | `docs/01` §3, `README.md` | Four source layers, tests, docs, installer, CI. Matches the repository. |
| 4 | UI Wireframes | **Delivered** | `docs/02` §4 | Text/ASCII wireframes for dashboard, assessment, deployment, reports and settings, matching the implemented XAML. Not high-fidelity mockups. |
| 5 | Database Schema | **Delivered** | `docs/02` §1, `SqliteStore.cs` | Eight tables: assessments, change_plans, approvals, backups, audit_log, ai_usage, settings, baselines. |
| 6 | API Specifications | **Delivered** | `docs/02` §2–3 | Graph v1.0/beta and Security & Compliance PowerShell surfaces consumed, plus internal port contracts. No OpenAPI/Swagger document, because the app exposes no inbound API. |
| 7 | Authentication Architecture | **Delivered** | `docs/03` §2 | Certificate-first credential selection, managed identity, interactive, client secret; permission rationale. |
| 8 | Certificate Management Architecture | **Delivered** | `docs/03` §2, `WindowsCertificateService.cs` | Generation, import/export, store integration, rotation, expiry monitoring, Key Vault store design (not yet wired — AUT-04). |
| 9 | AI Privacy Architecture | **Delivered** | `docs/03` §3, `Domain/Privacy/*` | Detector, four treatment modes, review gate, audit, cloud disable and offline mode. |
| 10 | Module Architecture | **Delivered** | `docs/01` §5, `StandardModule.cs` | 16 modules over 49 rules, weights, descriptors, `IModule` extension seam. |
| 11 | Source Code | **Delivered** | `src/` | 5 projects, 122 C# files, 12 XAML files; Release build succeeds. |
| 12 | Installer Project | **Delivered** | `installer/` | WiX v4 project + `Package.wxs`, MSIX manifest, `build.ps1` with optional signing. Binaries not produced here (WiX and certificate absent — INS-07). |
| 13 | CI/CD Pipeline | **Delivered** | `.github/workflows/ci.yml` | Restore, Release build, tests with TRX upload, vulnerability scan, self-contained single-file publish, artifact upload, commented signing step. |
| 14 | Threat Model | **Delivered** | `docs/03` §5 | STRIDE across trust boundaries with mitigations. |
| 15 | Security Review | **Delivered** | `docs/03` §6–7 | OWASP Top 10/ASVS, CIS, NIST SSDF mapping; findings S-01…S-08 with severity. |
| 16 | Automated Test Framework | **Delivered** | `tests/PurviewCopilot.Tests` | 68 xUnit tests: scoring, catalog integrity, privacy detection/treatment, approvals, planning, licensing, drift, failover, provider health/cooldown, SQLite, DPAPI secret protection, audit hash chain, real PowerShell payloads, rollback script, config-package round trip, PDF/DOCX/XLSX package validity, risk heatmap and trend series, tenant profiles and portfolio isolation, monitoring alerts, DPAPI payload encryption, AI usage summaries, reports. All passing. |
| 17 | User Guide | **Delivered** | `docs/04-user-guide.md` | Every screen end to end, plus accessibility notes. |
| 18 | Admin Guide | **Delivered** | `docs/05-admin-guide.md` | Platforms, build, Entra registration, permissions, install, upgrade, operations, troubleshooting, diagnostics, uninstall. |
| 19 | Deployment Guide | **Delivered** | `docs/05` §2–5 | Build/publish, Entra setup, MSI/MSIX, Intune and MECM commands, upgrade and rollback. |
| 20 | Troubleshooting Guide | **Delivered** | `docs/05` §7–8 | Symptom/cause/fix table plus log and diagnostics locations. |
| 21 | Operations Runbook | **Delivered** | `docs/05` §6 | Routine cadence, certificate rotation, backup/restore, drift review, incident steps, contacts. |
| 22 | Future Roadmap | **Delivered** | `docs/06-roadmap.md` | Phases 1.1–3, explicitly out-of-scope items, and sequencing. |

## Documentation consistency fixed during this audit

- Control count corrected from 48 to 49 in `README.md`, `docs/01` and `docs/07`.
- `docs/07` now points to this tracker and the requirement-level tracker as the authoritative status.
- PDF export is described as not implemented; earlier text implied a browser-print workaround was a delivered format.

## Cross-reference

- Requirement-level status with evidence and gaps: [08 — Requirements Tracker](08-requirements-tracker.md)
- Narrative architecture and security rationale: `docs/01`–`docs/03`
- Phase plan for closing the gaps: `docs/06-roadmap.md`
