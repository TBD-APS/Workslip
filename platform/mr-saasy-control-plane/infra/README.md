# MR Sassy Azure Control Plane Infrastructure

This folder contains the infrastructure owned by MR Sassy.

## Ownership

Sassy owns Azure resources used for:

- agent runtime
- MCP services
- tool registry infrastructure
- secrets and configuration
- workload integrations

## Initial foundation

The first deployment creates:

- User Assigned Managed Identity
- Azure App Configuration
- Azure Key Vault with RBAC enabled

Future modules:

- MCP host runtime
- agent execution environment
- monitoring
- workload registration
