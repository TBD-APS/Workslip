using Microsoft.EntityFrameworkCore;
using Xunit;
using Workslip.Api.Services;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Api;

public sealed class IdempotencyStoreTests
{
    private static SqlDbContext CreateDb(string name) => new(new DbContextOptionsBuilder<SqlDbContext>()
        .UseInMemoryDatabase(name)
        .Options);

    [Fact]
    public async Task SameKeyAndRequest_ReplaysCompletedResponse()
    {
        await using var db = CreateDb(nameof(SameKeyAndRequest_ReplaysCompletedResponse));
        var store = new IdempotencyStore(db);
        var first = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "A" }, CancellationToken.None);

        await store.CompleteAsync(first.Reservation!.Id, first.ReservationToken!, new { id = 42 }, 200, CancellationToken.None);
        var replay = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "A" }, CancellationToken.None);

        Assert.True(replay.IsReplay);
        Assert.Equal("{\"id\":42}", replay.ResponseJson);
    }

    [Fact]
    public async Task SameKeyAndDifferentRequest_IsRejected()
    {
        await using var db = CreateDb(nameof(SameKeyAndDifferentRequest_IsRejected));
        var store = new IdempotencyStore(db);
        _ = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "A" }, CancellationToken.None);

        var conflict = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "B" }, CancellationToken.None);

        Assert.True(conflict.RequestHashConflict);
    }

    [Fact]
    public async Task ReservationToken_PreventsStaleCompletion()
    {
        await using var db = CreateDb(nameof(ReservationToken_PreventsStaleCompletion));
        var store = new IdempotencyStore(db);
        var reservation = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "A" }, CancellationToken.None);

        await store.CompleteAsync(reservation.Reservation!.Id, "wrong-token", new { id = 1 }, 200, CancellationToken.None);
        var stillInProgress = await store.StartAsync("jobs.create:org:user", "key-1", new { name = "A" }, CancellationToken.None);

        Assert.True(stillInProgress.InProgress);
    }
}
