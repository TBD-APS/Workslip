using Microsoft.Extensions.DependencyInjection;
using Workslip.Infrastructure.Schema;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Application;
using Workslip.Application.Jobs;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Infrastructure.Jobs;

namespace Workslip.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkslipInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseRetryPolicy, PollyDatabaseRetryPolicy>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IJobRepository, DapperJobRepository>();
        services.AddScoped<IJobLinkRepository, DapperJobLinkRepository>();
        services.AddScoped<IJobTaxonomyRepository, DapperJobTaxonomyRepository>();
        services.AddScoped<IOrganizationRepository, DapperOrganizationRepository>();
        services.AddScoped<IUserRepository, DapperUserRepository>();
        services.AddScoped<IInviteRepository, DapperInviteRepository>();
        services.AddScoped<IEmailService, AcsEmailService>();
        services.AddScoped<WorkslipSchemaRunner>();
        services.AddHostedService<JobDeletionCleanupService>();

        return services;
    }
}
