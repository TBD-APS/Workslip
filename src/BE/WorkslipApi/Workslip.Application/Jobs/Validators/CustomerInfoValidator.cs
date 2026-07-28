using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public sealed class CustomerInfoValidator : AbstractValidator<CustomerInfo>
{
    public CustomerInfoValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(240).WithMessage("Kundenavn må højst være 240 tegn.");

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Kundeadresse må højst være 500 tegn.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Kundens e-mailadresse er ugyldig.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.ContactPerson)
            .MaximumLength(200).WithMessage("Kontaktperson må højst være 200 tegn.");

        RuleFor(x => x.Phone)
            .MaximumLength(80).WithMessage("Telefonnummer må højst være 80 tegn.");
    }
}
