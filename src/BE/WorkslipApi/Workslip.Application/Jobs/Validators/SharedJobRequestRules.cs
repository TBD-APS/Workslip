using FluentValidation;
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
    }

    public static void AddCommonRules(AbstractValidator<UpdateJobRequest> validator)
    {
        ApplyCustomerSnapshot(validator, x => x.CustomerSnapshot);
        ApplyJobType(validator, x => x.JobType);
        ApplyWork(validator, x => x.Work);
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
            .Must(BeValidJobType).WithMessage("JobType must be 'KLS' or 'Diverse'.")
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
                .WithMessage("Duplicate installation type is not allowed.");

            validator.RuleForEach(x => getWork(x)!.InstallationTypes)
                .ChildRules(installation =>
                {
                    installation.RuleFor(x => x.Id)
                        .NotEmpty().WithMessage("Installation type id is required.");

                    installation.RuleFor(x => x.Categories)
                        .Must(JobRequestValidationRules.HaveNoDuplicateCategories)
                        .WithMessage("Duplicate category is not allowed for an installation type.");

                    installation.RuleForEach(x => x.Categories)
                        .ChildRules(category =>
                        {
                            category.RuleFor(x => x.Id)
                                .NotEmpty().WithMessage("Category id is required.");

                            category.RuleFor(x => x.ControlPoints)
                                .Must(JobRequestValidationRules.HaveNoDuplicateControlPoints)
                                .WithMessage("Duplicate control point is not allowed for a category.");

                            category.RuleForEach(x => x.ControlPoints)
                                .ChildRules(controlPoint =>
                                {
                                    controlPoint.RuleFor(x => x.Id)
                                        .NotEmpty().WithMessage("Control point id is required.");
                                });
                        });
                });

            validator.RuleFor(x => getWork(x)!.WorkKind)
                .MaximumLength(80).WithMessage("Work kind must not exceed 80 characters.");

            validator.RuleFor(x => getWork(x)!.CustomWorkKind)
                .MaximumLength(160).WithMessage("Custom work kind must not exceed 160 characters.");

            validator.RuleFor(x => getWork(x)!.ClosureFlags)
                .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.");
        });
    }
}
