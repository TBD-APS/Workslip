using FluentValidation;
using Workslip.Application.Worksheets;

namespace Workslip.Application.Worksheets.Validators;

public sealed class CreateWorksheetValidator : AbstractValidator<UpsertWorksheetRequest>
{
    public CreateWorksheetValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty().WithMessage("Job ID is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required.");

        RuleFor(x => x.WorkDate)
            .NotEmpty().WithMessage("Work date is required.");

        RuleFor(x => x.HoursWorked)
            .GreaterThan(0).WithMessage("Hours worked must be greater than 0.")
            .LessThanOrEqualTo(24).WithMessage("Hours worked cannot exceed 24 hours in a day.")
            .Must(BeValidHourIncrement).WithMessage("Hours worked must be in increments of 0.25 (quarter, half, or whole hours).");
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
