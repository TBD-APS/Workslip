using Workslip.Data;
using Workslip.Documents;
using Workslip.Migrations;
using Workslip.Jobs;
using Microsoft.Extensions.DependencyInjection;

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
