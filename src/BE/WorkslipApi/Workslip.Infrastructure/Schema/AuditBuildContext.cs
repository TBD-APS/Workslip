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
    public HashSet<Guid> ProcessedAssignmentReportIds { get; } = [];
    public HashSet<Guid> ProcessedClosureFlagReportIds { get; } = [];
    public HashSet<Guid> ProcessedInstallationIds { get; } = [];
    public HashSet<Guid> ProcessedLinkReportIds { get; } = [];
    public Dictionary<Guid, (Guid OrganizationId, string CurrentStatus, string OriginalStatus)?> ReportStateCache { get; } = [];
    public Dictionary<(Guid OrganizationId, Guid ReportId), bool> ReportHistoryExistsCache { get; } = [];
    public Dictionary<(Guid OrganizationId, Guid ReportId), string> ReportDisplayCache { get; } = [];
    public Dictionary<(Guid OrganizationId, Guid UserId), string> UserDisplayCache { get; } = [];
    public Dictionary<(Guid OrganizationId, Guid CustomerId), string> CustomerDisplayCache { get; } = [];
    public Dictionary<Guid, string> WorkKindDisplayCache { get; } = [];
    public Dictionary<(Guid OrganizationId, Guid InstallationTypeDefinitionId), string> InstallationTypeDisplayCache { get; } = [];
    public Dictionary<Guid, string> ControlCategoryDisplayCache { get; } = [];
    public Dictionary<Guid, string> ControlPointDisplayCache { get; } = [];
    public Dictionary<Guid, string> ClosureFlagDisplayCache { get; } = [];
}
