using FluentValidation;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator()
    {
        SharedJobRequestRules.AddCommonRules(this);
    }
}
