# Status Board

Single weekly report. Update the snapshot, the phase table and the "next actions" every week; add a row to the update log.

## Snapshot

| Field | Value |
| --- | --- |
| Report date | 2026-11-25 |
| Phase | 1 — Assess & Baseline |
| Gate in view | G1 — Baseline approved (target 2026-12-09) |
| Health | **Amber** — scope confirmed, licensing unconfirmed |
| Work items | 45 total · 1 done · 2 in progress · 1 blocked · 5 next · 36 backlog |
| Risks above tolerance | R-01 (licensing), R-04 (DLP over-blocking) |
| Decisions pending | D-006 (Data Map in/out), D-007 (retention schedule source) |

## Phase progress

| Phase | Status | Done / Total | Gate | Comment |
| --- | --- | --- | --- | --- |
| 0 Mobilize | In progress | 1 / 5 | G0 | Charter drafted; sponsor sign-off outstanding (`PS-002`) |
| 1 Assess & baseline | In progress | 1 / 7 | G1 | Readiness script delivered; assessment not yet executed against tenant |
| 2 Foundation | Not started | 0 / 6 | G2 | Blocked by taxonomy approval and licensing |
| 3 Information protection | Not started | 0 / 4 | G3a | — |
| 4 DLP | Not started | 0 / 6 | G3 | — |
| 5 IRM & comms compliance | Not started | 0 / 4 | G4 | Licensing-dependent |
| 6 Lifecycle & records | Not started | 0 / 3 | G4 | Legal retention schedule required |
| 7 eDiscovery & audit | Not started | 0 / 3 | G4 | — |
| 8 Data Map | Not started | 0 / 3 | — | Conditional; decision `D-006` |
| 9 Operate & handover | Not started | 0 / 4 | G5 | — |

## In flight

| ID | Task | Owner | Started | Next step |
| --- | --- | --- | --- | --- |
| PS-002 | RACI sign-off | PL | 2026-11-25 | Send RACI + charter to sponsor for approval |
| PS-010 | Tenant readiness assessment | PL | 2026-11-25 | Run `Get-PurviewReadiness.ps1` in a change window (read-only) and attach output |

## Blocked

| ID | Task | Blocked by | Owner | Escalation |
| --- | --- | --- | --- | --- |
| PS-003 | Licensing entitlement validation | Needs SKU export + sponsor confirmation; assessment evidence not yet produced | PL | Sponsor, at G1 review |

## Next actions (ordered)

1. Sponsor approves charter + RACI → closes `PS-002`, unlocks `PS-005`.
2. Execute readiness assessment → `PS-010`, produces the SKU list that unblocks `PS-003`.
3. Confirm in/out scope for Data Map, Priva, DSPM for AI → `PS-004`, `D-006` stays open until then.
4. Build baseline exports of existing labels, DLP and retention → `PS-013`.
5. Draft sensitivity taxonomy v1 → `PS-020` (start once licensing is clear).

## Blockers and decisions needed

| Item | Needed from | By when | Impact if late |
| --- | --- | --- | --- |
| Licensing confirmation | Sponsor / procurement | 2026-12-02 | Phases 5–6 reduced to licensed-only; M5 at risk |
| Retention schedule | Legal | 2026-12-16 | `PS-024`, `PS-060` slip; G2 partial |
| Pilot group list (≈50 users) | IT Ops | 2026-12-09 | Label and DLP pilots cannot start |

## Update log

| Date | Author | Change |
| --- | --- | --- |
| 2026-11-25 | Purview Lead | Board created; Phase 0 tracker work complete; Phase 1 opened; readiness script delivered |
