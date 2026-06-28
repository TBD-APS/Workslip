using FluentValidation;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Application.Users.Validators;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be valid")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MaximumLength(256).WithMessage("Display name cannot exceed 256 characters");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone cannot exceed 20 characters")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(r => r is Roles.Superadmin or Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Role must be Superadmin, Admin, Auditor, or User");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(256).WithMessage("Display name cannot exceed 256 characters")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone cannot exceed 20 characters")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Role)
            .Must(r => r is Roles.Superadmin or Roles.Admin or Roles.Auditor or Roles.User)
            .WithMessage("Role must be Superadmin, Admin, Auditor, or User")
            .When(x => !string.IsNullOrEmpty(x.Role));
    }
}
