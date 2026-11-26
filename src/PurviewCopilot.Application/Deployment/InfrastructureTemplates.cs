namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Infrastructure-as-code for the Azure Purview account. Purview compliance objects (labels,
/// DLP, retention) are not ARM resources; they travel through the JSON/PowerShell export instead.
/// </summary>
public static class InfrastructureTemplates
{
    public const string Arm = """
        {
          "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
          "contentVersion": "1.0.0.0",
          "metadata": {
            "description": "Purview account only. Purview compliance configuration (labels, DLP, retention) is not ARM-deployable; use the JSON/PowerShell export."
          },
          "parameters": {
            "purviewAccountName": { "type": "string", "defaultValue": "purview-account" },
            "location": { "type": "string", "defaultValue": "[resourceGroup().location]" }
          },
          "resources": [
            {
              "type": "Microsoft.Purview/accounts",
              "apiVersion": "2021-07-01",
              "name": "[parameters('purviewAccountName')]",
              "location": "[parameters('location')]",
              "identity": { "type": "SystemAssigned" },
              "properties": { "publicNetworkAccess": "Enabled" }
            }
          ],
          "outputs": {
            "purviewAccountId": {
              "type": "string",
              "value": "[resourceId('Microsoft.Purview/accounts', parameters('purviewAccountName'))]"
            }
          }
        }
        """;

    public const string Bicep = """
        // Purview account only. Compliance objects are exported as JSON/PowerShell, not ARM.
        param purviewAccountName string = 'purview-account'
        param location string = resourceGroup().location

        resource purview 'Microsoft.Purview/accounts@2021-07-01' = {
          name: purviewAccountName
          location: location
          identity: { type: 'SystemAssigned' }
          properties: { publicNetworkAccess: 'Enabled' }
        }

        output purviewAccountId string = purview.id
        """;

    public const string Terraform = """
        # Purview account only. Compliance objects are exported as JSON/PowerShell, not Terraform.
        terraform {
          required_providers {
            azurerm = {
              source  = "hashicorp/azurerm"
              version = "~> 4.0"
            }
          }
        }

        variable "resource_group_name" { type = string }
        variable "location" { type = string }
        variable "purview_account_name" {
          type    = string
          default = "purview-account"
        }

        resource "azurerm_purview_account" "this" {
          name                = var.purview_account_name
          resource_group_name = var.resource_group_name
          location            = var.location
          identity { type = "SystemAssigned" }
        }

        output "purview_account_id" {
          value = azurerm_purview_account.this.id
        }
        """;
}
