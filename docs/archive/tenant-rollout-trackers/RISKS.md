# Risk Register

Scoring: likelihood (1–5) × impact (1–5). Above tolerance if score ≥ 12, or impact = 5. Reviewed bi-weekly; closed risks stay in the table with a closure note for audit history.

| ID | Risk | L | I | Score | Owner | Mitigation | Trigger / early warning | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| R-01 | Licensing shortfall: tenant lacks E5/A5-class entitlements for Insider Risk, eDiscovery Premium, Audit Premium or advanced auto-labelling, invalidating the design | 4 | 5 | 20 | Sponsor | Confirm SKUs in `PS-003` before design freeze; maintain a licensed-only fallback design; log each unlicensed capability as deferred scope | SKU report shows no E5/A5 and no standalone Purview add-on | Open — above tolerance |
| R-02 | Label taxonomy churns after deployment, forcing relabelling and policy rework | 3 | 4 | 12 | Compliance | Freeze taxonomy v1 with sponsor + legal sign-off; treat changes as versioned migrations; pilot before broad publish | More than 2 change requests to taxonomy in a week | Open |
| R-03 | Endpoint DLP pilot disrupts business workflows or blocks legitimate line-of-business apps | 3 | 4 | 12 | IT Ops | 10-device pilot, audit-only first, documented override path, rollback tested before enforcement | Pilot users report blocked work; override rate > 10% | Open |
| R-04 | DLP false positives cause alert fatigue and enforcement is rejected by the business | 4 | 4 | 16 | Security | 10-business-day simulation, tune thresholds, per-rule precision sample, staged enforcement by location | > 200 matches/week or > 5% false-positive sample | Open — above tolerance |
| R-05 | Audit log retention or ingestion misconfigured, leaving compliance evidence gaps | 2 | 5 | 10 | Security | Verify ingestion on day 1 (`T-001`); baseline retention vs legal minimum; quarterly attestation | Ingestion disabled, or retention below legal period | Open |
| R-06 | Purview Data Map (governance) cost and scope creep | 3 | 3 | 9 | Sponsor | Explicit in/out decision (`D-006`); cost model before registering any source; defer by default | Requests to "just scan" additional sources | Open — conditional |
| R-07 | Excessive privileged roles (standing Global Admin, broad Compliance Admin) | 3 | 4 | 12 | Security | Least-privilege role model per RACI; PIM/JIT where available; quarterly access review | > 5 standing Global Admins; role groups without named owners | Open |
| R-08 | Insider Risk / Communication Compliance deployment blocked by HR, works council or legal | 2 | 4 | 8 | Security / Legal | Engage legal and HR before enabling indicators; document purpose limitation and anonymization settings | No written sign-off by W8 | Open |
| R-09 | Change fatigue and low label adoption by end users | 3 | 3 | 9 | Change Mgmt | Default labels, training, comms plan `PS-033`, champion network | Label application rate < 40% two weeks after publish | Open |
| R-10 | Key-person dependency on the Purview Lead for configuration and reporting | 2 | 4 | 8 | Sponsor | Runbooks written for every procedure; ops shadowing from Phase 2; handover test `PS-092` | No second person able to execute runbooks by W10 | Open |
| R-11 | Tenant configuration drift after handover (policies edited outside change control) | 3 | 3 | 9 | IT Ops | Baseline exports retained, monthly drift check against `assessments/`, tuning log `PS-091` | Unexplained policy change detected in review | Open |
| R-12 | Assessment evidence contains sensitive tenant identifiers and is committed accidentally | 2 | 4 | 8 | Purview Lead | `assessments/` git-ignored; evidence stored in the compliance evidence store, not the repo; pre-commit review | `git status` shows assessment output staged | Open |

## Watch list (not yet scored)

- Regulatory change altering retention periods mid-programme.
- Microsoft service changes to Purview licensing or portal (classic compliance portal retirement) invalidating runbook screenshots.
- eDiscovery Premium capacity limits during a large legal case.

## Closed

_None yet._
