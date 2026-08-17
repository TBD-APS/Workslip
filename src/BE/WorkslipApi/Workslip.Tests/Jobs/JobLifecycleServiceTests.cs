using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobLifecycleServiceTests
{
    [Fact]
    public async Task ChangeStatusAsync_WithoutOrganizationContext_RemainsUnauthorized()
    {
        var service = new JobLifecycleService(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new InlineValidator<ChangeJobStatusRequest>(),
            new TestCurrentUserContext(Guid.NewGuid(), null, Roles.Admin),
            NullLogger<JobService>.Instance,
            null!,
            null!);

        var result = await service.ChangeStatusAsync(
            Guid.NewGuid(),
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;
}
