using FluentValidation;
using Workslip.Application.Worksheets;

namespace Workslip.Application.Worksheets.Validators;

public sealed class CreateWorksheetValidator : AbstractValidator<UpsertWorksheetRequest>
{
    public CreateWorksheetValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty().WithMessage("Sag-id er påkrævet.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("Bruger-id er påkrævet.");

        RuleFor(x => x.WorkDate)
            .NotEmpty().WithMessage("Arbejdsdato er påkrævet.");

        RuleFor(x => x.HoursWorked)
            .GreaterThan(0).WithMessage("Antal timer skal være større end 0.")
            .LessThanOrEqualTo(24).WithMessage("Antal timer må højst være 24 pr. dag.")
            .Must(BeValidHourIncrement).WithMessage("Antal timer skal angives i intervaller på 0,25 time.");
    }

    private bool BeValidHourIncrement(decimal hours)
    {
        // Check if hours is a multiple of 0.25
        // Multiply by 4, check if it's close to an integer
        var multiplied = hours * 4m;
        var remainder = decimal.Remainder(multiplied, 1m);
        return Math.Abs(remainder) < 0.0001m || Math.Abs(remainder - 1m) < 0.0001m;
    }
}
