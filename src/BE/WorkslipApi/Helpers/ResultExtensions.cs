using Ardalis.Result;

namespace Workslip.Api.Helpers;

public static class ResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult(
        Ardalis.Result.Result result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(),
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.NoContent => Results.NoContent(),
            ResultStatus.Conflict => Results.Conflict(new
            {
                error = result.Errors.FirstOrDefault() ?? "conflict",
                message = result.SuccessMessage
            }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => Results.Problem(
                detail: result.Errors.FirstOrDefault(),
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(
        Ardalis.Result.Result<T> result)
    {
        return ToHttpResult(result, x => x);
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T, TOut>(
        Ardalis.Result.Result<T> result,
        Func<T, TOut> map)
    {
        return result.Status switch
        {
            ResultStatus.Ok or ResultStatus.Created
                => Results.Ok(map(result.Value)),

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
