using FluentValidation;
using Workslip.Application.Organizations;

namespace Workslip.Application.Organizations.Validators;

public class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.");
            
        RuleFor(x => x.Cvr)
            .NotEmpty().WithMessage("CVR is required.")
            .Length(8).WithMessage("CVR must be exactly 8 digits.")
            .Matches(@"^\d{8}$").WithMessage("CVR must contain only digits.");
            
        RuleFor(x => x.AdminDisplayName)
            .NotEmpty().WithMessage("Admin display name is required.");
            
        RuleFor(x => x.AdminEmail)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.AdminEmail))
            .WithMessage("Admin email must be a valid email address.");
    }
}