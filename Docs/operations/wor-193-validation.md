# WOR-193 combined small fixes validation

**State:** Active PR validation note  
**Owner:** Workslip engineering  
**Review:** Remove or archive after the linked PR is merged  
**Linear:** WOR-193, WOR-166, WOR-156, WOR-108

## Included changes

- Filled visual treatment for control-point subcategories.
- Manual, tenant-scoped clearing of individual invitation statuses.
- Backend-owned Danish role display names and role-specific badges in `Folk`.
- Customer edit back navigation returns to customer detail.
- Rejected jobs open on the first wizard step.
- Uncontrolled collapsible sections preserve their open state in browser history, covering assigned jobs under `Folk`.

## Regression protection

`InvitationStatusServiceTests` covers missing organization context, cross-tenant lookup rejection, Entra cleanup ordering, and accepted-invitation behavior.

## Additional verified bug fixed

`EfInviteRepository.GetInviteByEmailAsync` accepted an organization ID but previously ignored it in the query. Resending an invitation could therefore reuse a row from another organization when the email matched. The query now requires both organization and email.

## Validation constraints

The connected execution environment can read and write the repository through GitHub but cannot resolve GitHub from the local shell. It therefore cannot create a local worktree or execute the .NET and frontend toolchains. Build, test, lint, and browser validation must be confirmed by repository checks or a developer worktree before merge.

The generated frontend API client was reviewed but not hand-edited. The additive `roleDisplayName` field is consumed through a local type extension until the established OpenAPI generation process is next run.

Repomix was not regenerated because the established PowerShell process requires a repository worktree.
