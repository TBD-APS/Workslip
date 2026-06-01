using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Mappers;

public static class JobReportMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JobReportResponse ToResponse(
        JobReportRow row,
        CustomerRow? customer,
        IReadOnlyList<JobLinkInfoResponse> links,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        IReadOnlyList<WorksheetUserGroupResponse> worksheetEntries,
        IReadOnlyList<InstallationTypeResponse> installationTypes,
        decimal? totalHours = null)
    {
        return new(
            row.Id, row.OrganizationId,
            customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
            row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            installationTypes, row.WorkKind, row.CustomWorkKind,
            row.Remarks, FromJsonList(row.ClosureFlagsJson), links,
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            assignedUsers, worksheetEntries,
            row.IsSoftDeleted, row.DeletionScheduledAt, totalHours);
    }

    public static JobEventResponse ToEventResponse(JobEventRow row) => new(
        row.Id, row.ReportId, row.ActorId, row.EventType,
        ToJsonObject(row.BeforeJson), ToJsonObject(row.AfterJson), row.CreatedAt);

    public static async Task<IReadOnlyList<InstallationTypeResponse>> LoadInstallationTypesAsync(
        this SqlDbContext dbContext, Guid organizationId, Guid jobReportId, CancellationToken cancellationToken)
    {
        var installations = await dbContext.JobReportInstallations
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && i.JobReportId == jobReportId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new
            {
                DefId = i.InstallationTypeDefinition.Id,
                DefName = i.InstallationTypeDefinition.Name,
                i.SortOrder,
                Categories = i.Categories
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new
                    {
                        CatId = c.ControlCategory.Id,
                        CatName = c.ControlCategory.Name,
                        c.SortOrder,
                        ControlPoints = c.ControlPoints
                            .OrderBy(cp => cp.SortOrder)
                            .Select(cp => new
                            {
                                cp.ControlPoint.Id,
                                cp.ControlPoint.Name,
                                cp.ControlPoint.Description,
                                cp.SortOrder,
                                cp.IsRequired,
                                cp.IsChecked
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return installations.Select(i => new InstallationTypeResponse(
            i.DefId, i.DefName, null, i.SortOrder,
            i.Categories.Select(c => new InstallationTypeCategoryResponse(
                c.CatId, c.CatName, c.SortOrder,
                c.ControlPoints.Select(cp => new InstallationTypeControlPointResponse(
                    cp.Id, cp.Name, cp.Description, cp.SortOrder, cp.IsRequired, cp.IsChecked
                )).ToArray()
            )).ToArray()
        )).ToArray();
    }

    public static JobStatus ParseStatus(string status) => Enum.Parse<JobStatus>(status, ignoreCase: true);

    public static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    public static IReadOnlyList<string> FromJsonList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static JsonObject? ToJsonObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
}
