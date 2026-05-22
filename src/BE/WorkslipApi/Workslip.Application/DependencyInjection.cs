using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Jobs;
using Workslip.Application.Organizations;

namespace Workslip.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkslipApplication(this IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
