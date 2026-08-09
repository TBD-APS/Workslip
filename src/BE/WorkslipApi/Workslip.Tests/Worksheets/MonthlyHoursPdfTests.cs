using System.Text;
using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure;

namespace Workslip.Tests.Worksheets;

public sealed class MonthlyHoursPdfTests
{
    [Fact]
    public async Task Service_scopes_monthly_pdf_data_to_current_organization()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new CapturingWorksheetRepository
        {
            Entries =
            [
                new MyWorksheetEntryResponse(
                    new DateOnly(2026, 8, 4),
                    Guid.NewGuid(),
                    userId,
                    "R-42",
                    "Kunde A",
                    null,
                    7.5m,
                    false,
                    "Alex Jensen")
            ]
        };
        var generator = new CapturingPdfGenerator();
        var service = CreateService(repository, generator, organizationId);

        var result = await service.GetAllWorksheetsPdfAsync(2026, 8, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationId, repository.RequestedOrganizationId);
        Assert.Equal(new DateOnly(2026, 8, 1), repository.RequestedMonthStart);
        Assert.Equal(new DateOnly(2026, 8, 31), repository.RequestedMonthEnd);
        Assert.Equal("workslip-timer-2026-08.pdf", result.Value.FileName);
        Assert.Equal([1, 2, 3], result.Value.Content);
        Assert.NotNull(generator.Month);
        Assert.Equal(7.5m, generator.Month.TotalHours);
    }

    [Fact]
    public async Task Service_returns_not_found_for_empty_month_without_generating_pdf()
    {
        var repository = new CapturingWorksheetRepository { Entries = [] };
        var generator = new CapturingPdfGenerator();
        var service = CreateService(repository, generator, Guid.NewGuid());

        var result = await service.GetAllWorksheetsPdfAsync(2026, 8, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Null(generator.Month);
    }

    [Fact]
    public void Generator_creates_a_pdf_from_monthly_hours_data()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var day = new MyWorksheetDayResponse(
            new DateOnly(2026, 8, 4),
            10m,
            0,
            [
                new MyWorksheetEntryResponse(new DateOnly(2026, 8, 4), Guid.NewGuid(), firstUser, "R-1", "Kunde A", null, 6m, false, "Alex Jensen"),
                new MyWorksheetEntryResponse(new DateOnly(2026, 8, 4), Guid.NewGuid(), secondUser, "R-2", "Kunde B", null, 4m, false, "Alex Jensen")
            ]);
        var month = new MyWorksheetsMonthResponse(
            2026,
            8,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            10m,
            0,
            [new MyWorksheetWeekResponse(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), 10m, 0, [day])]);

        var pdf = new MonthlyHoursPdfGenerator().Generate(month);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    private static WorksheetService CreateService(
        IWorksheetRepository repository,
        IMonthlyHoursPdfGenerator generator,
        Guid organizationId) =>
        new(
            repository,
            null!,
            null!,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId),
            generator,
            NullLogger<WorksheetService>.Instance);

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => "Admin";
    }

    private sealed class CapturingPdfGenerator : IMonthlyHoursPdfGenerator
    {
        public MyWorksheetsMonthResponse? Month { get; private set; }

        public byte[] Generate(MyWorksheetsMonthResponse month)
        {
            Month = month;
            return [1, 2, 3];
        }
    }

    private sealed class CapturingWorksheetRepository : IWorksheetRepository
    {
        public IReadOnlyList<MyWorksheetEntryResponse> Entries { get; init; } = [];
        public Guid? RequestedOrganizationId { get; private set; }
        public DateOnly? RequestedMonthStart { get; private set; }
        public DateOnly? RequestedMonthEnd { get; private set; }

        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(
            Guid organizationId,
            DateOnly monthStart,
            DateOnly monthEnd,
            CancellationToken cancellationToken)
        {
            RequestedOrganizationId = organizationId;
            RequestedMonthStart = monthStart;
            RequestedMonthEnd = monthEnd;
            return Task.FromResult(Entries);
        }

        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
