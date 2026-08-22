# Workslip self-hosted PostHog on Azure

**Linear:** WOR-736

This folder provisions an isolated Ubuntu VM in Azure Sweden Central for Workslip product analytics. It does not change the Workslip runtime or send any application events by itself.

## Why a VM

PostHog's current self-host documentation describes its free self-host deployment as a Docker Compose hobby deployment and recommends a Linux Ubuntu VM with roughly 4 vCPU, 16 GB RAM and more than 30 GB storage. The deployment is officially unsupported by PostHog and must therefore remain non-authoritative: Workslip customer records, documents and operational state stay in Workslip's own Azure SQL/Storage services.

Reference: https://posthog.com/docs/self-host

## Azure shape

Default deployment:

- Region: `swedencentral`
- Resource group: `rg-mrsoftwarev2-analytics-live`
- VM: `Standard_D4as_v5`
- Ubuntu 24.04 LTS
- 64 GiB OS disk
- Dedicated 128 GiB Standard SSD mounted at `/var/lib/docker`
- Static public IP with an Azure-managed DNS name
- HTTPS/HTTP open publicly for the PostHog UI and TLS issuance
- SSH restricted to the public IPv4 address of the operator running the deployment

No Postgres, ClickHouse, Redis or other PostHog backend ports are exposed publicly.

## Deploy the host

Make sure Azure CLI is pointing at the intended PAYG subscription first.

Preview:

```powershell
./deploy-posthog.ps1 `
  -WhatIf `
  -ExpectedTenantId "d700dfea-febb-4673-8587-fa4e57c66ad1" `
  -ExpectedSubscriptionId "103ca4bd-da5c-4713-ab24-1bade07f9e06"
```

Deploy:

```powershell
./deploy-posthog.ps1 `
  -ExpectedTenantId "d700dfea-febb-4673-8587-fa4e57c66ad1" `
  -ExpectedSubscriptionId "103ca4bd-da5c-4713-ab24-1bade07f9e06"
```

The script prints the VM FQDN and SSH command.

## Install PostHog

SSH to the FQDN printed by the deployment script. Wait for cloud-init to finish:

```bash
sudo cloud-init status --wait
mountpoint /var/lib/docker
```

Then use PostHog's current official self-host installer from their documentation. The current non-interactive form is:

```bash
cd /opt
curl -OfsSL https://github.com/PostHog/posthog/releases/download/hobby-latest/hobby-installer
chmod +x hobby-installer
sudo ./hobby-installer --ci --domain=<FQDN_FROM_DEPLOYMENT>
```

PostHog states that first boot, migrations and TLS issuance can take around 5-10 minutes.

## First Workslip integration

Do not enable broad capture or session replay by default. The first Workslip integration should only send an allow-listed event contract for WOR-736, with no customer names, addresses, document contents, free-text comments, email addresses, telephone numbers or authentication/session material.

Start with a deliberately small set of events such as:

- `customer_created`
- `case_created`
- `case_completed`
- `document_uploaded`
- `kls_started`
- `checkpoint_completed`
- `checkpoint_irrelevant`
- `validation_error`
- `help_opened`

Tenant/user identifiers must be pseudonymous or otherwise approved by the GDPR change gate before they are sent.

## Operations

PostHog self-hosting tracks the latest Docker image rather than normal tagged application releases. Treat upgrades as explicit operational work and validate the instance after upgrades.

The dedicated Docker data disk protects container data from routine OS disk replacement, but this first slice does not configure Azure Backup. Product analytics is therefore recoverable/secondary data, not a Workslip system of record.
