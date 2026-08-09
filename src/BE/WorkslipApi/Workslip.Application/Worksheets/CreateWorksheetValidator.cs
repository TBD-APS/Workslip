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
            .LessThanOrEqualTo(WorksheetHourRules.MaxDailyHours).WithMessage(WorksheetHourRules.DailyLimitMessage)
            .Must(WorksheetHourRules.IsValidIncrement).WithMessage("Antal timer skal angives i intervaller på 0,25 time.");
    }
}
