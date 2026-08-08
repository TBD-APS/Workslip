using FluentValidation;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator(IUserRepository userRepository, ICurrentUserContext currentUser)
    {
        SharedJobRequestRules.AddCommonRules(this);

        RuleFor(request => request).CustomAsync(async (request, context, cancellationToken) =>
        {
            var organizationId = currentUser.OrganizationId;
            if (organizationId is null)
                return;

            var assignedUserIds = JobAssignmentPolicy.ResolveInitialAssignments(
                request.AssignedUserIds,
                currentUser.UserId,
                currentUser.Role);

            foreach (var userId in assignedUserIds)
            {
                var user = await userRepository.GetByIdAsync(userId, cancellationToken);
                if (user is null
                    || user.OrganizationId != organizationId.Value
                    || !JobAssignmentPolicy.CanReceiveAssignment(user.Role))
                {
                    context.AddFailure(
                        nameof(CreateJobRequest.AssignedUserIds),
                        "Sager kan kun tildeles brugere eller administratorer i samme organisation.");
                    return;
                }
            }

            if (request.Timesheets is null || request.Timesheets.Count == 0)
                return;

            var timesheetUserIds = new List<Guid>(request.Timesheets.Count);
            foreach (var timesheet in request.Timesheets)
            {
                if (!Guid.TryParse(timesheet.UserId, out var timesheetUserId) || timesheetUserId == Guid.Empty)
                {
                    context.AddFailure(nameof(CreateJobRequest.Timesheets), "Bruger-id på timeregistreringen er ugyldigt.");
                    return;
                }
                timesheetUserIds.Add(timesheetUserId);
            }

            if (!JobAssignmentPolicy.AreTimesheetUsersAssigned(assignedUserIds, timesheetUserIds))
            {
                context.AddFailure(
                    nameof(CreateJobRequest.Timesheets),
                    "Timer kan kun registreres på medarbejdere, der er tildelt sagen.");
            }
        });
    }
}
