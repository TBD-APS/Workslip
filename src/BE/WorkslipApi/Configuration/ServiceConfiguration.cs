using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Core;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Graph;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using StackExchange.Redis;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Api.Telemetry;
using Workslip.Application;
using Workslip.Application.Common;
using Workslip.Application.Operations;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Api.Configuration;

public static class ServiceConfiguration
{
    private const string RedisConnectionStringKey = "Azure:Redis:ConnectionString";

    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi();
        // Registered before AddHybridCache purely for readability; HybridCache resolves
        // IDistributedCache from the container when it is constructed, not when it is
        // registered, so the two calls are order-independent. The return value is the
        // one thing only this method knows - whether an L2 exists at all. Nothing about
        // the claims cache follows from it: those entries are process-local by
        // construction and carry a single lifetime in every configuration
        // (UserClaimsCache), and the diagnostics screen now reports that region as
        // process-local whether or not an L2 is registered
        // (CacheEntryReach.ProcessLocal in BuildCacheRegions). It is kept as the
        // parameter of BuildCacheRegions so the characterization test that pins "the
        // claims TTL is the same in both configurations" still has both configurations
        // to compare.
        var distributedCacheConfigured = AddDistributedCache(builder);
        builder.Services.AddHybridCache();
        builder.Services.AddMemoryCache();

        // Registered as a service, not just handed to CacheDiagnostics: the region
        // definitions are where CacheEntryReach is declared, and the cache endpoints
        // need them to describe a region's tier without re-deriving it from the store
        // type - which is exactly the mistake this replaces.
        var cacheRegions = BuildCacheRegions(distributedCacheConfigured);
        builder.Services.AddSingleton<IReadOnlyList<CacheRegionDefinition>>(cacheRegions);
        builder.Services.AddSingleton(_ => new CacheDiagnostics(cacheRegions));
        builder.Services.AddSingleton<ICacheDiagnostics>(services => new TelemetryCacheDiagnostics(
            services.GetRequiredService<CacheDiagnostics>(),
            services.GetService<TelemetryClient>()));
        builder.Services.AddSingleton<ILogEventFilter>(new DistributedCacheFailureLogFilter());
        builder.Services.AddScoped<IdempotencyStore>();
        builder.Services.AddScoped<IdempotentMutationService>();
        builder.Services.AddSingleton<CustomerImportFileParser>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

        builder.Services.AddWorkslipApplication();
        builder.Services.AddWorkslipInfrastructure(
            includeHostedServices: !DatabaseStartup.IsOpenApiGeneration(builder.Configuration));

        if (DemoModeConfiguration.IsEnabled(builder.Environment, builder.Configuration))
        {
            // Demo must never send invitations/OTC messages to external recipients.
            // Register last so it replaces the normal ACS implementation for IEmailService.
            builder.Services.AddScoped<IEmailService, DemoEmailService>();
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IApplicationEnvironmentRegistry, WorkslipApplicationEnvironmentRegistry>();
        builder.Services.AddScoped<IControlCenterReadService, ControlCenterReadService>();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("customer-import", httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
            });

            options.AddPolicy("diagnostics-read", httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
            });

            options.AddPolicy("demo-session", httpContext =>
            {
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
            });
        });

        builder.Services.AddSingleton<IJobReportPdfService, JobReportPdfService>();

        builder.Services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        return builder;
    }

    /// <summary>
    /// The cache regions the superadmin diagnostics screen reports on. A region's
    /// <c>Type</c> names the store it is registered against and its
    /// <see cref="CacheEntryReach"/> says how much of that store its entries use;
    /// together they decide whether the region can have a distributed tier at all
    /// (<see cref="CacheReach.TierFor"/>), and whether it actually has one is reported
    /// separately, from the distributed-cache probe. Its TTL is the lifetime a reader
    /// of that region observes.
    /// </summary>
    /// <remarks>
    /// The claims region is <see cref="CacheStoreTypes.Hybrid"/> because that is the
    /// store it is registered against, and <see cref="CacheEntryReach.ProcessLocal"/>
    /// because every one of its calls sets
    /// <c>DisableDistributedCacheRead | DisableDistributedCacheWrite</c> - see
    /// <c>UserClaimsCache.EntryOptions</c>. Reporting it off the store type alone made
    /// the screen claim <see cref="CacheTier.LocalAndDistributed"/> and
    /// <see cref="CacheClearScope.ProcessAndDistributedTier"/> for a region that uses
    /// neither, on any deployment with Redis reachable. Its TTL is
    /// <see cref="UserClaimsCache.Lifetime"/> in every configuration, for the same
    /// reason: nothing about these entries follows from whether an L2 exists.
    /// </remarks>
    internal static CacheRegionDefinition[] BuildCacheRegions(bool distributedCacheConfigured) =>
    [
        new CacheRegionDefinition(CacheRegionNames.ReferenceData, CacheStoreTypes.Hybrid, 600),
        new CacheRegionDefinition(
            CacheRegionNames.AuthenticatedUsers,
            CacheStoreTypes.Hybrid,
            (int)UserClaimsCache.Lifetime.TotalSeconds,
            CacheEntryReach.ProcessLocal)
    ];

    /// <summary>
    /// Registers Redis as the shared L2 behind <c>HybridCache</c>, but only when
    /// <c>Azure:Redis:ConnectionString</c> is configured, and reports which of the two it
    /// did. Without a connection string HybridCache runs L1-only and every replica
    /// caches on its own. Authentication claims do not vary with this at all: they are
    /// cached per process in either configuration - see <see cref="UserClaimsCache"/>
    /// - so only the reference-data and job-list regions gain a shared tier here.
    /// </summary>
    /// <remarks>
    /// Redis is an accelerator and a cross-replica coherence mechanism here, never an
    /// availability dependency. Authentication claims are read through this cache, so a
    /// Redis outage must degrade to L1 rather than fail requests. That is enforced in four
    /// places - two configured here, two inherent to the caching packages:
    /// <list type="number">
    /// <item>Startup never dials Redis. <c>AddStackExchangeRedisCache</c> only adds a
    /// singleton, and <c>RedisCache</c> opens its multiplexer lazily on first use, so an
    /// unreachable Redis cannot stop the host from starting or fail a health probe.</item>
    /// <item>A connection string that cannot be parsed, or that parses without naming an
    /// endpoint, is reported and dropped here rather than thrown, so a bad value in App
    /// Configuration cannot turn into a first-request failure after a green
    /// deployment.</item>
    /// <item><see cref="ConfigurationOptions.AbortOnConnectFail"/> is false, so the
    /// multiplexer keeps reconnecting in the background instead of throwing permanently,
    /// while <see cref="BacklogPolicy.FailFast"/> plus the connect/sync/async timeouts
    /// bound how long any single request can wait on Redis before it gives up.</item>
    /// <item>HybridCache catches every L2 read and write exception (logging
    /// CacheBackendReadFailure/CacheBackendWriteFailure, which
    /// <see cref="DistributedCacheFailureLogFilter"/> drops because the exception they
    /// carry names the cache key and the cache host) and falls back to L1 plus the
    /// underlying data callback, and its tag-invalidation read is itself wrapped in a
    /// try/catch with a hard 4s timeout that fails towards "invalidated" - a cache miss,
    /// never a stale hit.</item>
    /// </list>
    /// One thing an L2 does <b>not</b> buy: cross-replica L1 invalidation.
    /// <c>RemoveByTagAsync</c> writes a <c>__MSFT_HCT__{tag}</c> marker to Redis, but each
    /// process reads a given tag's marker at most once and caches the result for the life
    /// of the process, so a tag invalidated on one replica does not evict L1 copies already
    /// held by its peers. Bounding that staleness is a per-entry
    /// <c>HybridCacheEntryOptions.LocalCacheExpiration</c> decision at each call site, not
    /// something this registration can do for them.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when an <c>IDistributedCache</c> was registered, so callers
    /// can size cache lifetimes and describe the deployment truthfully.
    /// </returns>
    private static bool AddDistributedCache(WebApplicationBuilder builder)
    {
        const string phase = "[STARTUP 06.1] Configure Redis distributed cache (HybridCache L2)";

        var connectionString = builder.Configuration[RedisConnectionStringKey];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Information("{StartupPhase} - SKIPPED (not configured)", phase);
            return false;
        }

        ConfigurationOptions redisOptions;

        try
        {
            redisOptions = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception exception)
        {
            // The configuration key and a failure category, never the exception and
            // never the value. StackExchange.Redis' parse errors quote the token they
            // choked on, and the token is a fragment of the connection string:
            // "Keyword 'passwrod' is not supported. (Parameter 'passwrod')" for a
            // misspelled keyword, and "Keyword 'syncTimeout' requires an integer value;
            // the value 'abc' is not recognised." for a malformed one. Both were
            // measured against StackExchange.Redis 2.7.27.
            Log.Warning(
                "{StartupPhase} - SKIPPED ({ConfigurationKey} could not be parsed: {CacheFailure}; continuing on L1 only)",
                phase,
                RedisConnectionStringKey,
                DistributedCacheProbe.DescribeFailureForLog(exception));
            return false;
        }

        if (redisOptions.EndPoints.Count == 0)
        {
            // "Parsed" is not "usable". ConfigurationOptions.Parse does not require an
            // endpoint and does not throw when it cannot find one - measured against
            // StackExchange.Redis 2.7.27, "host:notaport,password=…" and a bare
            // "password=…" both parse cleanly and return options with an empty
            // EndPoints collection. Registering those would produce a cache that
            // reports itself configured, is described as an L2 by the diagnostics
            // screen, and can never connect: RedisCache would throw on first use, once
            // per operation, on the authenticated path. Not configured is the truthful
            // reading, and it is the same fallback as a missing connection string.
            //
            // What this cannot catch is a connection string whose endpoint is wrong
            // rather than absent - "cache.example.invalid:6380,hunter2" parses to TWO
            // endpoints, the second being "hunter2:0", because a bare token is an
            // endpoint to this parser. That one is a reachability problem, and the
            // distributed-cache probe is what reports it.
            Log.Warning(
                "{StartupPhase} - SKIPPED ({ConfigurationKey} parsed but names no cache endpoint; continuing on L1 only)",
                phase,
                RedisConnectionStringKey);
            return false;
        }

        // Never abort on a failed connect: a cold or restarting Redis must leave the API
        // serving from L1 while the multiplexer reconnects underneath.
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = 2_000;
        redisOptions.ConnectRetry = 3;

        // FailFast is what keeps an outage of an already-connected Redis cheap. The
        // default backlog policy queues commands while the multiplexer is reconnecting
        // and only gives up at AsyncTimeout, so one request that touches the cache four
        // or five times pays that timeout once per operation - measured at ~5s for a
        // single cold-claims request against a stopped Redis. FailFast refuses to enqueue
        // instead, and measured across a 15s kill-and-restart of a Redis this process had
        // already connected to, 161 claims resolutions saw no authentication failure and
        // nothing slower than 200ms. For a cache that is the right trade: a miss costs a
        // database read, a stalled request costs the caller.
        //
        // What FailFast does NOT bound is the very first connect, because there is no
        // connection yet to fail fast against. The first cache-touching request in each
        // process pays ConnectTimeout - measured at 4.086s on the real API against an
        // unreachable Redis with the 2s below (the claims read and the claims write each
        // crossing a cold multiplexer), and 2062ms for a single operation in isolation,
        // after which every further request was under 10ms. That is a known cost, once
        // per process, on an authenticated path, whenever a process starts while the
        // cache is down: a scale-out, a revision rollout, or a Redis restart. Lowering
        // ConnectTimeout, or dialling the multiplexer off the request path at startup,
        // is what would remove it; neither is done here.
        redisOptions.BacklogPolicy = BacklogPolicy.FailFast;

        // The remaining ceiling, for the case where a connection exists but the server
        // has stopped answering.
        redisOptions.SyncTimeout = 1_000;
        redisOptions.AsyncTimeout = 1_000;
        redisOptions.ClientName = "workslip-api";

        // Cached authentication claims live in this store, so environment isolation is a
        // structural guarantee rather than an operator convention: a Development or Demo
        // deployment pointed at the same Redis as Live cannot read or overwrite its keys.
        var instanceName = $"workslip:{builder.Environment.EnvironmentName.ToLowerInvariant()}:";

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisOptions;
            options.InstanceName = instanceName;
        });

        Log.Information(
            "{StartupPhase} - OK ({EndpointCount} endpoint(s), key prefix {KeyPrefix})",
            phase,
            redisOptions.EndPoints.Count,
            instanceName);

        return true;
    }

    /// <summary>
    /// Drops the two log events HybridCache emits itself when its L2 fails a read or a
    /// write, because they are the one cache-failure log path this code does not own -
    /// and the only one that still carries a cache key and a cache address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library's own message templates are clean ("Cache backend read failure.",
    /// "Cache backend write failure."). What leaks is the exception it attaches:
    /// measured against Microsoft.Extensions.Caching.Hybrid 10.6.0 with an unreachable
    /// Redis and a claims-shaped key, the captured event was
    /// <c>[Error] Microsoft.Extensions.Caching.Hybrid.HybridCache (6/CacheBackendReadFailure):
    /// Cache backend read failure.</c> with
    /// <c>RedisConnectionException: No connection is active/available to service this
    /// operation: HGET workslip:development:auth:user:email:anna.jensen@example.com;
    /// UnableToConnect on 127.0.0.1:1/Interactive, …</c> - a cache key holding an
    /// e-mail address and the cache endpoint, in one line, at Error, which the
    /// <c>"Microsoft": "Warning"</c> override in appsettings.json does not hold back.
    /// </para>
    /// <para>
    /// <b>Why a Serilog filter and not <c>builder.Logging.AddFilter</c>.</b> Because
    /// the latter does nothing in this host, which was measured rather than assumed:
    /// <c>UseSerilog</c> replaces the container's <c>ILoggerFactory</c> with
    /// <c>Serilog.Extensions.Logging.SerilogLoggerFactory</c>, whose loggers consult
    /// Serilog's own levels only, so a category filter added through
    /// <c>Microsoft.Extensions.Logging</c> is silently ignored - a control line logged
    /// on a category filtered to <c>LogLevel.None</c> still reached the sink.
    /// This filter is picked up instead by the <c>.ReadFrom.Services(services)</c> call
    /// in <c>LoggingConfiguration.ConfigureLogging</c>, which resolves every registered
    /// <see cref="ILogEventFilter"/>. Removing that call would silently re-open the
    /// leak.
    /// </para>
    /// <para>
    /// <b>What an operator loses.</b> Exactly these two events, and nothing else on the
    /// category: the key-validation, serialization, payload-size and data-rejected
    /// events all still arrive, because the filter matches on the event and not on the
    /// category or a level. What is gone is the per-operation record of an L2 read or
    /// write failing - so a Redis outage no longer produces a line per cache operation.
    /// What remains to see it by: the distributed-cache probe behind
    /// <c>GET /api/superadmin/cache/status</c>, which reports state plus a failure
    /// category, and the clear endpoint's own warning. That is a real narrowing, and
    /// the alternative that would keep the events is to decorate
    /// <c>IDistributedCache</c> so the exception HybridCache attaches is already
    /// scrubbed; it is not taken here because such a decorator has to re-implement
    /// <c>IBufferDistributedCache</c> as well - <c>RedisCache</c> implements it, and
    /// HybridCache picks its zero-copy path off that interface - and losing that
    /// silently is worse than losing two log lines.
    /// </para>
    /// </remarks>
    internal sealed class DistributedCacheFailureLogFilter : ILogEventFilter
    {
        private const string HybridCacheCategory = "Microsoft.Extensions.Caching.Hybrid.HybridCache";

        private static readonly string[] SuppressedEventNames =
            ["CacheBackendReadFailure", "CacheBackendWriteFailure"];

        private static readonly int[] SuppressedEventIds = [6, 7];

        public bool IsEnabled(LogEvent logEvent) => !IsBackendFailure(logEvent);

        private static bool IsBackendFailure(LogEvent logEvent)
        {
            if (!string.Equals(ScalarString(logEvent, "SourceContext"), HybridCacheCategory, StringComparison.Ordinal))
            {
                return false;
            }

            if (!logEvent.Properties.TryGetValue("EventId", out var value) || value is not StructureValue eventId)
            {
                return false;
            }

            // Both the name and the id are matched: the name survives a renumbering and
            // the id survives a rename, and either match is scoped to this one category
            // already, so the worst a version skew can do is drop a sibling event.
            foreach (var property in eventId.Properties)
            {
                var matched = property switch
                {
                    { Name: "Name", Value: ScalarValue { Value: string name } } =>
                        SuppressedEventNames.Contains(name, StringComparer.Ordinal),
                    { Name: "Id", Value: ScalarValue { Value: int id } } => SuppressedEventIds.Contains(id),
                    _ => false
                };

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ScalarString(LogEvent logEvent, string property) =>
            logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: string text }
                ? text
                : null;
    }
}
