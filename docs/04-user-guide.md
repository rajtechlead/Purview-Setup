# 04 — User Guide

## 1. First run

The app opens on the **Executive Dashboard** with no tenant connected. Two ways to get data:

- **Assessment → Load sample tenant** — populates every screen with clearly-labelled demo data (tenant name ends in `(demo data)`). Use it to learn the workflow and to demo reports.
- **Assessment → Discover tenant** — requires connection settings and permissions (Admin Guide §3).

No data is sent anywhere during either action.

## 2. Executive Dashboard

- **Tenant card**: tenant name, assessment time, control count, maturity band, failures, warnings and recommendations.
- **Eight score cards**: Overall, Security, Governance, Compliance, Risk (lower is better), AI governance, Copilot readiness. Colour: green ≥70, amber ≥45, red below.
- **Module coverage**: every module with at least one assessed control, with a bar and numeric score.
- **Priority findings**: the twelve most severe open items with module, status and severity stripe.

Scores are reproducible: the same evidence always produces the same number. If a number looks wrong, open Assessment and filter by that module — the finding behind every score is visible with its evidence string.

## 3. Assessment

1. Choose **Discover tenant** (live) or **Load sample tenant**.
2. While running, the status line names the stage. Graph permissions that are missing do not abort the run; they appear as discovery notes on the dashboard and in the finding evidence.
3. Filter with the search box (control ID, title, module) and toggle **Gaps only**.
4. Read a finding as four parts: **ID**, **status** (Pass / Warning / Fail), **evidence** (what was observed) and **remediation** (what to do). Framework mappings are attached to each control and surface in Reports.
5. The right column lists the generated recommendations, highest severity first.

Assessment is read-only. Nothing in this page changes the tenant.

## 4. Deployment

This page turns recommendations into a controlled change.

1. Pick a **mode**: Assessment Only, Guided Deployment, One-Click Recommended, Custom, Expert, Greenfield.
2. Enter the **operator** name (recorded in the plan, approvals and audit log).
3. Select recommendations. High and critical items are preselected.
4. **Build change plan** → creates the plan, captures a pre-change backup and writes an audit entry.
5. Read the **change-management pack**: executive summary, technical summary, business/user/security/compliance/licensing impact, risks, validation steps and rollback steps.
6. **Simulate (what-if)** → validates every operation payload and produces the validation log. A plan with an empty payload fails simulation.
7. **Submit for approval** → state becomes `PendingSecurity`.
8. Approve through three stages: **Security → Compliance → CAB**. Enter a comment for each decision. **Reject** ends the request; **Request changes** returns it to draft.
9. Applying requires an approved state. In this build the executor is disabled: it prepares the exact PowerShell script for a change window rather than executing it. The UI states this explicitly.

Rollback: every plan has a stored backup (`backups` table) containing the snapshot and operations. The rollback path validates the package and lists the restore steps.

## 5. Monitoring

- **Drift since previous collection**: compares this assessment with the previous one. Detects audit disabled, retention shortened, labels/retention labels removed, DLP enforcement reduced, endpoint DLP disabled, onboarding coverage drop, new Global Admins, lost conditional access policies, new app secrets and secure-score drops. Higher severity means a control that protects data regressed.
- **Licence utilisation**: assigned vs enabled per SKU.
- **Optimisation insights**: unused capacity, over-assignment, E5 capability gap, Copilot prerequisites, Copilot-before-labelling and SKU sprawl.

Drift is computed on demand from the session's previous snapshot; there is no background scheduler.

## 6. Reports

1. Set **Organisation** (appears on the report).
2. Choose **Export HTML** (self-contained, print-ready — use browser Print → Save as PDF for a PDF), **Export JSON**, **Export CSV** or **Export Markdown**.
3. The framework table shows coverage per framework and the failing controls behind each gap.

Reports contain scores, module table, framework coverage, every finding and the top recommendations. HTML has no scripts, no external CSS and escapes all tenant-derived text.

## 7. AI Assistant

1. Type a question. Example prompts that work well:
   - "What are the three highest-risk Purview gaps, and what should I fix first?"
   - "Draft an executive summary of this assessment for a non-technical sponsor."
   - "Explain why DLP enforcement matters and what simulation period we should require."
2. **Prepare sanitized prompt** — runs the local privacy guard, shows the exact text that would be sent and lists what was sanitized.
3. Review it. If a value should not be there, edit the question and prepare again.
4. **Send** (or **Prepare and send**). The answer names the provider and token/cost usage.

Blocked prompts never leave the machine. In Block mode, secrets, credentials and tenant identifiers cause a refusal; three or more matches of any kind also block. If cloud AI is disabled or offline mode is on, only local providers (Ollama, LM Studio, Open WebUI) are attempted.

## 8. Settings

- **Connection** — auth mode, tenant ID, client ID, thumbprint, optional lab secret, **Test connection**, **Save settings**.
- **Certificate lifecycle** — subject and validity, **Create**, **Import PFX**, **Export PFX**, **Rotate**, **Remove**, password field for PFX operations, plus the list of app certificates and a 30-day expiry warning.
- **AI privacy guardrail** — mode (Redact/Mask/Tokenize/Block), allow cloud AI, offline mode, require review before send.
- **AI providers** — enable/disable each provider. Keys are read from the environment variables named in the provider's key reference (for example `OPENAI_API_KEY`); they are never stored in the app database.

## 9. Help Center and About

Help Center contains twelve searchable, beginner-friendly topics: Purview overview, sensitivity labels, DLP, endpoint DLP, DSPM and DSPM for AI, eDiscovery, insider risk, AI governance, Copilot governance, Compliance Manager, how scoring works, and the AI privacy guard. About shows authorship, owner, contact and supported platforms.

## 10. Keyboard and accessibility notes

- All interactive controls are reachable by Tab; lists and the navigation rail use standard WPF focus behaviour.
- Colour is never the only signal: scores carry numeric values, statuses carry words, and severities carry labels.
- Text scales with Windows display scaling (per-monitor v2 DPI awareness).
