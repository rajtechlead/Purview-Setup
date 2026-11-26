# 07 — Requirements traceability

This is an index, not a second status tracker. [08 — Requirements Tracker](08-requirements-tracker.md) is the authoritative requirement-level record with status, proof code and evidence; [09 — Deliverables Tracker](09-deliverables-tracker.md) covers the 22 named artifacts.

| Specification area | Tracker section | Primary implementation | Tests |
| --- | --- | --- | --- |
| Platform, packaging and scope | A, T | `app.manifest`, `PlatformSupport`, `installer/` | `PlatformSupportTests`, MSI/MSIX validation |
| Authentication and certificates | B | `EntraTokenProvider`, `WindowsCertificateService`, `GraphAuthenticationProvisioner`, `KeyVaultCertificateStore` | `ProvisioningTests` |
| Discovery and assessment | C | `GraphTenantCollector`, `CompliancePowerShellCollector`, `ScoringEngine`, `ModuleCatalog` | `ScoringAndModuleTests`, `WorkloadReportCsvTests` |
| Deployment modes and change control | D | `DeploymentModePolicy`, `ChangePlanBuilder`, `PhasedDeploymentService`, `PowerShellDeploymentExecutor` | `DeploymentModePolicyTests`, `PhasedDeploymentTests`, `DeploymentScriptTests` |
| Modular architecture and plugins | E | `IModule`, `ModuleRegistry`, `PluginModuleLoader` | `PluginModuleTests` |
| AI assistant, failover and privacy | F, G | `CopilotService`, `AiFailoverOrchestrator`, `SensitiveDataDetector`, `RedactionService` | `PrivacyTests`, `ProviderSequenceTests`, `AiAndPersistenceTests` |
| Change safety, approvals and backups | H, K | `ChangePlanBuilder`, `BackupService`, `ObjectBackupService`, `ApprovalWorkflow`, `DeploymentValidation` | `PhasedDeploymentTests`, `ObjectBackupTests`, `WorkflowTests` |
| MSP mode and configuration-as-code | I, J | `TenantProfile`, `TenantAccessPolicy`, `TenantPortfolio`, `ConfigurationPackage`, `InfrastructureTemplates` | `TenantAccessTests`, `TenantProfileTests`, `ConfigurationExportTests` |
| Offline, national clouds and licensing | L, M | `CloudProfiles`, `LicenseOptimizer` | `CloudProfileTests`, `LicensePriceTests` |
| Monitoring and reporting | N, O | `DriftDetector`, `AlertBuilder`, `TrendReport`, report exporters | `MonitoringAlertTests`, `TrendReportTests`, `OfficeReportTests`, `ReportTypeTests` |
| Security engineering and architecture | P, Q | `SECURITY.md`, `docs/10`, `AuditLog`, `SqliteStore`, `DpapiSecretProtector` | `AuditChainAndHealthTests`, `PayloadEncryptionTests` |
| UX, help and About | R, S | `ViewModels/`, `Views/`, `HelpViewModel`, `AboutViewModel` | `DashboardAnalyticsTests`, `FindingActionTests` |

Open items and their reasons are listed only in [08](08-requirements-tracker.md); do not copy statuses into this page.
