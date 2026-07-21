using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workslip.Application;
using Workslip.Application.Common;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Application.Notifications;
using Workslip.Infrastructure.Invitations;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure.Jobs;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Workslip.Infrastructure.Transactions;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Notifications;

namespace Workslip.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkslipInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseRetryPolicy, PollyDatabaseRetryPolicy>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IApplicationTransactionFactory, EfApplicationTransactionFactory>();

        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<SqlDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = SqlConnectionFactory.ResolveConnectionString(configuration);
            options.UseSqlServer(connectionString, b =>
                b.MigrationsAssembly("Workslip.Api"));

            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            options.AddInterceptors(auditInterceptor);

            options.ConfigureWarnings(warnings =>
            warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
        });

        services.AddScoped<IAssignmentRepository, EfAssignmentRepository>();
        services.AddScoped<ICustomerRepository, EfCustomerRepository>();
        services.AddScoped<IInviteRepository, EfInviteRepository>();
        services.AddScoped<IJobLinkRepository, EfJobLinkRepository>();
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IOrganizationRepository, EfOrganizationRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IWorksheetRepository, EfWorksheetRepository>();
        services.AddScoped<IReferenceDataRepository, EfReferenceDataRepository>();
        services.AddScoped<INotificationRepository, EfNotificationRepository>();
        services.AddScoped<IJobViewRepository, EfJobViewRepository>();

        services.AddScoped<IEmailService, AcsEmailService>();
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddHostedService<JobDeletionCleanupService>();
        services.AddHostedService<InviteEntraCleanupService>();
        services.AddHostedService<PushNotificationWorker>();

        services.AddOptions<VapidOptions>()
            .Configure<IConfiguration>((options, config) =>
                config.GetSection(VapidOptions.SectionName).Bind(options));

        return services;
    }
}
