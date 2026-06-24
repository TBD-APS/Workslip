using FluentValidation;

namespace Workslip.Application.Customers.Validators;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Kundenavn er påkrævet.")
            .MaximumLength(240).WithMessage("Kundenavn må højst være 240 tegn.");

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Adresse må højst være 500 tegn.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("E-mailadressen er ugyldig.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.ContactPerson)
            .MaximumLength(200).WithMessage("Kontaktperson må højst være 200 tegn.");

        RuleFor(x => x.Phone)
            .MaximumLength(80).WithMessage("Telefonnummer må højst være 80 tegn.");
    }
}
