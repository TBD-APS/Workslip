using System.Globalization;
using FluentValidation;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public static class SharedJobRequestRules
{
    public static bool BeValidJobType(string? jobType)
    {
        return Enum.TryParse<JobType>(jobType, out var _);
    }

    public static void AddCommonRules(AbstractValidator<CreateJobRequest> validator)
    {
        ApplyCustomerSnapshot(validator, x => x.CustomerSnapshot);
        ApplyJobType(validator, x => x.JobType);
        ApplyWork(validator, x => x.Work);
        validator.RuleForEach(x => x.Timesheets)
            .SetValidator(new TimesheetRequestValidator());
    }

    public static void AddCommonRules(AbstractValidator<UpdateJobRequest> validator)
    {
        ApplyCustomerSnapshot(validator, x => x.CustomerSnapshot);
        ApplyJobType(validator, x => x.JobType);
        ApplyWork(validator, x => x.Work);
        validator.RuleForEach(x => x.Timesheets)
            .SetValidator(new TimesheetRequestValidator());
    }

    private static void ApplyCustomerSnapshot<T>(
        AbstractValidator<T> validator,
        Func<T, CustomerSnapshotData?> getSnapshot) where T : class
    {
        validator.RuleFor(x => getSnapshot(x))
            .SetValidator(new CustomerSnapshotDataValidator()!)
            .When(x => getSnapshot(x) is not null);
    }

    private static void ApplyJobType<T>(
        AbstractValidator<T> validator,
        Func<T, string?> getJobType) where T : class
    {
        validator.RuleFor(x => getJobType(x))
            .Must(BeValidJobType).WithMessage("Sagstypen skal være 'KLS' eller 'Diverse'.")
            .When(x => !string.IsNullOrWhiteSpace(getJobType(x)));
    }

    private static void ApplyWork<T>(
        AbstractValidator<T> validator,
        Func<T, CreateJobWorkRequest?> getWork) where T : class
    {
        validator.When(x => getWork(x) is not null, () =>
        {
            validator.RuleFor(x => getWork(x)!.InstallationTypes)
                .Must(JobRequestValidationRules.HaveNoDuplicateInstallations)
                .WithMessage("Den samme installationstype må ikke vælges flere gange.");

            validator.RuleForEach(x => getWork(x)!.InstallationTypes)
                .ChildRules(installation =>
                {
                    installation.RuleFor(x => x.Id)
                        .NotEmpty().WithMessage("Installationstype-id er påkrævet.");

                    installation.RuleFor(x => x.Categories)
                        .Must(JobRequestValidationRules.HaveNoDuplicateCategories)
                        .WithMessage("Den samme kategori må ikke vælges flere gange for en installationstype.");

                    installation.RuleForEach(x => x.Categories)
                        .ChildRules(category =>
                        {
                            category.RuleFor(x => x.Id)
                                .NotEmpty().WithMessage("Kategori-id er påkrævet.");

                            category.RuleFor(x => x.ControlPoints)
                                .Must(JobRequestValidationRules.HaveNoDuplicateControlPoints)
                                .WithMessage("Det samme kontrolpunkt må ikke vælges flere gange for en kategori.");

                            category.RuleForEach(x => x.ControlPoints)
                                .ChildRules(controlPoint =>
                                {
                                    controlPoint.RuleFor(x => x.Id)
                                        .NotEmpty().WithMessage("Kontrolpunkt-id er påkrævet.");
                                });
                        });
                });

            validator.RuleFor(x => getWork(x)!.WorkKind)
                .MaximumLength(80).WithMessage("Arbejdstypen må højst være 80 tegn.");

            validator.RuleFor(x => getWork(x)!.CustomWorkKind)
                .MaximumLength(160).WithMessage("Den brugerdefinerede arbejdstype må højst være 160 tegn.");

            validator.RuleFor(x => getWork(x)!.ClosureFlags)
                .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Det samme afslutningsflag må ikke vælges flere gange.");
        });
    }

    private sealed class TimesheetRequestValidator : AbstractValidator<CreateTimesheetRequest>
    {
        public TimesheetRequestValidator()
        {
            RuleFor(x => x.UserId)
                .Must(value => Guid.TryParse(value, out var id) && id != Guid.Empty)
                .WithMessage("Bruger-id på timeregistreringen er ugyldigt.");

            RuleFor(x => x.WorkDate)
                .Must(value => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .WithMessage("Arbejdsdato på timeregistreringen er ugyldig.");

            RuleFor(x => x.HoursWorked)
                .GreaterThan(0).WithMessage("Antal timer skal være større end 0.")
                .LessThanOrEqualTo(WorksheetHourRules.MaxDailyHours).WithMessage(WorksheetHourRules.DailyLimitMessage)
                .Must(WorksheetHourRules.IsValidIncrement).WithMessage("Antal timer skal angives i intervaller på 0,25 time.");
        }
    }
}
