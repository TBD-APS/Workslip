using FluentValidation;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;
public sealed class UpdateJobRequestValidator : AbstractValidator<UpdateJobRequest>
{
    public UpdateJobRequestValidator()
    {
        SharedJobRequestRules.AddCommonRules(this);
    }

}
