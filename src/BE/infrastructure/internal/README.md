# Internal deployment helpers

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** the three supported entry points in the parent directory  
**Review cadence:** whenever deployment entry points or helper boundaries change  
**Linear:** WOR-190

Files in this directory are implementation details invoked by `deploy.ps1`, `deploy-entra.ps1` or `deploy-infrastructure.ps1`.

Do not run them as operator deployment entry points and do not reference them directly from CI/CD workflows.
