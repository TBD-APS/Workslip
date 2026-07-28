# WOR-190 rollout note: optional Vercel token

`Vercel:Token` is consumed only by the optional administrator-triggered Vercel CDN cache purge. The API skips that external purge when either the token or project ID is absent.

The current `deploy-infrastructure.ps1` implementation incorrectly blocks the entire infrastructure deployment when it cannot find an existing `Vercel--Token`, migrate a legacy plain App Configuration value, or read `WORKSLIP_VERCEL_TOKEN` / `-VercelToken`.

Required correction before WOR-190 is complete:

- preserve and migrate an existing token when present;
- accept explicit token input for setup or rotation;
- continue with a warning when no token is configured;
- leave `Vercel:Token` absent when the optional integration is disabled;
- never create a Key Vault reference to a missing secret;
- never blank an existing token when input is omitted.

Do not use a dummy token to bypass the rollout check. A dummy value would be persisted as a production secret and make cache-purge requests fail.
