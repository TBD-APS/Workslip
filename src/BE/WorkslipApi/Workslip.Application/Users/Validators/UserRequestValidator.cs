using FluentValidation;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Application.Users.Validators;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("E-mailadresse er påkrævet.")
            .EmailAddress().WithMessage("E-mailadressen er ugyldig.")
            .MaximumLength(256).WithMessage("E-mailadressen må højst være 256 tegn.");

        RuleFor(request => request.DisplayName)
            .NotEmpty().WithMessage("Visningsnavn er påkrævet.")
            .MaximumLength(256).WithMessage("Visningsnavn må højst være 256 tegn.");

        RuleFor(request => request.Phone)
            .MaximumLength(20).WithMessage("Telefonnummer må højst være 20 tegn.")
            .When(request => !string.IsNullOrEmpty(request.Phone));

        RuleFor(request => request.Role)
            .NotEmpty().WithMessage("Rolle er påkrævet.")
            .Must(role => role is Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Rollen skal være Admin, Auditor eller User. Superadmin oprettes kun gennem platformadministration.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(request => request.DisplayName)
            .MaximumLength(256).WithMessage("Visningsnavn må højst være 256 tegn.")
            .When(request => !string.IsNullOrEmpty(request.DisplayName));

        RuleFor(request => request.Phone)
            .MaximumLength(20).WithMessage("Telefonnummer må højst være 20 tegn.")
            .When(request => !string.IsNullOrEmpty(request.Phone));

        RuleFor(request => request.Role)
            .Must(role => role is Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Rollen skal være Admin, Auditor eller User. Superadmin administreres på platformniveau.")
            .When(request => !string.IsNullOrEmpty(request.Role));
    }
}
