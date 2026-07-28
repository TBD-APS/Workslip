using Ardalis.Result;

namespace Workslip.Api.Helpers;

public static class ResultExtensions
{
    private const string UnexpectedErrorTitle = "Der opstod en uventet fejl.";
    private const string UnexpectedErrorDetail = "Anmodningen kunne ikke gennemføres. Prøv igen, eller kontakt support.";
    private const string GenericConflictMessage = "Handlingen kunne ikke gennemføres på grund af en konflikt.";

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult(Result result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(),
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.NoContent => Results.NoContent(),
            ResultStatus.Conflict => ToConflictResult(result.Errors, result.SuccessMessage),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => ToUnexpectedErrorResult()
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(Result<T> result)
    {
        return ToHttpResult(result, x => x);
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T, TOut>(Result<T> result, Func<T, TOut> map)
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

            ResultStatus.Conflict => ToConflictResult(result.Errors, result.SuccessMessage),

            ResultStatus.Unauthorized => Results.Unauthorized(),

            ResultStatus.Forbidden => Results.Forbid(),

            ResultStatus.NoContent => Results.NoContent(),

            _ => ToUnexpectedErrorResult()
        };
    }

    private static Microsoft.AspNetCore.Http.IResult ToConflictResult(IEnumerable<string> errors, string? successMessage)
    {
        var error = errors.FirstOrDefault() ?? "conflict";
        return Results.Conflict(new
        {
            error,
            message = string.IsNullOrWhiteSpace(successMessage)
                ? GetConflictMessage(error)
                : successMessage
        });
    }

    private static Microsoft.AspNetCore.Http.IResult ToUnexpectedErrorResult() =>
        Results.Problem(
            title: UnexpectedErrorTitle,
            detail: UnexpectedErrorDetail,
            statusCode: StatusCodes.Status500InternalServerError);

    private static string GetConflictMessage(string error) => error switch
    {
        "customer_number_exists" => "Kundenummeret er allerede i brug.",
        "duplicate_report_number" => "Rapportnummeret er allerede i brug.",
        "organization_cvr_exists" => "Der findes allerede en organisation med dette CVR-nummer.",
        "email_in_use" => "E-mailadressen er allerede i brug.",
        "entra_user_not_provisioned" => "Den inviterede bruger er ikke klar endnu. Prøv igen.",
        "invite_consumed" => "Invitationen er allerede brugt.",
        "invite_expired" => "Invitationen er udløbet.",
        "worksheet_rule_violation" => "Arbejdssedlen kunne ikke gemmes, fordi oplysningerne er ugyldige.",
        _ => GenericConflictMessage
    };
}
