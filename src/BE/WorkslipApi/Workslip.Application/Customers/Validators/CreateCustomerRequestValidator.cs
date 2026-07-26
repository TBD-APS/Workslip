using FluentValidation;

namespace Workslip.Application.Customers.Validators;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Kundenavn er påkrævet.")
            .MaximumLength(240).WithMessage("Kundenavn må højst være 240 tegn.");

        RuleFor(x => x.CustomerNumber)
            .MaximumLength(80).WithMessage("Kundenummer må højst være 80 tegn.");

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Adresse må højst være 500 tegn.");

        RuleFor(x => x.ZipCode)
            .MaximumLength(20).WithMessage("Postnummer må højst være 20 tegn.");

        RuleFor(x => x.City)
            .MaximumLength(120).WithMessage("By må højst være 120 tegn.");

        RuleFor(x => x.Country)
            .MaximumLength(120).WithMessage("Land må højst være 120 tegn.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("E-mailadressen er ugyldig.")
            .MaximumLength(320).WithMessage("E-mailadressen må højst være 320 tegn.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.ContactPerson)
            .MaximumLength(200).WithMessage("Kontaktperson må højst være 200 tegn.");

        RuleFor(x => x.Phone)
            .MaximumLength(80).WithMessage("Telefonnummer må højst være 80 tegn.");
    }
}
