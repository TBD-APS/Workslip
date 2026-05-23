using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Workslip.Application.Organizations;
using Workslip.Application.Organizations.Validators;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;

namespace Workslip.Application;

public static class DependencyInjection
{
public static IServiceCollection AddWorkslipApplication(this IServiceCollection services)
{
    services.AddScoped<IJobService, JobService>();
    services.AddScoped<IOrganizationService, OrganizationService>();
    services.AddScoped<UserService>();
    services.AddScoped<IAuthService, AuthService>();
    
    // Add FluentValidation validators
    //services.AddValidatorsFromAssemblyContaining<CreateJobRequestValidator>();
    services.AddValidatorsFromAssemblyContaining<UpdateJobRequestValidator>();
    services.AddValidatorsFromAssemblyContaining<CreateOrganizationRequestValidator>();
    services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>();

    return services;
}
}
