using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Documents;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Migrations;
using Workslip.Infrastructure.Repositories;

namespace Workslip.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkslipInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IDocumentRepository, DapperDocumentRepository>();
        services.AddScoped<IJobRepository, DapperJobRepository>();
        services.AddScoped<SqlMigrationRunner>();

        return services;
    }
}
