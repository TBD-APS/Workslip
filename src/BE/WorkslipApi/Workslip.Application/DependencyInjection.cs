using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Auth;
using Workslip.Application.Worksheets;
using Workslip.Application.Invitations;
using Workslip.Application.Jobs;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Application.Customers;
using Workslip.Application.Notifications;

namespace Workslip.Application;

public static class DependencyInjection
{
public static IServiceCollection AddWorkslipApplication(this IServiceCollection services)
{
    services.AddScoped<JobService>();
    services.AddScoped<IJobService, AuthorizedJobService>();
    services.AddScoped<IJobAssignmentValidator, JobAssignmentValidator>();
    services.AddScoped<IJobAssignmentService, JobAssignmentService>();
    services.AddScoped<JobValidationService>();
    services.AddScoped<JobDeletionNotificationService>();
    services.AddScoped<IOrganizationService, OrganizationService>();
    services.AddScoped<IOrganizationSessionService, OrganizationSessionService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<UserEntraService>();
    services.AddScoped<IUserEntraService>(serviceProvider =>
        serviceProvider.GetRequiredService<UserEntraService>());
    services.AddScoped<ISuperadminEntraService>(serviceProvider =>
        serviceProvider.GetRequiredService<UserEntraService>());
    services.AddScoped<IAuthService, AuthService>();
    services.AddScoped<IInvitationService, InvitationService>();
    services.AddScoped<IInvitationStatusService, InvitationStatusService>();
    services.AddScoped<IWorksheetService, WorksheetService>();
    services.AddScoped<IReferenceDataService, ReferenceDataService>();
    services.AddScoped<ICustomerService, CustomerService>();
    services.AddScoped<INotificationService, NotificationService>();
    services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();

    // Add FluentValidation validators (scans the entire assembly)
    services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    return services;
}
}
