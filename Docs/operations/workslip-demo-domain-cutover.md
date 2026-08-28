# Workslip demo custom-domain cutover

This runbook covers only the live DNS/TLS cutover for the isolated Workslip demo described in `workslip-demo-container-apps.md`.

## Target

- public hostname: `demo.mrsoftware.dk`
- Container App: `ca-workslip-demo`
- Container Apps environment: `cae-workslip-demo`
- resource group: `rg-mrsoftware-demo`

The application/database boundary is unchanged. This procedure never points the demo at production resources.

## Automation

`.github/workflows/demo-domain-cutover.yml` runs automatically after a successful **Deploy Workslip Demo** workflow and can also be run manually.

The workflow:

1. resolves the actual Container App FQDN;
2. resolves Azure's current domain-verification ID;
3. prints the exact Cloudflare CNAME/TXT values in the Actions summary;
4. verifies public DNS;
5. refuses certificate cutover when a restrictive CAA policy does not authorize DigiCert;
6. binds `demo.mrsoftware.dk` to the Container App when DNS is ready;
7. provisions/binds an Azure managed certificate using CNAME validation;
8. smoke-tests `/login`, `/api/demo/token` and authenticated `/api/auth/me` over the custom HTTPS hostname;
9. verifies the production app does not expose a successful demo-token endpoint.

If DNS is not ready, the workflow exits successfully without mutating the hostname/certificate and leaves the required values in the job summary. Re-run it after DNS propagation.

## Cloudflare records

Use the values printed by the workflow summary. The record shape is:

| Type | Name | Value | Proxy |
| --- | --- | --- | --- |
| CNAME | `demo` | Container App generated FQDN | **DNS only** |
| TXT | `asuid.demo` | Azure Container Apps verification ID | n/a |

Do not enable the Cloudflare proxy for the CNAME while using the Azure managed certificate. Azure requires a subdomain CNAME to map directly to the generated Container Apps hostname for certificate issuance and renewal.

If the root `mrsoftware.dk` zone has CAA records, DigiCert must be permitted because Azure Container Apps managed certificates use DigiCert.

## Routing activation

Do not mark the cross-site `Ægte demo` destination as live until the cutover workflow reports all custom-hostname smoke tests as passed.

After that verification:

- set the marketing-site routing flag `demo_live` to `true`;
- enable the matching Shopify cross-site link;
- retain `app.mrsoftware.dk` as the production login/workspace destination.

## Rollback

A DNS rollback is simply removal/reversion of the `demo` CNAME. Do not redirect `demo.mrsoftware.dk` to production Workslip as a fallback.

The Azure custom hostname/certificate can remain provisioned during a DNS rollback; it is harmless while DNS no longer resolves to the Container App. If permanently retiring the demo, delete the custom hostname/certificate and then the isolated demo resource group according to the main demo runbook.
