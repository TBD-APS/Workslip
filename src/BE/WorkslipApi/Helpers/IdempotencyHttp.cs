using System.Text;
using Workslip.Api.Services;

namespace Workslip.Api.Helpers;

public static class IdempotencyHttp
{
    public static bool TryGetKey(HttpContext context, out string key)
    {
        key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        return key.Length is > 0 and <= 128;
    }

    public static IResult? ReplayOrReject(IdempotencyStartResult result)
    {
        if (result.RequestHashConflict)
        {
            return Results.Conflict(new
            {
                error = "idempotency_key_reused_with_different_request",
                message = "Idempotensnøglen er allerede brugt til en anden anmodning."
            });
        }

        if (result.InProgress)
        {
            return Results.Conflict(new
            {
                error = "request_with_idempotency_key_in_progress",
                message = "En anmodning med denne idempotensnøgle behandles allerede."
            });
        }

        return result.IsReplay
            ? Results.Content(result.ResponseJson!, "application/json", Encoding.UTF8, result.StatusCode!.Value)
            : null;
    }
}
