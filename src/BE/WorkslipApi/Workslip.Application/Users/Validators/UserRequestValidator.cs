using FluentValidation;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Application.Users.Validators;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-mailadresse er påkrævet.")
            .EmailAddress().WithMessage("E-mailadressen er ugyldig.")
            .MaximumLength(256).WithMessage("E-mailadressen må højst være 256 tegn.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Visningsnavn er påkrævet.")
            .MaximumLength(256).WithMessage("Visningsnavn må højst være 256 tegn.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Telefonnummer må højst være 20 tegn.")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Rolle er påkrævet.")
            .Must(r => r is Roles.Superadmin or Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Rollen skal være Superadmin, Admin, Auditor eller User.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(256).WithMessage("Visningsnavn må højst være 256 tegn.")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Telefonnummer må højst være 20 tegn.")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Role)
            .Must(r => r is Roles.Superadmin or Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Rollen skal være Superadmin, Admin, Auditor eller User.")
            .When(x => !string.IsNullOrEmpty(x.Role));
    }
}
