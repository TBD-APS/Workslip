using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using StackExchange.Redis;
using Workslip.Api.Configuration;
using Workslip.Api.Helpers;
using Workslip.Application.Common;
using Workslip.Infrastructure.Diagnostics;
using Xunit;

namespace Workslip.Tests.Caching;

public sealed class CacheDiagnosticsTests
{
    [Fact]
    public void Snapshot_exposes_aggregate_metrics_without_cache_keys_or_values()
    {
        var diagnostics = CreateDiagnostics();

        diagnostics.RecordHit(CacheRegionNames.ReferenceData);
        diagnostics.RecordMiss(CacheRegionNames.ReferenceData);
        diagnostics.RecordSet(CacheRegionNames.ReferenceData);
        diagnostics.RecordLoad(CacheRegionNames.ReferenceData, TimeSpan.FromMilliseconds(12.5));
        diagnostics.RecordFailure(CacheRegionNames.ReferenceData);

        var snapshot = diagnostics.GetSnapshot();
        var region = Assert.Single(snapshot.Regions, item => item.Name == CacheRegionNames.ReferenceData);

        Assert.Equal("HybridCache", region.Type);
        Assert.Equal(600, region.TtlSeconds);
        Assert.Equal(1, region.Hits);
        Assert.Equal(1, region.Misses);
        Assert.Equal(1, region.Sets);
        Assert.Equal(1, region.Failures);
        Assert.Equal(1, region.Loads);
        Assert.Equal(12.5, region.AverageLoadDurationMs, precision: 3);
        Assert.NotNull(region.LastActivityAt);
    }

    [Fact]
    public void Snapshot_reports_the_process_local_baseline_before_the_distributed_tier_is_known()
    {
        var snapshot = CreateDiagnostics().GetSnapshot();

        Assert.All(snapshot.Regions, region =>
        {
            Assert.Equal(CacheTier.LocalOnly, region.Tier);
            Assert.Equal(CacheClearScope.ProcessOnly, region.ClearScope);
        });
    }

    [Fact]
    public void Global_clear_marks_every_registered_region_invalidated()
    {
        var diagnostics = CreateDiagnostics();

        diagnostics.RecordGlobalClear();

        var snapshot = diagnostics.GetSnapshot();

        Assert.NotNull(snapshot.LastClearedAt);
        Assert.All(snapshot.Regions, region => Assert.Equal(1, region.Invalidations));
    }

    [Fact]
    public void Regions_report_one_tier_when_no_distributed_cache_is_configured()
    {
        var regions = CreateRegions();
        var described = CacheReach.Describe(
            new CacheDiagnostics(regions).GetSnapshot(),
            DistributedCacheSnapshot.NotConfigured,
            regions);

        Assert.All(described.Regions, region =>
        {
            Assert.Equal(CacheTier.LocalOnly, region.Tier);
            Assert.Equal(CacheClearScope.ProcessOnly, region.ClearScope);
        });
        Assert.Equal(CacheClearScope.ProcessOnly, CacheReach.WidestClearScope(described));
    }

    [Fact]
    public void Only_hybrid_regions_gain_a_distributed_tier_when_a_distributed_cache_is_configured()
    {
        var regions = CreateRegions();
        var described = CacheReach.Describe(new CacheDiagnostics(regions).GetSnapshot(), Configured(), regions);

        var hybrid = Assert.Single(described.Regions, region => region.Name == CacheRegionNames.ReferenceData);
        Assert.Equal(CacheTier.LocalAndDistributed, hybrid.Tier);
        Assert.Equal(CacheClearScope.ProcessAndDistributedTier, hybrid.ClearScope);

        // A region backed by a raw IMemoryCache cannot be widened by any L2
        // registration: it stays per-process in every shape.
        var memory = Assert.Single(described.Regions, region => region.Name == CacheRegionNames.AuthenticatedUsers);
        Assert.Equal(CacheTier.LocalOnly, memory.Tier);
        Assert.Equal(CacheClearScope.ProcessOnly, memory.ClearScope);

        Assert.Equal(CacheClearScope.ProcessAndDistributedTier, CacheReach.WidestClearScope(described));
    }

    [Fact]
    public void A_region_moved_onto_hybrid_cache_is_reported_with_both_tiers()
    {
        var regions = CreateRegions(CacheStoreTypes.Hybrid, CacheEntryReach.StoreTiers);
        var described = CacheReach.Describe(new CacheDiagnostics(regions).GetSnapshot(), Configured(), regions);

        var region = Assert.Single(described.Regions, item => item.Name == CacheRegionNames.AuthenticatedUsers);
        Assert.Equal(CacheTier.LocalAndDistributed, region.Tier);
        Assert.Equal(CacheClearScope.ProcessAndDistributedTier, region.ClearScope);
    }

    /// <summary>
    /// The defect this pins: the store type alone said HybridCache, so the screen
    /// reported <see cref="CacheTier.LocalAndDistributed"/> and
    /// <see cref="CacheClearScope.ProcessAndDistributedTier"/> for the claims region on
    /// any deployment with a reachable Redis — for entries that set
    /// <c>DisableDistributedCacheRead | DisableDistributedCacheWrite</c> on every call
    /// and therefore never touch that tier. The region is registered against
    /// HybridCache either way; what changed is that the reach of its entries is now
    /// declared rather than inferred.
    ///
    /// <para>The reference-data region in the same snapshot is asserted alongside it,
    /// because the distinction has to be per region: an opt-out on one region must not
    /// narrow the report for another.</para>
    /// </summary>
    [Fact]
    public void A_hybrid_region_whose_entries_opt_out_is_reported_without_a_distributed_tier()
    {
        var regions = CreateRegions(CacheStoreTypes.Hybrid, CacheEntryReach.ProcessLocal);
        var described = CacheReach.Describe(new CacheDiagnostics(regions).GetSnapshot(), Configured(), regions);

        var claims = Assert.Single(described.Regions, item => item.Name == CacheRegionNames.AuthenticatedUsers);
        Assert.Equal(CacheStoreTypes.Hybrid, claims.Type);
        Assert.Equal(CacheTier.LocalOnly, claims.Tier);
        Assert.Equal(CacheClearScope.ProcessOnly, claims.ClearScope);

        var referenceData = Assert.Single(described.Regions, item => item.Name == CacheRegionNames.ReferenceData);
        Assert.Equal(CacheTier.LocalAndDistributed, referenceData.Tier);
        Assert.Equal(CacheClearScope.ProcessAndDistributedTier, referenceData.ClearScope);
    }

    /// <summary>
    /// The same rule over the regions the API actually registers, so a region added
    /// later is covered without anyone remembering to extend a test: whatever
    /// <c>BuildCacheRegions</c> declares as <see cref="CacheEntryReach.ProcessLocal"/>
    /// must never be reported with a shared tier or a shared-tier clear, with the
    /// distributed cache configured and answering.
    /// </summary>
    [Fact]
    public void No_registered_region_is_reported_with_a_tier_its_entries_opt_out_of()
    {
        var regions = ServiceConfiguration.BuildCacheRegions(distributedCacheConfigured: true);
        var described = CacheReach.Describe(new CacheDiagnostics(regions).GetSnapshot(), Configured(), regions);

        var processLocal = regions.Where(region => region.EntryReach == CacheEntryReach.ProcessLocal).ToArray();
        Assert.NotEmpty(processLocal);

        foreach (var region in processLocal)
        {
            var reported = Assert.Single(described.Regions, item => item.Name == region.Name);
            Assert.Equal(CacheTier.LocalOnly, reported.Tier);
            Assert.Equal(CacheClearScope.ProcessOnly, reported.ClearScope);
        }
    }

    /// <summary>
    /// The other direction, and the one a future edit is most likely to get wrong: the
    /// declared reach has to match the entry flags the call sites actually pass.
    /// <c>UserClaimsCache.EntryOptions</c> is the ground truth — it is what
    /// DefaultHybridCache reads before deciding whether to touch L2 — so a change that
    /// left the flags in place while declaring the region as using the store's tiers
    /// would fail here instead of quietly overstating the screen again.
    /// </summary>
    [Fact]
    public void The_claims_region_declares_the_reach_its_entry_flags_actually_have()
    {
        var optOut = HybridCacheEntryFlags.DisableDistributedCacheRead
            | HybridCacheEntryFlags.DisableDistributedCacheWrite;

        var entriesOptOut = new[] { UserClaimsCache.EntryOptions, UserClaimsCache.ProbeOptions }
            .All(options => ((options.Flags ?? HybridCacheEntryFlags.None) & optOut) == optOut);

        var claims = Assert.Single(
            ServiceConfiguration.BuildCacheRegions(distributedCacheConfigured: true),
            region => region.Name == CacheRegionNames.AuthenticatedUsers);

        Assert.True(entriesOptOut, "the claims cache entries must disable both halves of the distributed cache");
        Assert.Equal(CacheEntryReach.ProcessLocal, claims.EntryReach);
        Assert.Equal(CacheStoreTypes.Hybrid, claims.Type);
    }

    /// <summary>
    /// A registered L2 that is not answering cannot be marked invalid, so the clear
    /// scope the status endpoint advertises has to follow reachability rather than
    /// configuration. The tier still follows configuration: the region really is
    /// registered as L1 in front of an L2, and the distributed snapshot on the same
    /// response is what says the L2 did not answer.
    /// </summary>
    [Fact]
    public void An_unreachable_distributed_cache_narrows_the_clear_scope_to_this_process()
    {
        var regions = CreateRegions(CacheStoreTypes.Hybrid);
        var described = CacheReach.Describe(
            new CacheDiagnostics(regions).GetSnapshot(),
            Unreachable(),
            regions);

        Assert.All(described.Regions, region =>
        {
            Assert.Equal(CacheClearScope.ProcessOnly, region.ClearScope);
            Assert.Equal(CacheTier.LocalAndDistributed, region.Tier);
        });
        Assert.Equal(CacheClearScope.ProcessOnly, CacheReach.WidestClearScope(described));
    }

    [Fact]
    public void Regions_registered_at_runtime_are_never_reported_wider_than_process_local()
    {
        var regions = CreateRegions();
        var diagnostics = new CacheDiagnostics(regions);
        diagnostics.RecordHit("unregistered-region");

        var described = CacheReach.Describe(diagnostics.GetSnapshot(), Configured(), regions);
        var region = Assert.Single(described.Regions, item => item.Name == "unregistered-region");

        Assert.Equal(CacheStoreTypes.Unknown, region.Type);
        Assert.Equal(CacheTier.LocalOnly, region.Tier);
        Assert.Equal(CacheClearScope.ProcessOnly, region.ClearScope);
    }

    [Fact]
    public void A_clear_never_claims_to_reach_every_replica()
    {
        Assert.False(CacheReach.ClearReachesEveryReplica);
    }

    [Fact]
    public void Clear_message_states_that_only_this_process_is_affected_without_a_distributed_cache()
    {
        var message = CacheReach.DescribeClear(
            "instance-1",
            DistributedCacheSnapshot.NotConfigured,
            distributedTierCleared: false);

        Assert.Contains("instance-1", message, StringComparison.Ordinal);
        Assert.Contains("No distributed cache is configured", message, StringComparison.Ordinal);
        Assert.Contains("every other replica keeps its own copy", message, StringComparison.Ordinal);
        Assert.DoesNotContain("All caches cleared", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The marked-tier message must not imply that a running replica converges when
    /// its local entry expires. Measured: the tag marker leaves the L2 payloads in
    /// place, and a replica that has already read the tag has memoised its
    /// invalidation timestamp for the life of the process, so it reloads the stale
    /// payload rather than discarding it. Only a restart converges it.
    /// </summary>
    [Fact]
    public void Clear_message_does_not_promise_that_running_replicas_converge_when_their_entries_expire()
    {
        var message = CacheReach.DescribeClear("instance-1", Configured(), distributedTierCleared: true);

        Assert.Contains("distributed tier is marked invalid", message, StringComparison.Ordinal);
        Assert.Contains("does not delete the shared payloads", message, StringComparison.Ordinal);
        Assert.Contains("only a restart converges it", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_message_states_that_the_distributed_tier_still_serves_stale_payloads_when_marking_failed()
    {
        var message = CacheReach.DescribeClear("instance-1", Unreachable(), distributedTierCleared: false);

        Assert.Contains("could not be marked invalid", message, StringComparison.Ordinal);
        Assert.Contains("still serves its cached", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_reports_not_configured_when_no_distributed_cache_is_registered()
    {
        var probed = await DistributedCacheProbe.ProbeAsync(null, TimeProvider.System, CancellationToken.None);

        Assert.False(probed.Configured);
        Assert.Equal(DistributedCacheState.NotConfigured, probed.State);
        Assert.Null(probed.Provider);
        Assert.Null(probed.Error);
        Assert.Null(probed.CheckedAt);
    }

    [Fact]
    public async Task Probe_reports_reachable_when_the_distributed_cache_answers_a_read()
    {
        var probed = await DistributedCacheProbe.ProbeAsync(
            new StubDistributedCache(),
            TimeProvider.System,
            CancellationToken.None);

        Assert.True(probed.Configured);
        Assert.Equal(DistributedCacheState.Reachable, probed.State);
        Assert.Equal(nameof(StubDistributedCache), probed.Provider);
        Assert.Null(probed.Error);
        Assert.NotNull(probed.CheckedAt);
    }

    [Fact]
    public async Task Probe_reports_unreachable_with_a_failure_category_when_the_distributed_cache_fails()
    {
        var failing = new StubDistributedCache
        {
            Failure = () => new InvalidOperationException(
                "It was not possible to connect to the redis server(s). password=hunter2, user@example.com"),
        };

        var probed = await DistributedCacheProbe.ProbeAsync(failing, TimeProvider.System, CancellationToken.None);

        Assert.True(probed.Configured);
        Assert.Equal(DistributedCacheState.Unreachable, probed.State);
        Assert.Equal(DistributedCacheProbe.FailureReasons.ConnectionFailed, probed.Error);
        Assert.DoesNotContain("hunter2", probed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user@example.com", probed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(probed.CheckedAt);
    }

    /// <summary>
    /// The security constraint in <c>Docs/operations/CACHE_DIAGNOSTICS.md</c> is that
    /// diagnostics never return the distributed cache's host name, and promise "a
    /// failure reason with no address in it". StackExchange.Redis puts the endpoint in
    /// every connection message, so the guarantee cannot rest on redaction or on where
    /// a length limit happens to cut: nothing from the provider message may reach the
    /// response at all.
    ///
    /// <para><see cref="ProviderConnectionMessage"/> is the real message shape,
    /// captured from Microsoft.Extensions.Caching.StackExchangeRedis 10.0.11 against
    /// an unreachable endpoint. The host in it is a reserved <c>.invalid</c> name, so
    /// this file carries no real address either.</para>
    /// </summary>
    [Fact]
    public async Task No_diagnostics_path_returns_the_distributed_cache_address()
    {
        var failure = new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            ProviderConnectionMessage);

        var probed = await DistributedCacheProbe.ProbeAsync(
            new StubDistributedCache { Failure = () => failure },
            TimeProvider.System,
            CancellationToken.None);
        var fromOutcome = DistributedCacheProbe.FromOutcome(
            new StubDistributedCache(),
            failure,
            TimeProvider.System);

        foreach (var reported in new[] { probed.Error, fromOutcome.Error })
        {
            Assert.Equal(DistributedCacheProbe.FailureReasons.ConnectionFailed, reported);
            Assert.DoesNotContain("example.invalid", reported!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("6380", reported!, StringComparison.Ordinal);
            Assert.DoesNotContain("workslip-cache", reported!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_failure_is_reported_as_one_of_the_closed_reason_vocabulary()
    {
        Assert.Equal(
            DistributedCacheProbe.FailureReasons.TimedOut,
            DistributedCacheProbe.DescribeFailure(new RedisTimeoutException(
                "Timeout performing HGET (1000ms), inst: 0, on cache.example.invalid:6380",
                CommandStatus.Sent)));

        Assert.Equal(
            DistributedCacheProbe.FailureReasons.AuthenticationFailed,
            DistributedCacheProbe.DescribeFailure(new RedisConnectionException(
                ConnectionFailureType.AuthenticationFailure,
                "AuthenticationFailure on cache.example.invalid:6380/Interactive: WRONGPASS")));

        Assert.Equal(
            DistributedCacheProbe.FailureReasons.HostNotResolved,
            DistributedCacheProbe.DescribeFailure(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "It was not possible to connect to the redis server(s). "
                + "No such host is known cache.example.invalid")));

        Assert.Equal(
            DistributedCacheProbe.FailureReasons.HostNotResolved,
            DistributedCacheProbe.DescribeFailure(new SocketException((int)SocketError.HostNotFound)));

        Assert.Equal(
            DistributedCacheProbe.FailureReasons.ConnectionFailed,
            DistributedCacheProbe.DescribeFailure(new AggregateException(
                new RedisConnectionException(ConnectionFailureType.SocketFailure, ProviderConnectionMessage))));

        // Only the exception's type name is added, which is an identifier and so
        // cannot be an address; the message it carried is not.
        var unexpected = DistributedCacheProbe.DescribeFailure(
            new InvalidOperationException("odd failure talking to cache.example.invalid:6380"));
        Assert.StartsWith(DistributedCacheProbe.FailureReasons.Unexpected, unexpected, StringComparison.Ordinal);
        Assert.EndsWith("(InvalidOperationException)", unexpected, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", unexpected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The log-side rendering of the same classification. It has to keep an operator
    /// able to tell the four categories apart — a name that will not resolve, a
    /// connection that was refused, a timeout, a rejected credential — while carrying
    /// none of the provider text those categories were read out of.
    /// </summary>
    [Fact]
    public void A_cache_failure_logged_on_a_cache_path_carries_the_category_and_no_provider_text()
    {
        var rendered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HostNotResolved"] = DistributedCacheProbe.DescribeFailureForLog(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "It was not possible to connect to the redis server(s). "
                + "No such host is known workslip-cache.example.invalid")),
            ["ConnectionFailed"] = DistributedCacheProbe.DescribeFailureForLog(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                ProviderConnectionMessage)),
            ["TimedOut"] = DistributedCacheProbe.DescribeFailureForLog(new RedisTimeoutException(
                "Timeout performing HGET (1000ms), inst: 0, on workslip-cache.example.invalid:6380",
                CommandStatus.Sent)),
            ["AuthenticationFailed"] = DistributedCacheProbe.DescribeFailureForLog(new RedisConnectionException(
                ConnectionFailureType.AuthenticationFailure,
                "AuthenticationFailure on workslip-cache.example.invalid:6380/Interactive: WRONGPASS")),
        };

        foreach (var (category, line) in rendered)
        {
            Assert.StartsWith(category, line, StringComparison.Ordinal);
            Assert.DoesNotContain("example.invalid", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("6380", line, StringComparison.Ordinal);
            Assert.DoesNotContain("anna.jensen", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth:user", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WRONGPASS", line, StringComparison.OrdinalIgnoreCase);
        }

        // The four categories stay distinguishable from one another.
        Assert.Equal(4, rendered.Values.Distinct(StringComparer.Ordinal).Count());

        // Everything else is Unexpected plus the exception type, which is an
        // identifier and so cannot be an address or a key.
        Assert.Equal(
            "Unexpected (InvalidOperationException)",
            DistributedCacheProbe.DescribeFailureForLog(
                new InvalidOperationException("odd failure talking to workslip-cache.example.invalid:6380")));
    }

    /// <summary>
    /// The claims cache invalidator's own failure path, through the real type: the
    /// keys it removes contain the user's e-mail address, so the log line it produces
    /// when a removal throws must carry a category and neither the exception nor a
    /// key.
    /// </summary>
    [Fact]
    public void The_claims_cache_invalidator_logs_a_category_and_never_the_exception()
    {
        var logger = new CapturingLogger<UserClaimsCacheInvalidator>();
        var invalidator = new UserClaimsCacheInvalidator(
            new ThrowingMemoryCache(),
            new CacheDiagnostics(CreateRegions()),
            logger);

        invalidator.Invalidate("00000000-0000-0000-0000-000000000001", "anna.jensen@example.com", null);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Contains("Unexpected (ObjectDisposedException)", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("anna.jensen", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth:user", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Characterization plus containment for the one cache-failure log path this code
    /// does not own. Registering an L2 turns on HybridCache's own
    /// CacheBackendReadFailure/CacheBackendWriteFailure logging, and the first half of
    /// this test is what makes that concrete: the library's message templates are
    /// clean, but the exception it attaches is the provider's, and the provider quotes
    /// the key of the failed operation next to the endpoint it could not reach. With a
    /// claims-shaped key that is an e-mail address and a cache address in one line, at
    /// <see cref="LogEventLevel.Error"/>, which the <c>"Microsoft": "Warning"</c>
    /// override in appsettings.json does not hold back.
    ///
    /// <para>The second half is the containment:
    /// <see cref="ServiceConfiguration.DistributedCacheFailureLogFilter"/> removes those
    /// two events and nothing else. If a package upgrade renumbers or renames them, the
    /// first half still passes and the second fails — which is the signal to update the
    /// filter rather than to discover the leak in a log.</para>
    /// </summary>
    [Fact]
    public async Task HybridCaches_own_backend_failure_logging_carries_the_key_and_is_filtered_out()
    {
        var unfiltered = new CollectingSink();
        await ReadThroughAFailingDistributedCacheAsync(unfiltered, filter: null);

        var backendFailures = unfiltered.Events
            .Where(logEvent => EventName(logEvent) is "CacheBackendReadFailure" or "CacheBackendWriteFailure")
            .ToArray();

        Assert.NotEmpty(backendFailures);
        Assert.All(backendFailures, logEvent =>
        {
            Assert.Equal(HybridCacheLoggerCategory, SourceContext(logEvent));
            Assert.Equal(LogEventLevel.Error, logEvent.Level);

            // The leak, stated as an assertion: the template is clean and the attached
            // exception is not.
            Assert.DoesNotContain("auth:user", logEvent.MessageTemplate.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("auth:user:email:anna.jensen@example.com", logEvent.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("workslip-cache.example.invalid:6380", logEvent.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
        });

        var filtered = new CollectingSink();
        await ReadThroughAFailingDistributedCacheAsync(
            filtered,
            new ServiceConfiguration.DistributedCacheFailureLogFilter());

        Assert.DoesNotContain(
            filtered.Events,
            logEvent => EventName(logEvent) is "CacheBackendReadFailure" or "CacheBackendWriteFailure");
        Assert.All(filtered.Events, logEvent =>
        {
            var rendered = Render(logEvent);
            Assert.DoesNotContain("anna.jensen", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.invalid", rendered, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// The filter reaches the pipeline through the <c>.ReadFrom.Services(services)</c>
    /// call in <c>LoggingConfiguration.ConfigureLogging</c>, which is the only channel
    /// available: <c>UseSerilog</c> replaces the container's <c>ILoggerFactory</c> with
    /// <c>SerilogLoggerFactory</c>, so a <c>builder.Logging.AddFilter</c> on the same
    /// category is silently ignored. This pins the channel, and that the filter is
    /// scoped to the two events rather than to the category or a level: a sibling event
    /// on the same category, and the same event id on another category, both survive.
    /// </summary>
    [Fact]
    public void The_backend_failure_filter_is_delivered_through_the_service_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogEventFilter>(new ServiceConfiguration.DistributedCacheFailureLogFilter());
        using var serviceProvider = services.BuildServiceProvider();

        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .ReadFrom.Services(serviceProvider)
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);

        var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, ProviderConnectionMessage);
        var hybrid = factory.CreateLogger(HybridCacheLoggerCategory);
        hybrid.LogError(new EventId(6, "CacheBackendReadFailure"), failure, "Cache backend read failure.");
        hybrid.LogError(new EventId(7, "CacheBackendWriteFailure"), failure, "Cache backend write failure.");
        hybrid.LogError(
            new EventId(5, "MaximumKeyLengthExceeded"),
            "Cache key maximum length exceeded (maximum: {MaxLength}, actual: {KeyLength}).",
            1024,
            2000);
        factory.CreateLogger("CacheAdministration")
            .LogWarning(new EventId(6, "CacheBackendReadFailure"), "an unrelated logger keeps its events");

        Assert.Collection(
            sink.Events,
            logEvent => Assert.Equal("MaximumKeyLengthExceeded", EventName(logEvent)),
            logEvent => Assert.Equal("CacheAdministration", SourceContext(logEvent)));
    }

    [Fact]
    public async Task Probe_gives_up_on_a_distributed_cache_that_ignores_the_cancellation_token()
    {
        var hanging = new StubDistributedCache { Hang = TimeSpan.FromSeconds(30) };

        var probed = await DistributedCacheProbe.ProbeAsync(
            hanging,
            TimeProvider.System,
            CancellationToken.None,
            timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(DistributedCacheState.Unreachable, probed.State);
        Assert.Equal(DistributedCacheProbe.FailureReasons.TimedOut, probed.Error);
    }

    /// <summary>
    /// The status endpoint is polled every 15 seconds, so the timeout must not
    /// outlive the request. The earlier
    /// <c>Task.WhenAny(probe, Task.Delay(timeout, CancellationToken.None))</c> started
    /// a timer that nothing could cancel once the probe won the race; this asserts the
    /// timer is created through the injected <see cref="TimeProvider"/> and disposed.
    /// </summary>
    [Fact]
    public async Task A_probe_that_answers_disposes_its_timeout_timer()
    {
        var timeProvider = new CountingTimeProvider();

        var probed = await DistributedCacheProbe.ProbeAsync(
            new StubDistributedCache { Hang = TimeSpan.FromMilliseconds(25) },
            timeProvider,
            CancellationToken.None,
            timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(DistributedCacheState.Reachable, probed.State);
        Assert.Equal(1, timeProvider.TimersCreated);

        // Task.WaitAsync stops its timer on a continuation, so disposal is ordered
        // after the await rather than inside it. Polling keeps the property pinned
        // without depending on when the thread pool runs that continuation - the
        // straight assertion passed alone and failed under full-suite load.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (timeProvider.TimersDisposed == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, timeProvider.TimersDisposed);
    }

    /// <summary>
    /// <c>RedisCache</c> prepends the configured <c>InstanceName</c>
    /// (<c>workslip:{environment}:</c>), so a hand-written <c>workslip:</c> prefix
    /// produced the double-prefixed key
    /// <c>workslip:development:workslip:diagnostics:distributed-cache-probe</c>.
    /// </summary>
    [Fact]
    public async Task The_probe_key_does_not_repeat_the_instance_prefix()
    {
        var cache = new StubDistributedCache();

        await DistributedCacheProbe.ProbeAsync(cache, TimeProvider.System, CancellationToken.None);

        var key = Assert.Single(cache.ReadKeys);
        Assert.Equal("diagnostics:distributed-cache-probe", key);
        Assert.DoesNotContain("workslip", key, StringComparison.Ordinal);
    }

    [Fact]
    public void A_completed_cache_operation_reports_the_state_it_demonstrated()
    {
        var cache = new StubDistributedCache();

        var succeeded = DistributedCacheProbe.FromOutcome(cache, null, TimeProvider.System);
        Assert.Equal(DistributedCacheState.Reachable, succeeded.State);

        var failed = DistributedCacheProbe.FromOutcome(
            cache,
            new InvalidOperationException("redis unavailable"),
            TimeProvider.System);
        Assert.Equal(DistributedCacheState.Unreachable, failed.State);
        Assert.StartsWith(
            DistributedCacheProbe.FailureReasons.Unexpected,
            failed.Error ?? string.Empty,
            StringComparison.Ordinal);

        Assert.Equal(
            DistributedCacheSnapshot.NotConfigured,
            DistributedCacheProbe.FromOutcome(null, null, TimeProvider.System));
    }

    /// <summary>
    /// Characterization test for the installed Microsoft.Extensions.Caching.Hybrid
    /// package, because the whole point of the reporting above is that it must not
    /// claim more than the package delivers. A shared L2 makes reads shared and
    /// records the tag invalidation for processes that start later, but it does not
    /// invalidate a running replica's in-process L1: each process reads a tag's
    /// invalidation timestamp from L2 at most once and memoises it for its lifetime.
    ///
    /// If the first assertion below starts failing, the package has gained
    /// cross-replica invalidation and <see cref="CacheReach.ClearReachesEveryReplica"/>
    /// — along with the wording the superadmin screen shows — must be revisited.
    /// </summary>
    [Fact]
    public async Task A_tag_clear_on_one_replica_does_not_invalidate_another_replicas_local_cache()
    {
        var sharedTier = new SharedDistributedStore();
        var replicaA = CreateHybridCache(sharedTier);
        var replicaB = CreateHybridCache(sharedTier);
        var options = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(5),
            LocalCacheExpiration = TimeSpan.FromMinutes(5),
        };

        await replicaA.GetOrCreateAsync(
            "key", _ => new ValueTask<string>("first"), options, tags: [CacheTagNames.All]);

        var warmedFromSharedTier = await replicaB.GetOrCreateAsync(
            "key", _ => new ValueTask<string>("replica-b-own"), options, tags: [CacheTagNames.All]);
        Assert.Equal("first", warmedFromSharedTier);

        await Task.Delay(50);
        await replicaA.RemoveByTagAsync(CacheTagNames.All);
        await Task.Delay(50);

        var replicaBAfterClear = await replicaB.GetOrCreateAsync(
            "key", _ => new ValueTask<string>("second"), options, tags: [CacheTagNames.All]);
        Assert.Equal("first", replicaBAfterClear);

        // The shared tier *was* marked invalid, so a process that starts after the
        // clear discards the payload it finds there.
        Assert.Contains(sharedTier.Keys, key => key.Contains(CacheTagNames.All, StringComparison.Ordinal));
        var replicaStartedAfterTheClear = await CreateHybridCache(sharedTier).GetOrCreateAsync(
            "key", _ => new ValueTask<string>("third"), options, tags: [CacheTagNames.All]);
        Assert.Equal("third", replicaStartedAfterTheClear);
    }

    /// <summary>
    /// StackExchange.Redis' own connection message, captured from
    /// Microsoft.Extensions.Caching.StackExchangeRedis 10.0.11 against an unreachable
    /// endpoint. The host is a reserved <c>.invalid</c> name: the point of the test
    /// that uses it is that no part of this text reaches a diagnostics response.
    /// </summary>
    private const string ProviderConnectionMessage =
        "No connection is active/available to service this operation: "
        + "HGET workslip:development:diagnostics:distributed-cache-probe; "
        + "UnableToConnect on workslip-cache.example.invalid:6380/Interactive, "
        + "Initializing/NotStarted, last: NONE, origin: BeginConnectAsync, outstanding: 0, "
        + "last-read: 0s ago, last-write: 0s ago, keep-alive: 60s, state: Connecting, "
        + "mgr: 10 of 10 available, last-heartbeat: never, global: 0s ago, v: 2.7.27.49176, "
        + "clientName: workslip-api";

    /// <summary>
    /// The category HybridCache logs on, measured from the installed package rather
    /// than derived from a type name.
    /// </summary>
    private const string HybridCacheLoggerCategory = "Microsoft.Extensions.Caching.Hybrid.HybridCache";

    /// <summary>
    /// Reads a claims-shaped key through a real <see cref="HybridCache"/> whose L2
    /// throws the provider's own connection exception, with the library's logging
    /// routed into <paramref name="sink"/>.
    /// </summary>
    private static async Task ReadThroughAFailingDistributedCacheAsync(
        CollectingSink sink,
        ILogEventFilter? filter)
    {
        var configuration = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink);
        if (filter is not null)
        {
            configuration.Filter.With(filter);
        }

        using var serilog = configuration.CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging();
        // Registered after AddLogging so it is the ILoggerFactory that Logger<T> - and
        // therefore DefaultHybridCache's own logger - is built from.
        services.AddSingleton<ILoggerFactory>(new SerilogLoggerFactory(serilog));
        services.AddMemoryCache();
        services.AddSingleton<IDistributedCache>(new StubDistributedCache
        {
            FailureForKey = ProviderConnectionFailure
        });
        services.AddHybridCache();

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<HybridCache>().GetOrCreateAsync(
            "auth:user:email:anna.jensen@example.com",
            _ => new ValueTask<string>("payload"),
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags: [CacheTagNames.All]);
    }

    private static string? SourceContext(LogEvent logEvent) => ScalarText(logEvent, "SourceContext");

    private static string? EventName(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue("EventId", out var value) || value is not StructureValue eventId)
        {
            return null;
        }

        return eventId.Properties
            .Where(property => property.Name == "Name")
            .Select(property => (property.Value as ScalarValue)?.Value as string)
            .FirstOrDefault();
    }

    private static string? ScalarText(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;

    private static string Render(LogEvent logEvent) =>
        logEvent.RenderMessage() + " " + logEvent.Exception;

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    /// <summary>An IMemoryCache in the state a disposed host leaves behind.</summary>
    private sealed class ThrowingMemoryCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => throw new ObjectDisposedException(nameof(ThrowingMemoryCache));

        public void Dispose()
        {
        }

        public void Remove(object key) => throw new ObjectDisposedException(nameof(ThrowingMemoryCache));

        public bool TryGetValue(object key, out object? value) =>
            throw new ObjectDisposedException(nameof(ThrowingMemoryCache));
    }

    /// <summary>
    /// The provider's connection failure for one key, in the shape
    /// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> 10.0.11 produces against
    /// an unreachable endpoint: the command and the full prefixed key, then the
    /// endpoint. The host is a reserved <c>.invalid</c> name, so this file still
    /// carries no real address.
    /// </summary>
    private static RedisConnectionException ProviderConnectionFailure(string key) =>
        new(
            ConnectionFailureType.UnableToConnect,
            "No connection is active/available to service this operation: "
            + $"HGET workslip:development:{key}; "
            + "UnableToConnect on workslip-cache.example.invalid:6380/Interactive, "
            + "Initializing/NotStarted, last: NONE, origin: BeginConnectAsync, outstanding: 0, "
            + "clientName: workslip-api");

    private static DistributedCacheSnapshot Configured() => new(
        true,
        DistributedCacheState.Reachable,
        "RedisCache",
        null,
        DateTimeOffset.UtcNow);

    private static DistributedCacheSnapshot Unreachable() => new(
        true,
        DistributedCacheState.Unreachable,
        "RedisCache",
        DistributedCacheProbe.FailureReasons.TimedOut,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// Both halves of what decides a region's reported reach are parameters, because
    /// they are independent and the reporting has to stay correct for every
    /// combination <c>ServiceConfiguration</c> can declare: the store the
    /// authenticated-user region is registered against, and whether its entries use
    /// that store's shared tier.
    /// </summary>
    private static CacheRegionDefinition[] CreateRegions(
        string authenticatedUsersStore = CacheStoreTypes.Memory,
        CacheEntryReach authenticatedUsersReach = CacheEntryReach.StoreTiers) =>
    [
        new CacheRegionDefinition(CacheRegionNames.ReferenceData, CacheStoreTypes.Hybrid, 600),
        new CacheRegionDefinition(
            CacheRegionNames.AuthenticatedUsers,
            authenticatedUsersStore,
            3600,
            authenticatedUsersReach)
    ];

    private static CacheDiagnostics CreateDiagnostics(
        string authenticatedUsersStore = CacheStoreTypes.Memory,
        CacheEntryReach authenticatedUsersReach = CacheEntryReach.StoreTiers) =>
        new(CreateRegions(authenticatedUsersStore, authenticatedUsersReach));

    private static HybridCache CreateHybridCache(SharedDistributedStore sharedTier)
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton<IDistributedCache>(new SharedTierDistributedCache(sharedTier));
        services.AddHybridCache();

        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    /// <summary>Stands in for the L2 that every simulated replica shares.</summary>
    private sealed class SharedDistributedStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _values.Keys;

        public byte[]? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, byte[] value) => _values[key] = value;

        public void Remove(string key) => _values.TryRemove(key, out _);
    }

    private sealed class SharedTierDistributedCache(SharedDistributedStore store) : IDistributedCache
    {
        public byte[]? Get(string key) => store.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => store.Set(key, value);

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Counts the timers a probe creates and disposes, so a timeout that outlives the
    /// request it bounded is a test failure rather than invisible churn.
    /// </summary>
    private sealed class CountingTimeProvider : TimeProvider
    {
        private int _created;
        private int _disposed;

        public int TimersCreated => Volatile.Read(ref _created);

        public int TimersDisposed => Volatile.Read(ref _disposed);

        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _created);

            return new CountingTimer(
                TimeProvider.System.CreateTimer(callback, state, dueTime, period),
                () => Interlocked.Increment(ref _disposed));
        }

        private sealed class CountingTimer(ITimer inner, Action onDispose) : ITimer
        {
            private int _alreadyDisposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

            public void Dispose()
            {
                inner.Dispose();
                Count();
            }

            public ValueTask DisposeAsync()
            {
                Count();
                return inner.DisposeAsync();
            }

            private void Count()
            {
                if (Interlocked.Exchange(ref _alreadyDisposed, 1) == 0)
                {
                    onDispose();
                }
            }
        }
    }

    private sealed class StubDistributedCache : IDistributedCache
    {
        private readonly ConcurrentQueue<string> _readKeys = new();

        public Func<Exception>? Failure { get; init; }

        /// <summary>
        /// A failure that depends on the key, so a test can reproduce the provider's
        /// real behaviour of quoting the key of the operation that failed.
        /// </summary>
        public Func<string, Exception>? FailureForKey { get; init; }

        public TimeSpan? Hang { get; init; }

        /// <summary>The keys the probe actually asked for, prefix included.</summary>
        public IReadOnlyCollection<string> ReadKeys => _readKeys;

        public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            _readKeys.Enqueue(key);

            if (FailureForKey is not null)
            {
                throw FailureForKey(key);
            }

            if (Failure is not null)
            {
                throw Failure();
            }

            if (Hang is { } hang)
            {
                // Deliberately ignores the token: a cache client that does not honour
                // cancellation must still not hang the diagnostics endpoint.
                await Task.Delay(hang, CancellationToken.None);
            }

            return null;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) => Task.CompletedTask;
    }
}
