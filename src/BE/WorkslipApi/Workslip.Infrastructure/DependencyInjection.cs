using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application;
using Workslip.Application.Common;
using Workslip.Application.Customers;
using Workslip.Application.Diagnostics;
using Workslip.Application.Invitations;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Diagnostics;
using Workslip.Infrastructure.Invitations;
using Workslip.Infrastructure.Jobs;
using Workslip.Infrastructure.Notifications;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Workslip.Infrastructure.Transactions;

namespace Workslip.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkslipInfrastructure(
        this IServiceCollection services,
        bool includeHostedServices = true)
    {
        services.AddSingleton<IDatabaseRetryPolicy, PollyDatabaseRetryPolicy>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IApplicationTransactionFactory, EfApplicationTransactionFactory>();

        services.AddScoped<JobStatusTransitionInterceptor>();
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<SqlDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = SqlConnectionFactory.ResolveConnectionString(configuration);
            options.UseSqlServer(connectionString);

            var transitionInterceptor = sp.GetRequiredService<JobStatusTransitionInterceptor>();
            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            options.AddInterceptors(transitionInterceptor, auditInterceptor);

            options.ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
        });

        services.AddScoped<IAssignmentRepository, EfAssignmentRepository>();
        services.AddScoped<ICustomerRepository, EfCustomerRepository>();
        services.AddScoped<IInviteRepository, EfInviteRepository>();
        services.AddScoped<IInvitationStatusRepository, EfInviteRepository>();
        services.AddScoped<IJobLinkRepository, EfJobLinkRepository>();
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IOrganizationRepository, EfOrganizationRepository>();
        services.AddScoped<IOrganizationAdministrationRepository, EfOrganizationRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IWorksheetRepository, EfWorksheetRepository>();
        services.AddScoped<IReferenceDataRepository, EfReferenceDataRepository>();
        services.AddScoped<INotificationRepository, EfNotificationRepository>();
        services.AddScoped<IJobViewRepository, EfJobViewRepository>();
        services.AddScoped<InstallationBaselineProvisioner>();
        services.AddScoped<DevelopmentDatabaseSeeder>();

        services.AddHttpClient<IErrorDiagnosticsService, ApplicationInsightsErrorDiagnosticsService>(client =>
        {
            client.BaseAddress = new Uri("https://api.loganalytics.azure.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IEmailService, AcsEmailService>();
        services.AddSingleton<VapidKeyMaterial>();
        services.AddSingleton<IVapidPublicKeyProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<VapidKeyMaterial>());
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddScoped<PushNotificationProcessor>();

        if (includeHostedServices)
        {
            services.AddHostedService<JobDeletionCleanupService>();
            services.AddHostedService<InviteEntraCleanupService>();
            services.AddHostedService<PushNotificationWorker>();
        }

        services.AddOptions<VapidOptions>()
            .Configure<IConfiguration>((options, config) =>
                config.GetSection(VapidOptions.SectionName).Bind(options));

        return services;
    }
}
