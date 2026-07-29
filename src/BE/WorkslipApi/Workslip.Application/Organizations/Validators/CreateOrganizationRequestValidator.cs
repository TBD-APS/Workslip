using FluentValidation;
using Workslip.Application.Organizations;

namespace Workslip.Application.Organizations.Validators;

public sealed class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organisationsnavn er påkrævet.")
            .MaximumLength(200).WithMessage("Organisationsnavnet må højst være 200 tegn.");

        RuleFor(x => x.Cvr)
            .NotEmpty().WithMessage("CVR-nummer er påkrævet.")
            .Length(8).WithMessage("CVR-nummer skal bestå af præcis 8 cifre.")
            .Matches(@"^\d{8}$").WithMessage("CVR-nummer må kun indeholde cifre.");

        RuleFor(x => x.AdminDisplayName)
            .NotEmpty().WithMessage("Administratorens visningsnavn er påkrævet.")
            .MaximumLength(200).WithMessage("Administratorens visningsnavn må højst være 200 tegn.");

        RuleFor(x => x.AdminEmail)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.AdminEmail))
            .WithMessage("Administratorens e-mailadresse er ugyldig.");

        RuleFor(x => x.AdminPhone)
            .MaximumLength(20).WithMessage("Administratorens telefonnummer må højst være 20 tegn.")
            .When(x => !string.IsNullOrWhiteSpace(x.AdminPhone));
    }
}

public sealed class UpsertOrganizationAdminRequestValidator : AbstractValidator<UpsertOrganizationAdminRequest>
{
    public UpsertOrganizationAdminRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Administratorens e-mailadresse er påkrævet.")
            .EmailAddress().WithMessage("Administratorens e-mailadresse er ugyldig.")
            .MaximumLength(320).WithMessage("Administratorens e-mailadresse må højst være 320 tegn.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Administratorens visningsnavn er påkrævet.")
            .MaximumLength(200).WithMessage("Administratorens visningsnavn må højst være 200 tegn.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Administratorens telefonnummer må højst være 20 tegn.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}
