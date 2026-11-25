# Roadmap

Baseline: **2026-11-25**. Durations are working weeks from baseline and are re-baselined at each gate, not silently slipped. Gate definitions are in `PROJECT_CHARTER.md` §7.

## Phase plan

| Phase | Name | Window | Gate | Primary outcome |
| --- | --- | --- | --- | --- |
| 0 | Mobilize | W0 | G0 | Charter, RACI, scope, licensing confirmed |
| 1 | Assess & baseline | W1–W2 | G1 | Readiness evidence, data estate inventory, policy baseline, scored risks |
| 2 | Foundation | W3–W4 | G2 | Roles, audit retention, label taxonomy, baseline retention live |
| 3 | Information protection | W5–W7 | G3a | Labels published, auto-labelling pilot, encryption configured |
| 4 | Data loss prevention | W6–W10 | G3 | DLP in enforce mode across email, collaboration, endpoint, browser |
| 5 | Insider risk & comms compliance | W9–W11 | G4 | One IRM policy live, triage playbook, comms compliance scoped |
| 6 | Lifecycle & records | W10–W12 | G4 | Retention labels, file plan, disposition review |
| 7 | eDiscovery & audit assurance | W11–W13 | G4 | Case process, legal-hold playbook, audit search tested |
| 8 | Data Map (conditional) | W12+ | — | Catalogue scope decision; execute only if in scope |
| 9 | Operate & hand over | W13–W14 | G5 | Monitoring, tuning cadence, runbooks, closeout |

Phases 3–7 overlap deliberately: label taxonomy must exist before DLP can reference it, and retention work can run in parallel with endpoint DLP pilots. Overlap is only allowed where the dependency table below permits it.

## Dependency chain

```
PS-003 licensing ─┬─> PS-020 taxonomy ─> PS-021 label set ─┬─> PS-030/031 auto-label
                  │                                        └─> PS-040 DLP design ─> PS-041..045
PS-010 readiness ─┴─> PS-011 audit ─> PS-072 audit playbook
PS-013 baseline ──> PS-060 retention ─> PS-061 disposition
PS-023 roles ─────> all configuration items (authorization)
PS-015 endpoint ──> PS-042 endpoint DLP
```

## Milestones

| ID | Milestone | Target | Owner | Status |
| --- | --- | --- | --- | --- |
| M0 | Trackers published, repo initialized | 2026-11-25 | Purview Lead | Done |
| M1 | Readiness evidence pack accepted at G1 | 2026-12-09 | Purview Lead | In progress |
| M2 | Label taxonomy approved, pilot labels published | 2026-12-23 | Compliance | Not started |
| M3 | DLP simulation clean for 10 business days | 2027-01-20 | Security | Not started |
| M4 | DLP enforcement approved | 2027-01-27 | Sponsor | Not started |
| M5 | IRM + eDiscovery playbooks operating | 2027-02-17 | Security / Legal | Not started |
| M6 | Handover accepted | 2027-03-03 | IT Ops | Not started |

## Critical path

`PS-003 → PS-020 → PS-021 → PS-030/031 → PS-041 → PS-045 → PS-090 → PS-092`

Licensing confirmation (`PS-003`) is the single largest schedule risk: every design decision downstream depends on which Purview capabilities are entitled. If it is not confirmed by 2026-12-02, cut Phase 5 and Phase 6 scope to licensed-only and hold M5.

## Re-plan triggers

- Licensing shortfall affecting more than two phases.
- Sponsor rejects the label taxonomy at G2.
- DLP simulation produces more than 200 false positives per week after tuning.
- Legal cannot supply the retention schedule by W6.
