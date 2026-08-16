namespace Workslip.Infrastructure.Schema;

/// <summary>
/// Request-scoped bridge between the repository transition command and the
/// SaveChanges immutability guard. This keeps the mandatory reopen reason in
/// the same database save/audit event as Approved -> Reopened.
/// </summary>
public sealed class JobReopenReasonContext
{
    private PendingReason? pending;

    public IDisposable Begin(Guid jobId, Guid organizationId, string reason)
    {
        if (pending is not null)
            throw new InvalidOperationException("A job reopen transition is already in progress in this scope.");

        pending = new PendingReason(jobId, organizationId, reason.Trim());
        return new Scope(this);
    }

    public bool TryGet(Guid jobId, Guid organizationId, out string reason)
    {
        if (pending is not null
            && pending.JobId == jobId
            && pending.OrganizationId == organizationId
            && !string.IsNullOrWhiteSpace(pending.Reason))
        {
            reason = pending.Reason;
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private void Clear() => pending = null;

    private sealed record PendingReason(Guid JobId, Guid OrganizationId, string Reason);

    private sealed class Scope(JobReopenReasonContext owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.Clear();
        }
    }
}
