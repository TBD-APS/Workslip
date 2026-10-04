namespace Workslip.Application.Jobs;

public static class JobLifecycleAuditContext
{
    private static readonly AsyncLocal<string?> RejectionCategorySlot = new();

    public static string? RejectionCategory => RejectionCategorySlot.Value;

    public static IDisposable PushRejectionCategory(string? category)
    {
        var previous = RejectionCategorySlot.Value;
        RejectionCategorySlot.Value = category;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            RejectionCategorySlot.Value = previous;
            disposed = true;
        }
    }
}
