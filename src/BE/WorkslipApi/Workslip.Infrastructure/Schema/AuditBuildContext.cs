using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;

namespace Workslip.Infrastructure.Schema;

internal sealed class AuditBuildContext(
    DbContext dbContext,
    ICurrentUserContext currentUser,
    AuditDisplayResolver displayResolver)
{
    public DbContext DbContext { get; } = dbContext;
    public ICurrentUserContext CurrentUser { get; } = currentUser;
    public AuditDisplayResolver DisplayResolver { get; } = displayResolver;
}
