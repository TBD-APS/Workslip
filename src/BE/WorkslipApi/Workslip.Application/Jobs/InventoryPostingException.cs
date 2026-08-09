namespace Workslip.Application.Jobs;

public enum InventoryPostingFailure { InactiveOrForeignReference, InsufficientStock }

public sealed class InventoryPostingException(InventoryPostingFailure failure) : Exception(failure.ToString())
{
    public InventoryPostingFailure Failure { get; } = failure;
}
