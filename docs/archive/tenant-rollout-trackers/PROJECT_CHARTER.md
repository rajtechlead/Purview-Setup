# Project Charter — Microsoft Purview Setup

| Field | Value |
| --- | --- |
| Document owner | Purview Lead |
| Approver | Project Sponsor (CISO delegate) |
| Version | 1.0 |
| Baseline | 2026-11-25 |
| Status | Draft — pending sponsor sign-off (`PS-002`) |

## 1. Problem statement

The tenant has Microsoft 365 data spread across Exchange Online, SharePoint Online, OneDrive, Teams and managed endpoints, but no consistent classification, no enforced data-loss controls, no evidence-grade audit retention and no repeatable insider-risk process. Compliance obligations (regulatory records, PII/PCI handling, legal hold) are met manually, which is slow and unverifiable.

## 2. Objective

Deploy Microsoft Purview as a governed capability — not a one-off configuration — so that:

1. Every document and email can carry a sensitivity label, and 80% of labelled content is labelled automatically or by default.
2. Data-loss policies cover the four primary egress channels (email, collaborative links, endpoint copy, browser upload) and are in enforce mode.
3. Audit events are retained for the period required by legal, with a tested search procedure.
4. Insider-risk and communication-compliance signals are triaged by a named team against a documented playbook.
5. Retention and records obligations are applied by policy rather than by user behaviour.

## 3. Success criteria (measurable)

| # | Criterion | Measure | Verified by |
| --- | --- | --- | --- |
| S1 | Label coverage | ≥ 80% of SharePoint/OneDrive content labelled (manual or auto) | Label activity explorer |
| S2 | DLP coverage | 100% of in-scope locations covered; 0 unactioned high-severity alerts > 5 days | DLP alert report |
| S3 | Audit readiness | Unified audit ingestion on; retention ≥ legal minimum; search procedure executed twice | `T-001`, `T-012` |
| S4 | Insider risk | ≥ 1 policy live; triage SLA documented; first 10 alerts dispositioned | `T-006` |
| S5 | Retention | All locations covered by a retention policy; disposition review tested | `T-007` |
| S6 | Operability | Runbooks + monitoring + tuning cadence handed over; no P1 config drift | `PS-090`–`PS-093` |

## 4. Scope

### In scope

- Licensing and role readiness assessment; least-privilege role model.
- Unified audit enablement and retention.
- Sensitivity label taxonomy, label set, publishing, client-side and service-side auto-labelling.
- DLP for Exchange, SharePoint, OneDrive, Teams, endpoint devices and browser upload.
- Insider Risk Management (one pilot policy, adaptive protection optional).
- Communication Compliance (one scoped policy, if licensed).
- Retention policies, retention labels, file plan and disposition review.
- eDiscovery (standard/premium as licensed) process and legal-hold playbook.
- Alerting, monitoring, tuning and handover.

### Out of scope (v1)

- Microsoft Purview Data Map / data governance (catalogue, scans, glossary) — conditional, see `PS-080` and `D-006`.
- Purview Priva / privacy management.
- DSPM for AI beyond inventorying available capability.
- Third-party connector ingestion and on-premises scanner deployment.
- Data migration, tenant-to-tenant moves, and endpoint management platform changes.

## 5. Constraints and assumptions

| Type | Statement |
| --- | --- |
| Constraint | Configuration changes require CAB approval and a change window (`PS-005`). |
| Constraint | No production enforcement without a completed simulation window and evidence. |
| Constraint | Only licensed capabilities are deployed; unlicensed items move to the backlog. |
| Assumption | Global Administrator is available for initial role assignment and audit enablement. |
| Assumption | Endpoint fleet is managed by a supported MDM/Defender onboarding path. |
| Assumption | Legal/compliance supplies the retention schedule and record classes. |
| Assumption | A pilot group (≈50 users) can be formed for label/DLP pilots. |

## 6. Governance

- **Cadence:** weekly 30-minute delivery stand-up; bi-weekly risk review; gate review at the end of each phase.
- **Authority:** scope, licensing and enforcement decisions are approved by the Sponsor; technical design by the Purview Lead.
- **Change control:** all tenant changes via runbook + change record; emergency changes retro-documented within 24 hours.
- **Reporting:** `STATUS.md` is the single weekly report; gate packs are assembled from `TEST_MATRIX.md`.

## 7. Gates

| Gate | Name | Exit condition |
| --- | --- | --- |
| G0 | Mobilize | Charter signed, RACI agreed, licensing confirmed |
| G1 | Baseline approved | Readiness + data estate + policy baseline evidenced; risks scored |
| G2 | Foundation live | Labels, audit retention, baseline retention, roles in production |
| G3 | Protection live | Auto-labelling and DLP enforcing across in-scope locations |
| G4 | Assurance live | Insider risk, comms compliance, eDiscovery playbooks operating |
| G5 | Handover | Monitoring, tuning cycle and runbooks accepted by operations |

## 8. Deliverables

1. Tracker set (this repository) — current.
2. Readiness assessment evidence pack.
3. Label taxonomy and label/policy configuration as code where supported.
4. DLP policy set with simulation reports and enforcement approvals.
5. Retention/records configuration.
6. Operating runbooks, alert triage guides and training material.
7. Closeout report with residual risk and roadmap for phase 2.
