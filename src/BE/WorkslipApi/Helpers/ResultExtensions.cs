using Ardalis.Result;

namespace Workslip.Api.Helpers;

public static class ResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(Ardalis.Result.Result<T> result, Func<T, string>? location = null)
    {
        return result.Status switch
        {
            ResultStatus.Ok => location is not null
                ? Results.Created(location(result.Value), result.Value)
                : Results.Ok(result.Value),

            ResultStatus.Created => location is not null
                ? Results.Created(location(result.Value), result.Value)
                : Results.Ok(result.Value),

            ResultStatus.Invalid => Results.ValidationProblem(
                result.ValidationErrors
                    .GroupBy(e => e.Identifier)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

            ResultStatus.NotFound => Results.NotFound(),

            ResultStatus.Conflict => Results.Conflict(new
            {
                error = result.Errors.FirstOrDefault() ?? "conflict",
                message = result.SuccessMessage
            }),

            ResultStatus.Unauthorized => Results.Unauthorized(),

            ResultStatus.Forbidden => Results.Forbid(),

            ResultStatus.NoContent => Results.NoContent(),

            _ => Results.Problem(
                detail: result.Errors.FirstOrDefault(),
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
