using System.Globalization;
using FluentValidation;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator(
        IUserRepository userRepository,
        IWorksheetRepository worksheetRepository,
        ICurrentUserContext currentUser)
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
            var datedTimesheets = new List<(CreateTimesheetRequest Timesheet, Guid UserId, DateOnly WorkDate)>(request.Timesheets.Count);
            foreach (var timesheet in request.Timesheets)
            {
                if (!Guid.TryParse(timesheet.UserId, out var timesheetUserId) || timesheetUserId == Guid.Empty)
                {
                    context.AddFailure(nameof(CreateJobRequest.Timesheets), "Bruger-id på timeregistreringen er ugyldigt.");
                    return;
                }

                timesheetUserIds.Add(timesheetUserId);
                if (DateOnly.TryParse(timesheet.WorkDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
                {
                    datedTimesheets.Add((timesheet, timesheetUserId, workDate));
                }
            }

            if (!JobAssignmentPolicy.AreTimesheetUsersAssigned(assignedUserIds, timesheetUserIds))
            {
                context.AddFailure(
                    nameof(CreateJobRequest.Timesheets),
                    "Timer kan kun registreres på medarbejdere, der er tildelt sagen.");
                return;
            }

            foreach (var day in datedTimesheets.GroupBy(entry => new { entry.UserId, entry.WorkDate }))
            {
                var existingHours = await worksheetRepository.GetHoursForUserDayAsync(
                    organizationId.Value,
                    day.Key.UserId,
                    day.Key.WorkDate,
                    cancellationToken);

                if (existingHours + day.Sum(entry => entry.Timesheet.HoursWorked) > WorksheetHourRules.MaxDailyHours)
                {
                    context.AddFailure(nameof(CreateJobRequest.Timesheets), WorksheetHourRules.DailyLimitMessage);
                }
            }
        });
    }
}
