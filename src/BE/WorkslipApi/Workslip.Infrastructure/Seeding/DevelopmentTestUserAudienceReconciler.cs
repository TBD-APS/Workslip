using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal static class DevelopmentTestUserAudienceReconciler
{
    private static readonly DevelopmentTestIdentity[] Identities =
    [
        new(
            new Guid("A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F"),
            "admin@17v3ygzs.mailosaur.net",
            "Niels Petersen",
            Roles.Admin),
        new(
            new Guid("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F"),
            "user@17v3ygzs.mailosaur.net",
            "Arne Arnesen",
            Roles.User),
        new(
            new Guid("C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F"),
            "auditor@17v3ygzs.mailosaur.net",
            "Auditor Jakobsen",
            Roles.Auditor)
    ];

    public static async Task ReconcileAsync(
        SqlDbContext db,
        CancellationToken cancellationToken = default)
    {
        var ids = Identities.Select(identity => identity.Id).ToArray();
        var users = await db.Users
            .Where(user => ids.Contains(user.Id))
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var identity in Identities)
        {
            var user = users.SingleOrDefault(candidate => candidate.Id == identity.Id);
            if (user is null || !identity.Matches(user))
            {
                continue;
            }

            if (string.Equals(user.UserKind, UserKinds.InternalTest, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(user.UserKind, UserKinds.Member, StringComparison.Ordinal))
            {
                continue;
            }

            user.UserKind = UserKinds.InternalTest;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var previousIsSeeding = db.IsSeeding;
        db.IsSeeding = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.IsSeeding = previousIsSeeding;
        }
    }

    private sealed record DevelopmentTestIdentity(
        Guid Id,
        string Email,
        string DisplayName,
        string Role)
    {
        public bool Matches(UserDataRow user) =>
            string.Equals(user.Email, Email, StringComparison.OrdinalIgnoreCase)
            && string.Equals(user.DisplayName, DisplayName, StringComparison.Ordinal)
            && string.Equals(user.Role, Role, StringComparison.OrdinalIgnoreCase);
    }
}
