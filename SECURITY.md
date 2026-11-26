# Security policy

## Supported versions

Security fixes land on `main` and in the latest published MSI. Older builds are not patched; roll forward with the current package.

## Reporting a vulnerability

Email **contact@belle-rajacorp.com** with the affected version, a description, reproduction steps and any logs with secrets removed. Do not open a public issue for a suspected vulnerability.

Acknowledgement within 3 business days; an initial assessment within 10 business days. Reporters are credited unless they ask not to be.

## Bug bar

| Severity | Examples | Fix target |
| --- | --- | --- |
| Critical | Remote code execution, credential or certificate private-key disclosure, audit-chain forgery, unauthenticated configuration change, AI prompt egress that bypasses the privacy guard | Immediate hotfix; release blocked |
| High | Privilege escalation between customer roles, tenant data crossing an MSP boundary, rollback package that cannot restore, unsigned update accepted | Fix before the next release |
| Medium | Stored XSS in an exported report, denial of service from a crafted manifest, information disclosure limited to the local user | Next scheduled release |
| Low | Hardening gap with no demonstrated impact, verbose error text without secrets | Backlog with an owner |

A finding is fixed at the cause; suppressing a scanner, weakening a test or catching-and-ignoring an error is not an acceptable fix.

## Security development lifecycle

The project follows Microsoft SDL practices and NIST SSDF. The practice-to-evidence mapping, threat model summary and release sign-off checklist are in [`docs/10-security-assurance.md`](docs/10-security-assurance.md).

Non-negotiable gates for every release:

1. Build and the full test suite pass.
2. Dependency vulnerability scan is clean or each advisory is documented with a decision.
3. CodeQL analysis has no unresolved high or critical alert.
4. No secret, certificate private key, tenant evidence or production credential is committed or packaged.
5. Live deployment remains fail-closed and opt-in; rollback package and script are produced for every plan.
6. Privacy guard tests pass for every outbound AI path.

Signing is required for a production release. This repository does not contain a signing certificate, so the published MSI is unsigned until the owner supplies one; that limitation is recorded in the requirements tracker rather than hidden.
