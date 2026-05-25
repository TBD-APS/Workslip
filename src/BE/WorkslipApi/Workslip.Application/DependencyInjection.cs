using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Jobs;
using Workslip.Application.Organizations;
using Workslip.Application.Users;

namespace Workslip.Application;

public static class DependencyInjection
{
public static IServiceCollection AddWorkslipApplication(this IServiceCollection services)
{
    services.AddScoped<IJobService, JobService>();
    services.AddScoped<IOrganizationService, OrganizationService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<IUserEntraService, UserEntraService>();
    services.AddScoped<IAuthService, AuthService>();
    services.AddScoped<IInvitationService, InvitationService>();

    // Add FluentValidation validators (scans the entire assembly)
    services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    return services;
}
}
