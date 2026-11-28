# 11 — Collaboration service

> **Client status.** `PurviewCopilot.Collaboration.CollaborationClient` is the supported typed client
> for this API and is verified against the running service by `CollaborationClientTests`. The WPF
> screens are not bound to it yet, so shared mode is currently available to the CLI and tests only;
> desktop sign-in still requires `IDN-01`.

## Approval quorum

The service enforces distinct approvers per stage from configuration, validated at startup:

```json
"Collaboration": {
  "Approval": { "Security": 2, "Compliance": 2, "Cab": 1 }
}
```

A value outside 1-9 stops the service instead of weakening the rule. The desktop app keeps the same
policy in its own settings, so local and shared approvals behave identically.

## Schema management

The store ships a committed EF Core migration (`InitialCreate`) and applies it at startup with
`MigrateAsync`; a fresh SQLite file is created entirely by migrations, which the 43 service tests
exercise. A database created by a pre-migration build must be recreated — no such build has shipped.
SQL Server uses the same migration, but has not been run against a real instance. An existing
single-node SQLite database is moved with `pgc store-copy` (see [Migrating an existing store](#migrating-an-existing-store)).

A customer-hosted Windows service/API that lets several operators share assessments, change plans,
approvals and audit for the same tenant. It is optional: the desktop app still runs fully offline on
local SQLite. The service is never a SaaS and contains no vendor-hosted component.

Implemented and tested against SQLite; SQL Server is supported by configuration but has not been
exercised against a real instance. There is no live Entra token test in this repository. The status
of every claim is tracked in [the dashboard](project/trackers/00-dashboard.md).

## Architecture

| Component | Project | Responsibility |
| --- | --- | --- |
| Domain | `src/PurviewCopilot.Domain` | Plans, approval records, scores |
| Application | `src/PurviewCopilot.Application` | Approval state machine, plan hashing |
| Collaboration | `src/PurviewCopilot.Collaboration` | EF Core store, seven-role matrix, tenant authorisation, audit chain |
| Service | `src/PurviewCopilot.Service` | ASP.NET Core host, JWT authentication, tenant-scoped endpoints |

```
desktop clients ──HTTPS + Entra token──► service ──► SQL Server / SQLite
                                            │
                                            └── shared approvals, audit chain, tenant membership
```

The store filters every query by tenant id. Authorisation is resolved from the database on every
request, not from a token claim and not from a hidden UI control.

## Configuration

Configuration is read from standard ASP.NET Core sources: `appsettings.json`,
`appsettings.{Environment}.json`, environment variables and command-line arguments.

| Key | Default | Meaning |
| --- | --- | --- |
| `ASPNETCORE_URLS` | — | Must include an `https://` URL in production |
| `Collaboration:Provider` | `Sqlite` | `Sqlite` for a single node, `SqlServer` for a shared store |
| `ConnectionStrings:Collaboration` | empty | Required for SQL Server; for SQLite an empty value uses `Collaboration:DataDirectory` |
| `Collaboration:DataDirectory` | `%PROGRAMDATA%\PurviewGovernanceCopilot` | Location of `collaboration.db` |
| `Collaboration:MaxPayloadBytes` | `1048576` | Rejects oversized plan and assessment payloads |
| `Collaboration:MaxOperations` | `500` | Maximum operations in one plan |
| `Collaboration:MaxPageSize` | `200` | Maximum page size for list endpoints |
| `AzureAd:Authority` | empty | Entra authority, e.g. `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `AzureAd:Audience` | empty | Application ID URI of the API, e.g. `api://purview-copilot-collab` |
| `Service:RequireHttps` | `true` | Rejects non-HTTPS requests; only disable in an isolated lab |
| `Service:BootstrapTenant` | empty | First-run tenant that receives the bootstrap administrator |
| `Service:BootstrapAdmin` | empty | Object id or UPN of the first Application Administrator |

**Fail-closed startup.** Outside the `Testing` environment the host refuses to start when
`AzureAd:Authority`/`Audience` are missing, when HTTPS is required but no `https://` URL is
configured, or when `SqlServer` is selected without a connection string.

## Entra registration

1. Create an app registration for the service API and expose a scope (for example
   `access_as_user`) under the Application ID URI you use as `AzureAd:Audience`.
2. Define app roles `Viewer`, `Analyst`, `Designer`, `Operator`, `Approver`, `Auditor` and
   `ApplicationAdministrator`. Token roles are informational: **authorisation comes from the
   membership table**, so an Entra role alone grants nothing.
3. Grant the desktop client permission to request that scope. The client integration is not built
   yet (tracked as SRV-07), so today the API is exercised by tests and by direct HTTPS calls.
4. Set `AzureAd:Authority` and `AzureAd:Audience` on the service host.

The API validates issuer, audience, lifetime and signing key, with a two-minute clock skew. It never
accepts an unsigned or locally minted token outside the test host.

## Roles and permissions

| Role | Read | Create assessment | Create plan | Start approval | Decide approval | Read audit | Manage members |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Viewer | ✔ | | | | | | |
| Analyst | ✔ | ✔ | | | | | |
| Designer | ✔ | | ✔ | | | | |
| Operator | ✔ | | | ✔ | | | |
| Approver | ✔ | | | | ✔ | | |
| Auditor | ✔ | | | | | ✔ | |
| Application Administrator | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

No role inherits another. A user may hold a different role on each tenant.

## Endpoints

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/health` | anonymous |
| GET | `/api/me` | authenticated |
| GET | `/api/tenants/{tenantId}/assessments` | Read |
| POST | `/api/tenants/{tenantId}/assessments` | CreateAssessment |
| GET | `/api/tenants/{tenantId}/plans` | Read |
| POST | `/api/tenants/{tenantId}/plans` | CreatePlan |
| GET | `/api/tenants/{tenantId}/plans/{planId}/approval` | Read |
| POST | `/api/tenants/{tenantId}/plans/{planId}/approval/start` | StartApproval |
| POST | `/api/tenants/{tenantId}/plans/{planId}/approval/decision` | DecideApproval |
| GET | `/api/tenants/{tenantId}/audit` | ReadAudit |
| GET | `/api/tenants/{tenantId}/audit/verify` | ReadAudit |
| GET | `/api/tenants/{tenantId}/members` | ManageMembers |
| POST | `/api/tenants/{tenantId}/members` | ManageMembers |
| DELETE | `/api/tenants/{tenantId}/members/{userId}` | ManageMembers |

Status codes are deliberate: **401** unauthenticated, **403** no membership or insufficient role,
**404** missing record for a tenant you can access, **409** stale concurrency stamp, expired approval,
changed plan or self-approval, **400** validation failure, **500** without internal detail.

The plan payload's `tenantId` is ignored and replaced from the route, so a client cannot publish into
another tenant. The stored plan is re-read before every decision and re-hashed, so editing it after
approval starts invalidates the approval.

## First run

On an empty store, configure `Service:BootstrapTenant` and `Service:BootstrapAdmin`, start the
service once, confirm the audit entry, then remove both settings. The service logs a warning while
the bootstrap setting remains.

```powershell
$env:AzureAd__Authority = 'https://login.microsoftonline.com/<tenant-id>/v2.0'
$env:AzureAd__Audience  = 'api://purview-copilot-collab'
$env:ASPNETCORE_URLS    = 'https://localhost:8443'
$env:Service__BootstrapTenant = 'contoso.onmicrosoft.com'
$env:Service__BootstrapAdmin  = '<object-id-or-upn>'
dotnet run --project src/PurviewCopilot.Service
```

## SQL Server

```powershell
$env:Collaboration__Provider = 'SqlServer'
$env:ConnectionStrings__Collaboration = 'Server=sql01;Database=PurviewCopilot;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default'
```

The schema is created on first start from the committed `InitialCreate` migration. Add a new
migration for later schema changes and apply it with `MigrateAsync`; do not edit the shipped one.

## Migrating an existing store

```powershell
pgc store-copy --source "$env:LOCALAPPDATA\PurviewGovernanceCopilot\collaboration.db" `
               --target "Server=sql01;Database=PurviewCopilot;Encrypt=True;Authentication=Active Directory Default" `
               --target-provider SqlServer
```

The tool migrates both stores, then copies memberships, assessments, plans, approvals and the
audit chain in order, preserving GUID identifiers, plan/approval links and audit sequence numbers.
It refuses a non-empty target or the same database: merging stores would drop memberships or fork
the audit chain. Point the service at the target afterwards and stop using the single-node file.

## Windows service installation

`installer/Install-CollaborationService.ps1` publishes the service, creates the ProgramData
directory, registers the Windows service, configures restart-on-failure and starts it. It supports
`-WhatIf`. It does not write secrets: set the connection string, authority and audience as machine
environment variables first.

```powershell
dotnet publish src/PurviewCopilot.Service -c Release -r win-x64 --self-contained true -o artifacts/service
powershell -File installer/Install-CollaborationService.ps1 -PublishDir artifacts/service `
  -ServiceName PurviewGovernanceCopilotCollab -Port 8443
```

Kestrel needs a certificate for `https://`. Bind one with `netsh http add sslcert` on the service
port, or terminate TLS at a reverse proxy and keep `Service:RequireHttps=true` with forwarded headers
enabled. The script never installs a certificate for you.

## Verification

| Check | Result |
| --- | --- |
| Store tests (SQLite) | Role matrix, tenant isolation, durable approvals, expiry, tamper invalidation, concurrency, audit chain, provider fail-fast |
| Migration and copy tests | `MigrationTests`, `StoreCopyTests`: migrated schema, cross-store copy, link preservation, empty-target and same-store guards |
| API tests (`WebApplicationFactory`) | 16 pass: 401/403, cross-tenant denial, body tenant spoofing, role denial, full approval journey, self-approval, stale `If-Match`, audit verification, membership rules, `/api/me`, bootstrap admin, fail-closed startup |
| SQL Server | Not run — no instance available |
| Live Entra token | Not run — no tenant available |
| Desktop client integration | Typed client implemented and HTTP-tested; the WPF app does not call it yet (SRV-07) |
| Windows service install | Script provided; not executed on a clean Windows host here |

## Known gaps

- No SQL Server instance has been used to validate the provider or the copy tool end to end; both were exercised between SQLite files only.
- No desktop client wiring: the collaboration API is reachable and tested but the WPF app still
  stores locally.
- No rate limiting beyond Kestrel request-size limits.
- The audit sequence is serialised per tenant inside a serialisable transaction; very high write
  rates on SQL Server should be load-tested before adoption.
- Membership is looked up per request, which is correct but adds one query per call.
- No automated backup/restore of the shared database; use the database engine's tooling.
