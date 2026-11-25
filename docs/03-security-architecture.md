# 03 — Security Architecture, Threat Model and Security Review

## 1. Security objectives

| Objective | Control |
| --- | --- |
| Assessment never mutates the tenant | All discovery is GET-only; the PowerShell script is read-only and reviewed in source |
| Least privilege | Purview role groups, app-only Graph permissions, no standing Global Admin requirement |
| No credential sprawl | Certificate is the default; private keys stay in the Windows store; provider keys only in environment variables |
| No tenant data leaves the machine unredacted | Local detector + modes + review + audit + offline mode |
| Change is deliberate and reversible | Backup → simulate → 3-stage approval → gated apply → rollback package |
| Evidence is trustworthy | Deterministic scoring, append-only approval history, local audit log |

## 2. Authentication architecture

`AuthConfiguration.Mode` selects one of four credentials through `EntraTokenProvider`:

| Mode | Credential | Use |
| --- | --- | --- |
| `Certificate` (default) | `ClientCertificateCredential` with an RSA private key from `CurrentUser\My` | Production, unattended, app-only |
| `ManagedIdentity` | `ManagedIdentityCredential` (optional client ID) | Azure-hosted or managed workstation |
| `Interactive` | `InteractiveBrowserCredential` (loopback redirect) | First-run/diagnostic only |
| `ClientSecret` | `ClientSecretCredential` | Lab/testing only, labelled in the UI |

Certificate requirements: RSA 4096, SHA-256, client-auth EKU, 12-month default validity (1–36 months enforced), subject `CN=Purview Governance Copilot` (configurable). The certificate is created in the per-user store, never machine-wide, and export requires an operator-chosen PFX password.

Rotation: `Rotate` creates the replacement first (no authentication gap), returns the new thumbprint, and leaves removal of the old certificate to the operator after the app registration is updated. `CertificateInfo.DaysUntilExpiry` drives the 30-day warning on the Settings page.

Key Vault (optional): PFX is stored base64 in a vault secret via the REST API with a `DefaultAzureCredential` token; the vault URI must be HTTPS. This gives central rotation without putting the PFX in the app database.

Guided setup, as implemented: the Settings page presents connection (auth mode, tenant, client, thumbprint, test), certificate lifecycle (create/import/export/rotate/remove with a live expiry list) and provider/privacy configuration. It is a single reviewable page rather than a modal multi-step wizard; every step the wizard would perform is present and labelled.

## 3. AI privacy architecture (mandatory gate)

```
user text + assessment summary
        │
        ▼  SensitiveDataDetector (regex, local, 250 ms timeout per rule)
email · phone · SSN · payment card (Luhn) · IBAN · JWT · bearer · AWS key
private key · connection-string secret · tenant GUID/domain · SharePoint URL
        │
        ▼  RedactionService (mode)
Redact  → [REDACTED:PII]
Mask    → ****1234
Tokenize→ <<EMAIL_1>> (stable per call; map kept locally)
Block   → refuse to send if secrets/credentials/tenant ids, or ≥3 matches
        │
        ▼  PreparedPrompt → displayed verbatim → administrator reviews
        │
        ▼  AiFailoverOrchestrator → provider
        │
        ▼  Audit log: rule categories, mode, provider, tokens, cost (never values)
```

Guarantees:

- The guard runs on the **combined** string (question + context), so assessment text cannot bypass it.
- Blocked prompts throw before any provider is constructed; the audit entry records only rule IDs.
- Token maps are returned to the caller and are never persisted or transmitted; `Restore` exists only for local review.
- Offline/cloud-disabled mode filters providers to `IsLocal`; if none remain, the call fails closed with an actionable message.
- Provider failures record `error_code` only; response bodies are truncated and never logged in full.

Residual risk: regex detection is not a DLP engine. Free-text descriptions that identify a customer without matching a pattern can pass. That is why administrator review is on by default and why cloud AI can be disabled entirely.

## 4. Cryptography and data protection

| Asset | At rest | In transit |
| --- | --- | --- |
| Certificate private key | Windows CNG key container (per-user), non-exportable unless exported by the operator | n/a |
| PFX export | Operator-supplied password; file location chosen by operator | n/a |
| SQLite database | Plaintext in `%LOCALAPPDATA%` (see gap S-04) | n/a |
| Graph/PowerShell | n/a | TLS 1.2+ (platform default) |
| AI prompts | Sanitized only | HTTPS endpoints; local providers on loopback |

## 5. Threat model (STRIDE)

| ID | Threat | Vector | Impact | Mitigation | Residual |
| --- | --- | --- | --- | --- | --- |
| T-01 | Prompt exfiltration of tenant data | AI provider request | Data breach | Local detection, tokenization, review, audit, offline mode | Medium — pattern-based detection |
| T-02 | Credential theft from workstation | Local malware reading DB/env | Tenant compromise | No keys in DB; cert key in CNG store; per-user install; no elevation | Medium — user-session compromise is out of scope |
| T-03 | Malicious change plan applied | Crafted operation payload | Tenant outage | Live apply disabled; simulation; 3-stage approval; backup; destructive flag | Low in this build |
| T-04 | Approval bypass | UI or direct DB edit | Unauthorised change | State machine rejects invalid transitions; history append-only; plan state mirrored | Medium — local DB is user-writable |
| T-05 | Prompt injection via assessed content | Crafted display name in context | Misleading AI output | AI output is advisory only; no tool execution from model output; prompts instruct not to invent facts | Low |
| T-06 | Supply-chain compromise | NuGet dependency | Code execution | Two dependencies only, pinned versions, CI vulnerability scan, no post-install scripts | Medium |
| T-07 | Tampering with reports/evidence | Local file edit | False compliance evidence | Reports are derived, not authoritative; audit log records generation; evidence store is external | Medium |
| T-08 | TLS interception | Malicious proxy | Token/credential theft | Platform TLS validation, no custom callback disabling validation | Low |
| T-09 | Over-broad permissions | App registration | Excess read access | Documented minimum permissions; read-only scopes; no write Graph scopes requested | Low |
| T-10 | Denial of service by huge exports | Large tenant | UI freeze | Payload caps on lists, report size bounded by findings (48), async UI operations | Low |
| T-11 | Stale certificate | Expiry | Assessment outage | 30-day warning, rotation without gap, thumbprint validation | Low |
| T-12 | Log leakage | Exception text | Secret disclosure | `ScrubForLog`, truncated provider bodies, no tokens in audit | Low |

Out of scope: physical access, compromised administrator account, malicious Microsoft 365 tenant configuration, and attacks requiring code execution on the workstation.

## 6. Security review against standards

### Microsoft SDL / NIST SSDF

| Practice | Status |
| --- | --- |
| Threat modelling | This document, §5 |
| Least privilege | Read-only discovery; per-user install; no elevation; minimum scopes |
| Secure defaults | Certificate auth; review-before-send; cloud AI optional; live apply off |
| Dependency management | Two NuGet dependencies, pinned; `dotnet list package --vulnerable` in CI |
| Static analysis | .NET analyzers enabled (`AnalysisLevel=latest`); warnings surfaced in build |
| Secrets management | Environment variables (providers), Windows store (cert), no secrets in git; `.gitignore` blocks `.env`/PFX |
| Code signing | **Not configured** — CI has a placeholder step; production releases must be signed |
| Secure update | MSI/MSIX authored with major-upgrade semantics; no self-updater (gap S-05) |

### OWASP Top 10 (2021)

| Risk | Relevance | Handling |
| --- | --- | --- |
| A01 Broken access control | Local app; tenant RBAC is external | Approval state machine, no privilege escalation, role guidance |
| A02 Cryptographic failures | Certificate/private key | CNG store, PFX password, TLS defaults |
| A03 Injection | SQL and PowerShell | Parameterized SQLite everywhere; PowerShell script is static with quoted parameters; no user string concatenated into commands |
| A04 Insecure design | Governance workflow | Simulation + approval + backup by design |
| A05 Security misconfiguration | Permissions, AI providers | Documented scopes, certificate default, cloud AI off-switch |
| A06 Vulnerable components | NuGet | Minimal pinned dependencies, CI scan |
| A07 Auth failures | Entra credentials | Entra-managed auth, cert default, no custom auth |
| A08 Integrity failures | Change plan/report | Backup, approval history, deterministic output |
| A09 Logging failures | Audit | Local audit for assessment, approval, AI, export |
| A10 SSRF | Custom AI endpoint | Operator-configured endpoint only; no URL fetched from tenant data |

### OWASP ASVS 4.0 (target level 2, selected)

- 2.10.4 — no embedded secrets: satisfied (env vars / store).
- 5.3.4 — parameterized queries: satisfied.
- 7.1.1 — no credentials in logs: satisfied via `ScrubForLog`.
- 8.3.1 — sensitive data not cached in URLs: satisfied (no query-string secrets; Gemini key is the exception mandated by that API — documented risk).
- 12.1.1 — TLS: platform default, no validation bypass.

### CIS Controls v8

CIS 3.13 (data protection), 4.1 (secure configuration), 5.4 (restrict admin privileges), 6.3/6.5 (MFA), 8.2 (audit logging) are mapped in the control catalog and surfaced per finding.

## 7. Findings from this review

| ID | Severity | Finding | Recommendation |
| --- | --- | --- | --- |
| S-01 | High | Live tenant execution is disabled, which is safe but incomplete for "one-click deployment" | Implement a signed, separately-installed executor profile with its own approval gate |
| S-02 | Medium | SQLite database and audit log are unencrypted | Add SQLCipher/DPAPI-wrapped key or store evidence in a protected location |
| S-03 | Medium | Gemini requires the API key in the query string | Prefer header-based auth if Google adds it; redact URLs in logs |
| S-04 | Medium | Client-secret mode stores the secret in the local DB | Restrict to lab builds or move to DPAPI-protected storage |
| S-05 | Medium | No signed binaries or signed update channel | Configure Authenticode in CI and publish checksums |
| S-06 | Low | Health checks exist on providers but are not consulted by the failover loop | Add pre-flight health probing with a circuit breaker |
| S-07 | Low | Regular-expression PII detection can miss context-dependent identifiers | Add optional Microsoft Presidio-style local model later |
| S-08 | Low | Local DB is user-writable, so approval history is not tamper-evident | Add hash-chaining of audit entries |

These are tracked in the roadmap; none blocks read-only assessment use.

## 8. Hardening checklist for deployment

- [ ] Sign the MSI and executable; publish SHA-256 hashes.
- [ ] Grant only the documented Graph application permissions; no `Directory.ReadWrite.All`.
- [ ] Add the app's service principal to **Compliance Administrator** (or a custom read-only role group) — not Global Administrator.
- [ ] Keep cloud AI disabled until the DLP/privacy decision is recorded.
- [ ] Restrict who can run the tool to a governance workstation; it holds assessment evidence.
- [ ] Back up `%LOCALAPPDATA%\PurviewGovernanceCopilot` before upgrades.
- [ ] Review the 30-day certificate warning monthly.
