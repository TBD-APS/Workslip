using System.Text.Json;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using ApiResultExtensions = Workslip.Api.Helpers.ResultExtensions;
using Xunit;

namespace Workslip.Tests.Helpers;

public sealed class ResultExtensionsTests
{
    [Fact]
    public async Task ToHttpResult_hides_technical_error_details()
    {
        const string technicalMessage = "Sensitive English database exception";
        var result = Result.Error(technicalMessage);

        var context = CreateHttpContext();
        await ApiResultExtensions.ToHttpResult(result).ExecuteAsync(context);

        var root = await ReadResponseAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("Der opstod en uventet fejl.", root.GetProperty("title").GetString());
        Assert.DoesNotContain(technicalMessage, root.ToString());
    }

    [Fact]
    public async Task ToHttpResult_keeps_conflict_code_and_adds_danish_message()
    {
        var result = Result.Conflict("email_in_use");

        var context = CreateHttpContext();
        await ApiResultExtensions.ToHttpResult(result).ExecuteAsync(context);

        var root = await ReadResponseAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("email_in_use", root.GetProperty("error").GetString());
        Assert.Equal("E-mailadressen er allerede i brug.", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ToHttpResult_maps_invalid_job_transition_to_conflict()
    {
        var result = Result.Conflict("invalid_job_status_transition");

        var context = CreateHttpContext();
        await ApiResultExtensions.ToHttpResult(result).ExecuteAsync(context);

        var root = await ReadResponseAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("invalid_job_status_transition", root.GetProperty("error").GetString());
        Assert.Equal("Statusændringen er ikke tilladt fra sagens nuværende status.", root.GetProperty("message").GetString());
    }

    [Fact]
    public void ToHttpResult_maps_forbidden_to_forbid_result()
    {
        var mapped = ApiResultExtensions.ToHttpResult(Result.Forbidden());

        Assert.IsType<ForbidHttpResult>(mapped);
    }

    [Fact]
    public async Task ToHttpResult_translates_legacy_validation_message()
    {
        var result = Result.Invalid([
            new ValidationError
            {
                Identifier = "WorkKind",
                ErrorMessage = "Work kind is required."
            }
        ]);

        var context = CreateHttpContext();
        await ApiResultExtensions.ToHttpResult(result).ExecuteAsync(context);

        var root = await ReadResponseAsync(context);
        var error = root.GetProperty("errors").GetProperty("WorkKind")[0].GetString();

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Arbejdstype er påkrævet.", error);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        return response.RootElement.Clone();
    }
}