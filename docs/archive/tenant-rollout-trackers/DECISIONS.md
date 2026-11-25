# Decision Log

One row per decision that constrains future work. Superseded decisions are never edited — add a new row and mark the old one `Superseded by D-xxx`.

| ID | Date | Decision | Rationale | Decided by | Status | Affects |
| --- | --- | --- | --- | --- | --- | --- |
| D-001 | 2026-11-25 | Track work in-repo (Markdown trackers authoritative) and mirror items as GitHub issues; do not run a separate PM tool | One source of truth, reviewable by pull request, works for a small team | Purview Lead | Accepted | All |
| D-002 | 2026-11-25 | Assessment automation is read-only by default and writes evidence to a git-ignored `assessments/` folder | Prevents accidental tenant change and customer data in git | Purview Lead | Accepted | `scripts/`, `PS-010` |
| D-003 | 2026-11-25 | Use the built-in sensitive information types before authoring custom SITs | Faster, maintained by Microsoft, lower false-positive risk; custom SITs only when a documented gap exists | Purview Lead | Accepted | `PS-031`, `PS-040` |
| D-004 | 2026-11-25 | All DLP policies start in simulation and only move to enforce after 10 business days with false positives < 5% | Protects business continuity; produces an approval artefact for the sponsor | Sponsor (pending G3) | Proposed | `PS-041`, `PS-045` |
| D-005 | 2026-11-25 | No on-premises scanner or third-party connectors in v1 | Out of scope; adds infrastructure and licence cost without addressing the top data-egress risks | Sponsor (pending `PS-004`) | Proposed | Scope |
| D-006 | 2026-11-25 | Microsoft Purview Data Map is conditional and off by default | Governance catalogue is a separate programme with consumption-based cost; not needed for DLP/labels | Sponsor | Proposed — decision due at `PS-004` | Phase 8 |
| D-007 | 2026-11-25 | Retention periods are sourced from Legal's retention schedule, not invented by IT | Legal defensibility; avoids rework | Legal | Proposed | `PS-024`, `PS-060` |
| D-008 | 2026-11-25 | Label names are business-readable (`Confidential – Finance`), no numeric-only or acronym-only labels | Adoption depends on users understanding the choice | Compliance | Proposed | `PS-020`, `PS-025` |
| D-009 | 2026-11-25 | Role assignments follow the RACI and use Purview role groups, not direct mailbox-level permissions; standing Global Admin is limited to break-glass | Least privilege and auditability | Security | Proposed | `PS-023` |

## How to record a decision

```markdown
| D-0NN | YYYY-MM-DD | Decision in one sentence | Why, and what was rejected | Role | Proposed/Accepted/Superseded | IDs |
```

Decisions that change scope, licensing or enforcement require the Sponsor; technical design decisions require the Purview Lead.
