using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Reporting;

public sealed class PowerBiWorksheetExportScopeResolver(SqlDbContext dbContext)
{
    public async Task<Guid?> ResolveOrganizationIdAsync(
        string readerEmail,
        string readerEntraObjectId,
        CancellationToken cancellationToken)
    {
        var organizationIds = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Email == readerEmail
                && user.EntraId == readerEntraObjectId
                && user.Role == Roles.Admin)
            .Select(user => user.OrganizationId)
            .Distinct()
            .Take(2)
            .ToArrayAsync(cancellationToken);

        return organizationIds.Length == 1 ? organizationIds[0] : null;
    }
}
