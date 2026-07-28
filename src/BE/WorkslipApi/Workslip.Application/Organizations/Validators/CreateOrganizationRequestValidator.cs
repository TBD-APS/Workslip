using FluentValidation;
using Workslip.Application.Organizations;

namespace Workslip.Application.Organizations.Validators;

public class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organisationsnavn er påkrævet.");

        RuleFor(x => x.Cvr)
            .NotEmpty().WithMessage("CVR-nummer er påkrævet.")
            .Length(8).WithMessage("CVR-nummer skal bestå af præcis 8 cifre.")
            .Matches(@"^\d{8}$").WithMessage("CVR-nummer må kun indeholde cifre.");

        RuleFor(x => x.AdminDisplayName)
            .NotEmpty().WithMessage("Administratorens visningsnavn er påkrævet.");

        RuleFor(x => x.AdminEmail)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.AdminEmail))
            .WithMessage("Administratorens e-mailadresse er ugyldig.");
    }
}
