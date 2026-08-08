using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetMonthResponseTests
{
    [Fact]
    public async Task GetAllWorksheetsAsync_PreservesUserIdsForSameNameEmployees()
    {
        var organizationId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var repository = new Mock<IWorksheetRepository>();
        repository
            .Setup(repo => repo.GetAllWorksheetsAsync(
                organizationId,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MyWorksheetEntryResponse(
                    new DateOnly(2026, 8, 3),
                    Guid.NewGuid(),
                    firstUserId,
                    "101",
                    "Kunde A",
                    null,
                    4m,
                    false,
                    "Alex Jensen"),
                new MyWorksheetEntryResponse(
                    new DateOnly(2026, 8, 3),
                    Guid.NewGuid(),
                    secondUserId,
                    "102",
                    "Kunde B",
                    null,
                    5m,
                    false,
                    "Alex Jensen")
            ]);

        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(context => context.OrganizationId).Returns(organizationId);

        var service = new WorksheetService(
            repository.Object,
            Mock.Of<IJobService>(),
            Mock.Of<FluentValidation.IValidator<UpsertWorksheetRequest>>(),
            currentUser.Object,
            NullLogger<WorksheetService>.Instance);

        var result = await service.GetAllWorksheetsAsync(2026, 8, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entries = result.Value.Weeks.SelectMany(week => week.Days).SelectMany(day => day.Entries).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry.UserId == firstUserId);
        Assert.Contains(entries, entry => entry.UserId == secondUserId);
        Assert.All(entries, entry => Assert.Equal("Alex Jensen", entry.UserDisplayName));
    }
}
