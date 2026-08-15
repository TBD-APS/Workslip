using Workslip.Application.Jobs;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobAuditorScopeValidationTests
{
    private readonly SetJobAuditorScopeRequestValidator validator = new();

    [Fact]
    public async Task Hiding_job_requires_reason()
    {
        var result = await validator.ValidateAsync(
            new SetJobAuditorScopeRequest(false, null),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SetJobAuditorScopeRequest.Reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task Hiding_job_rejects_empty_or_too_short_reason(string reason)
    {
        var result = await validator.ValidateAsync(
            new SetJobAuditorScopeRequest(false, reason),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Restoring_auditor_scope_does_not_require_reason()
    {
        var result = await validator.ValidateAsync(
            new SetJobAuditorScopeRequest(true, null),
            CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Reason_is_bounded()
    {
        var result = await validator.ValidateAsync(
            new SetJobAuditorScopeRequest(false, new string('x', SetJobAuditorScopeRequestValidator.MaxReasonLength + 1)),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }
}
