using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public sealed class CustomerSnapshotDataValidator : AbstractValidator<CustomerSnapshotData>
{
    public CustomerSnapshotDataValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(240).WithMessage("Customer name must not exceed 240 characters.");

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Customer address must not exceed 500 characters.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Customer email is invalid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Phone)
            .MaximumLength(80).WithMessage("Phone must not exceed 80 characters.");
    }
}
