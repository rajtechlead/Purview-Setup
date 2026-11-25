# Test & Evidence Matrix

No work item is marked `Done` without a row here. Evidence is a redacted export, screenshot or report stored in the compliance evidence store (never in git) and referenced by filename.

**Result values:** `Pass` · `Fail` · `Not run` · `N/A`

| ID | Proves | Method | Expected result | Evidence | Result | Date |
| --- | --- | --- | --- | --- | --- | --- |
| T-001 | Unified audit ingestion is active (`PS-011`, `PS-022`) | `Get-AdminAuditLogConfig`; then perform a labelled action and retrieve it with `Search-UnifiedAuditLog -StartDate (Get-Date).AddHours(-1)` | `UnifiedAuditLogIngestionEnabled = True`; the test event is retrievable within 60 minutes | `audit-ingestion-*.json`, `audit-search-*.csv` | Not run | — |
| T-002 | Audit retention meets the legal minimum (`PS-022`) | `Get-UnifiedAuditLogRetentionPolicy`; compare each policy's duration to the legal requirement table | Every required record type has retention ≥ requirement | `audit-retention-*.json` | Not run | — |
| T-003 | Labels are published and visible to pilot users (`PS-021`) | Sign in as a pilot user in Word/Outlook/Web; inspect the sensitivity menu | All published labels appear with correct name, tooltip and colour; no unpublished label visible | `labels-pilot-*.png` | Not run | — |
| T-004 | Label encryption allows intended access and blocks unintended access (`PS-032`) | Share a protected file with an internal user, an external user and a non-member; attempt open/edit | Intended recipients succeed; others are denied or read-only as designed | `encryption-access-*.md` | Not run | — |
| T-005 | Auto-labelling precision is acceptable (`PS-031`) | Run rule in simulation; review a random 200-item sample | Precision ≥ 95%, or rule stays in simulation | `autolabel-sample-*.csv` | Not run | — |
| T-006 | DLP detects the targeted data in every in-scope location (`PS-041`) | Send/upload/copy test payloads (synthetic SIT data) from each location | Match generated with correct policy, rule, severity and user hint | `dlp-testmatrix-*.md` | Not run | — |
| T-007 | DLP has no material false positives (`PS-041`, `PS-045`) | 10-business-day simulation; daily review of matches | False-positive rate < 5%; no P1 business impact | `dlp-simulation-*.xlsx` | Not run | — |
| T-008 | Endpoint DLP controls fire and can be overridden (`PS-042`) | On pilot device: copy to USB, upload to personal cloud, print | Block/audit/override behaviour matches policy; override requires justification | `edlp-pilot-*.md` | Not run | — |
| T-009 | Role model is least privilege (`PS-023`) | Enumerate role groups and members; attempt an action outside the assigned role | No daily-use account holds Global Admin; unauthorized action fails | `roles-review-*.csv` | Not run | — |
| T-010 | Readiness assessment is reproducible (`PS-010`) | Run `Get-PurviewReadiness.ps1` twice on different days | Same schema, no unhandled errors, exit code 0 or documented 2 | `readiness-*.json` (2 runs) | Not run | — |
| T-011 | Insider Risk policy generates and routes alerts (`PS-051`) | Simulate a departing-user indicator in the pilot tenant scope; triage the alert | Alert created, routed to the named queue, disposition recorded | `irm-alert-*.md` | Not run | — |
| T-012 | eDiscovery search and hold preserve content (`PS-070`, `PS-071`) | Create test case, search a known item, apply hold, attempt deletion | Item found; deletion blocked or retained; export contains original | `ediscovery-*.md` | Not run | — |
| T-013 | Retention keeps and deletes as configured (`PS-060`) | Create test items with each retention label; advance time via test labels where available | Items retained for the configured period; disposition review triggers at end | `retention-*.md` | Not run | — |
| T-014 | Alerting and reporting are operational (`PS-090`) | Confirm alert policies fire to the ticketing queue; generate the weekly report | Alerts arrive within SLA; report renders with current data | `monitoring-*.png` | Not run | — |

## Gate evidence checklist

| Gate | Required rows passing |
| --- | --- |
| G1 | T-010, plus baseline exports attached |
| G2 | T-001, T-002, T-003, T-009, T-013 |
| G3 | T-005, T-006, T-007, T-008 |
| G4 | T-011, T-012, T-014 |
| G5 | T-014 plus handover sign-off (`PS-092`) |
