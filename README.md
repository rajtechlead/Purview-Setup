# Purview Governance Copilot

A Windows desktop workspace for **assessing, planning, deploying, monitoring and reporting** Microsoft Purview governance — with a mandatory local privacy guard in front of every AI call.

Designed and built by **Raj Raja** · Owned by **Belle-RajaCorp** · contact@belle-rajacorp.com

---

## What it does

| Area | Capability |
| --- | --- |
| Discovery | Microsoft Graph (identity, licensing, posture) + Security & Compliance PowerShell (labels, DLP, retention, audit, insider risk, eDiscovery) — **read-only** |
| Assessment | 49 evidence-based controls across 16 Purview modules, deterministic scoring |
| Scoring | Security, Governance, Compliance, Maturity, Risk, AI Governance, Copilot Readiness |
| Planning | Recommended, guided, custom, expert and greenfield change plans with full change-management packs |
| Safety | Pre-change backups, simulation (what-if), 3-stage approval (Security → Compliance → CAB), rollback packages |
| Monitoring | Drift detection, local alerts with optional HTTPS webhook delivery, an in-app check schedule, a generated daily Task Scheduler job for the CLI, licence utilisation and optimisation insights, score trends and a module × severity risk heatmap |
| National clouds | Connection selector for Commercial/GCC, GCC High and DoD with matching Entra, Graph and compliance PowerShell endpoints |
| Multi-tenant | Customer tenant registry with per-tenant credentials, tenant activation, a cross-tenant portfolio of latest scores and CSV export |
| Updates | HTTPS manifest check with version comparison and SHA-256-verified MSI download; installing stays manual |
| Action centre | Owner, due date, status and DPAPI-encrypted notes for every open finding, stored per tenant |
| Reporting | PDF, DOCX, XLSX, HTML, JSON, CSV and Markdown executive/framework reports (ISO 27001, NIST CSF, CIS v8, SOC 2, HIPAA, GDPR, PCI-DSS, ISO 42001, NIST AI RMF), generated with no external report library |
| AI | OpenAI, Azure OpenAI, Anthropic, Gemini, DeepSeek, Grok, OpenRouter, Ollama, LM Studio, Open WebUI, custom endpoints — with quota-aware failover, token/cost tracking and offline mode |

## Quick start

Requirements: **Windows 10/11 or Windows Server 2019/2022/2025**, .NET 9 Desktop Runtime (or the self-contained build), and PowerShell 5.1+ with `ExchangeOnlineManagement` for Purview configuration reads.

```powershell
# Build and run from source
dotnet build PurviewGovernanceCopilot.sln
dotnet run --project src/PurviewCopilot.UI

# Tests
dotnet test tests/PurviewCopilot.Tests
```

No tenant yet? Open **Assessment → Load sample tenant**: the entire workspace (scores, findings, plan, reports) runs against clearly-labelled demo data.

## Connect a tenant

1. **Settings → Certificate lifecycle → Create** — generates a 4096-bit RSA self-signed certificate in `CurrentUser\My` and fills in the thumbprint.
2. Register the certificate on an Entra app registration and grant:
   - `Organization.Read.All`, `User.Read.All`, `Group.Read.All`, `Application.Read.All`, `RoleManagement.Read.Directory`, `Policy.Read.All`, `SecurityEvents.Read.All` (Graph application permissions)
   - `Exchange.ManageAsApp` (Office 365 Exchange Online) with the service principal added to the **Compliance Administrator** role.
3. Grant admin consent, then **Test connection**.
4. **Assessment → Discover tenant**.

The wizard's checked steps and the full permission rationale are in [`docs/05-admin-guide.md`](docs/05-admin-guide.md).

## Privacy by design

Every prompt and every assessment summary passes through a local detector (email, phone, national ID, payment card with Luhn validation, IBAN, JWT, bearer token, AWS key, private key, connection-string secret, tenant GUID/domain, SharePoint URL) before transmission. Modes: **Redact**, **Mask**, **Tokenize** (reversible locally), **Block**. Secrets and credentials are blocked outright in Block mode. The exact sanitized prompt is shown for review, and every decision plus every provider attempt is written to the local audit log. Cloud AI can be disabled; offline mode restricts calls to local providers.

## Repository layout

```
PurviewGovernanceCopilot.sln
src/
  PurviewCopilot.Domain/          # models, scoring, privacy detection (no I/O)
  PurviewCopilot.Application/     # modules, assessment, planning, approvals, AI orchestration, reports
  PurviewCopilot.Infrastructure/  # Graph, PowerShell, SQLite, certificates, Key Vault, AI providers
  PurviewCopilot.UI/              # WPF shell, dashboards, pages (MVVM)
  PurviewCopilot.Cli/             # headless `pgc assess` / `pgc verify-audit`
tests/
  PurviewCopilot.Tests/           # 109 automated tests
docs/                             # architecture, design, security, guides, traceability
installer/                        # WiX MSI project + MSIX manifest
dist/                             # built MSI + SHA-256 (framework-dependent, unsigned)
.github/workflows/ci.yml          # build, test, publish
```

## Headless CLI

`src/PurviewCopilot.Cli` runs the same `GovernanceWorkspace` pipeline for scheduled tasks and pipelines:

```powershell
dotnet run --project src/PurviewCopilot.Cli -c Release -- assess --demo --format json --out report.json
dotnet run --project src/PurviewCopilot.Cli -c Release -- assess --tenant <id> --client <app-id> --thumbprint <thumbprint> --format pdf
dotnet run --project src/PurviewCopilot.Cli -c Release -- verify-audit
```

`--demo` scores the built-in sample tenant, so the command works without a tenant. A live run reuses the certificate in `CurrentUser\My`, stores evidence in `%LOCALAPPDATA%\PurviewGovernanceCopilot` (override with `--data-dir`), and writes Html, Json, Csv, Markdown, Pdf, Docx or Xlsx. It never applies changes, and exits non-zero on failure.

## Documentation

| Document | Contents |
| --- | --- |
| [01 Solution architecture](docs/01-solution-architecture.md) | Layers, folder structure, module and plugin architecture, deployment modes |
| [02 Detailed design](docs/02-detailed-design.md) | Database schema, API/Graph surface, UI wireframes |
| [03 Security architecture](docs/03-security-architecture.md) | Auth, certificate lifecycle, AI privacy, threat model, security review |
| [04 User guide](docs/04-user-guide.md) | Every screen, end to end |
| [05 Admin guide](docs/05-admin-guide.md) | Deployment, Intune/MECM, install, upgrade, operations, troubleshooting |
| [06 Roadmap](docs/06-roadmap.md) | Phase 2 and beyond |
| [07 Requirements traceability](docs/07-requirements-traceability.md) | Spec → implementation status summary |
| [08 Requirements tracker](docs/08-requirements-tracker.md) | Requirement-level tracker: 134 requirements with status, proof and gap |
| [09 Deliverables tracker](docs/09-deliverables-tracker.md) | The 22 named deliverables, locations and limitations |

## Status

Version **1.0.0** — buildable, tested and runnable. Assessment, scoring, planning, approval, backup, simulation, reporting, AI failover and privacy guard are implemented and covered by tests. The local audit log is hash-chained and verifiable from Settings, AI failover probes provider health and cools down failed providers, and the lab client secret is DPAPI-protected at rest. Deployment exports a reviewed PowerShell script built from real Purview cmdlets, a reversal script and a portable JSON plan that can be imported on another workstation. Settings includes an Authentication Provisioning Center that can create the app registration, service principal, certificate, Graph permissions and consent in one guided flow (dry run first), and certificate generation supports RSA 2048/3072/4096 and ECC P-256/P-384. Live tenant mutation remains gated in this build: deployment generates a reviewed PowerShell script rather than executing it. See the trackers for the precise boundary.

The application is read-only by default and requires no administrator rights: certificates install per-user and data is stored in `%LOCALAPPDATA%\PurviewGovernanceCopilot`.
