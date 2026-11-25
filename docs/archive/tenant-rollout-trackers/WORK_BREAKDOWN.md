# Work Breakdown

Authoritative task list. Every task has an ID, an owner role, acceptance criteria and a test reference where verification applies.

**Status values:** `Done` · `In progress` · `Next` · `Blocked` · `Backlog`
**Owner roles:** PL = Purview Lead · SEC = Security/Compliance · LEG = Legal · ITOPS = IT Operations/Endpoint · CHG = Change Manager · SPON = Sponsor

---

## Phase 0 — Mobilize

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-001 | Create trackers, repo scaffolding, working agreement | PL | Done | — | All tracker files present and linked from README; changelog 0.1.0 |
| PS-002 | Confirm sponsor, phase owners, RACI sign-off | PL/SPON | In progress | PS-001 | RACI accepted by all named roles; sponsor named in charter |
| PS-003 | Validate licensing entitlements (E3/E5/A5, Purview add-ons) | PL | Blocked | PS-010 | SKU inventory signed off; capability matrix per SKU agreed; gaps logged in risks |
| PS-004 | Confirm scope and exclusions (Data Map, Priva, DSPM for AI) | SPON | Next | PS-003 | Charter §4 signed; conditional items moved to backlog with a decision record |
| PS-005 | Agree change windows and CAB path | CHG | Backlog | PS-002 | Documented window, approver and lead time; emergency path defined |

## Phase 1 — Assess & baseline

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-010 | Run tenant readiness assessment | PL | In progress | PS-001 | `Get-PurviewReadiness.ps1` completes with no failed checks unaccounted for; evidence stored per evidence standard |
| PS-011 | Audit enablement and retention baseline | SEC | Next | PS-010 | Unified audit ingestion confirmed on; retention policy inventory captured; gap against legal requirement recorded |
| PS-012 | Data estate inventory (SPO sites, Teams, OneDrive, Exchange, endpoints) | ITOPS | Next | PS-010 | Counts by location, top 50 sites by sensitivity, endpoint onboarding coverage % |
| PS-013 | Export existing label/DLP/retention policy baseline | PL | Next | PS-010 | JSON/CSV export of all policies with mode, locations, rule counts; stored in `assessments/` |
| PS-014 | Role group baseline and least-privilege gap analysis | SEC | Backlog | PS-010 | Every Purview role group mapped to RACI role; standing Global Admin count recorded; gaps listed |
| PS-015 | Endpoint onboarding readiness | ITOPS | Backlog | PS-012 | Onboarded device count, supported OS coverage, exclusion list agreed |
| PS-016 | Portal, network and browser validation | ITOPS | Backlog | PS-010 | Purview portal reachable for all admins; required endpoints allowed; browser baseline documented |

## Phase 2 — Foundation

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-020 | Sensitivity taxonomy v1 (levels, sub-labels, meanings) | SEC | Next | PS-003 | 4 top-level levels approved by sponsor and legal; usage guidance written |
| PS-021 | Build and publish label set to pilot group | PL | Backlog | PS-020 | Labels created in test policy; published to ≤50 pilot users; no taxonomy errors |
| PS-022 | Configure audit retention policy | SEC | Backlog | PS-011 | Retention period ≥ legal minimum; policy scoped correctly; verified with `T-001` |
| PS-023 | Assign Purview role groups per RACI | SEC | Backlog | PS-014 | Every RACI role has a matching group membership; no permanent Global Admin for daily work |
| PS-024 | Baseline retention policy for all locations | LEG/PL | Backlog | PS-013 | Policy covers Exchange, SPO, OD, Teams; 7-year default or legal schedule; no user-visible surprise |
| PS-025 | Naming and description standards for labels and policies | PL | Backlog | PS-020 | Convention documented in runbook; applied to all new objects |

## Phase 3 — Information protection

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-030 | Client-side auto-labelling pilot | PL | Backlog | PS-021 | Rule set runs on pilot; recommendation vs auto-apply decided per rule |
| PS-031 | Service-side auto-labelling (SIT-based) | PL | Backlog | PS-021 | ≥3 SIT rules in simulation; precision reviewed on 200-item sample |
| PS-032 | Encryption, content marking and watermarking | SEC | Backlog | PS-020 | Encryption templates tested with internal/external access; content marking renders correctly |
| PS-033 | Label adoption comms and training | CHG | Backlog | PS-021 | Comms sent, 30-min training delivered, FAQ published |

## Phase 4 — Data loss prevention

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-040 | DLP policy design (PII, PCI, PHI, credentials) | SEC | Backlog | PS-020 | Rule matrix approved: sensitive types, thresholds, actions, user overrides |
| PS-041 | Exchange/SPO/OD/Teams DLP in simulation | PL | Backlog | PS-040 | Test mode on all four locations; matches reviewed daily for 10 business days |
| PS-042 | Endpoint DLP pilot (10 devices) | ITOPS | Backlog | PS-015, PS-041 | Copy/upload/print controls fire on pilot; override reasons captured |
| PS-043 | Browser and cloud-app DLP | SEC | Backlog | PS-041 | Policy covers unmanaged browser upload for in-scope users/ apps |
| PS-044 | Alert triage workflow and routing | SEC | Backlog | PS-041 | Named queue, SLA (P1 same day), disposition categories, escalation path |
| PS-045 | Enforcement approval and cutover | SPON | Backlog | PS-041, PS-044 | Simulation report accepted; false-positive rate < 5%; change approved; rollback plan tested |

## Phase 5 — Insider risk & communication compliance

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-050 | Insider Risk settings, indicators, anonymization policy | SEC | Backlog | PS-023 | Indicators enabled per risk decision; HR/legal/works-council sign-off recorded |
| PS-051 | IRM policy pilot (data theft by departing users) | SEC | Backlog | PS-050 | Policy live for pilot; 10 alerts triaged; thresholds tuned |
| PS-052 | Adaptive protection (conditional) | SEC | Backlog | PS-051, PS-045 | Adaptive control linked to DLP only if licensed and approved |
| PS-053 | Communication Compliance policy scoping | SEC/LEG | Backlog | PS-023 | Scoped reviewers, reviewable content list, retention window agreed |

## Phase 6 — Lifecycle & records

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-060 | Retention labels and file plan | LEG | Backlog | PS-024 | File plan approved: code, retention period, trigger, disposition |
| PS-061 | Disposition review | LEG | Backlog | PS-060 | Reviewers assigned; first review cycle completed on test labels |
| PS-062 | Regulatory records configuration | LEG | Backlog | PS-060 | Record/regulatory labels prevent edit/delete as required |

## Phase 7 — eDiscovery & audit assurance

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-070 | eDiscovery process, roles and case template | LEG | Backlog | PS-023, PS-022 | Case roles assigned; custodian workflow documented; test case executed |
| PS-071 | Legal hold playbook | LEG | Backlog | PS-070 | Hold applied/removed in test; preservation verified |
| PS-072 | Audit search playbook and alert policies | SEC | Backlog | PS-011 | 5 standard searches documented; 3 alert policies live and routed |

## Phase 8 — Data Map (conditional)

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-080 | Confirm Data Map scope and cost model | SPON | Backlog | PS-004 | Funded or explicitly deferred; decision recorded in `DECISIONS.md` |
| PS-081 | Register sources and configure scans | PL | Backlog | PS-080 | Sources registered; first scan complete; classification report reviewed |
| PS-082 | Classifications, glossary and ownership | PL | Backlog | PS-081 | Glossary terms with owners; critical data elements mapped |

## Phase 9 — Operate & hand over

| ID | Task | Owner | Status | Depends on | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PS-090 | Monitoring and reporting (policy match, alert SLA) | PL | Backlog | PS-044 | Dashboard or scheduled report produced weekly; SLA breaches visible |
| PS-091 | Tuning cadence and change log | SEC | Backlog | PS-045 | Monthly tuning review scheduled; decisions recorded |
| PS-092 | Runbook handover and training | ITOPS | Backlog | PS-090 | Ops team executes 3 runbooks unaided; sign-off recorded |
| PS-093 | Closeout report | PL | Backlog | PS-092 | Residual risks, metrics, backlog for phase 2 published |

---

## Roll-up

| Phase | Items | Done | In progress | Blocked | Backlog |
| --- | --- | --- | --- | --- | --- |
| 0 Mobilize | 5 | 1 | 1 | 1 | 2 |
| 1 Assess | 7 | 0 | 2 | 0 | 5 |
| 2 Foundation | 6 | 0 | 0 | 0 | 6 |
| 3 Information protection | 4 | 0 | 0 | 0 | 4 |
| 4 DLP | 6 | 0 | 0 | 0 | 6 |
| 5 IRM & CC | 4 | 0 | 0 | 0 | 4 |
| 6 Lifecycle | 3 | 0 | 0 | 0 | 3 |
| 7 eDiscovery | 3 | 0 | 0 | 0 | 3 |
| 8 Data Map | 3 | 0 | 0 | 0 | 3 |
| 9 Operate | 4 | 0 | 0 | 0 | 4 |
| **Total** | **45** | **1** | **3** | **1** | **40** |
