using System.Net.Sockets;
using Microsoft.Extensions.Caching.Distributed;
using Workslip.Application.Common;

namespace Workslip.Infrastructure.Diagnostics;

/// <summary>
/// The category a cache failure is classified into. Everything a diagnostics response
/// or a log line is allowed to say about a cache failure is built from one of these
/// and nothing else: <see cref="DistributedCacheProbe.DescribeFailure"/> renders the
/// response vocabulary, <see cref="DistributedCacheProbe.DescribeFailureForLog"/>
/// renders a log field. Neither rendering carries provider text, which is what keeps
/// the cache host and the cache key out of both.
/// </summary>
public enum CacheFailureCategory
{
    /// <summary>The operation, or the connect it needed, ran out of time.</summary>
    TimedOut,

    /// <summary>The endpoint resolved but refused, dropped or never completed a connection.</summary>
    ConnectionFailed,

    /// <summary>The configured host name could not be resolved.</summary>
    HostNotResolved,

    /// <summary>The cache rejected the credentials it was given.</summary>
    AuthenticationFailed,

    /// <summary>Nothing in the closed vocabulary matched.</summary>
    Unexpected
}

/// <summary>
/// Reports whether a distributed cache (the HybridCache L2) is registered and
/// answering, so an operator can tell an L1-only deployment from L1+L2 instead of
/// reading "HybridCache" and having to guess.
///
/// The probe depends only on <see cref="IDistributedCache"/>: it works with whatever
/// implementation is registered, including none. It never throws for a cache
/// problem — an unreachable cache is a reported state, not a failed diagnostics
/// request — and it is bounded by <see cref="DefaultTimeout"/> even when the
/// implementation ignores the cancellation token, so a hung cache cannot hang the
/// status endpoint that polls it.
///
/// A failure is reported as one of <see cref="FailureReasons"/> and never as provider
/// text. StackExchange.Redis puts the endpoint it could not reach — and the key of
/// the operation that failed — into every connection message. Measured against
/// Microsoft.Extensions.Caching.StackExchangeRedis 10.0.11 with this API's own
/// <c>ConfigurationOptions</c> and an unreachable endpoint:
///
/// <code>
/// No connection is active/available to service this operation:
/// HGET workslip:development:auth:user:email:anna.jensen@example.com;
/// UnableToConnect on 127.0.0.1:1/Interactive, Initializing/NotStarted, ...
/// </code>
///
/// So a single provider message carries both the cache address and an e-mail
/// address, and this cache holds authentication claims, which makes its address a
/// target rather than a diagnostic — see the security constraints in
/// <c>Docs/operations/CACHE_DIAGNOSTICS.md</c>. Redacting after the fact is not
/// enough for that, because it is a guess about where in the message the address
/// sits; the reason is instead constructed here from a closed vocabulary and the
/// provider message is read for classification only.
///
/// The same rule binds the log paths, which is why <see cref="DescribeFailureForLog"/>
/// lives here next to the response rendering rather than at each call site: an
/// <c>ILogger</c> call that passes the exception object logs its message too, and
/// Serilog renders that message into every sink.
/// </summary>
public static class DistributedCacheProbe
{
    /// <summary>
    /// Read-only probe key. It is never written, so the probe cannot alter cache
    /// contents, and a missing key is a successful answer. Deliberately unprefixed:
    /// <c>RedisCache</c> prepends the configured <c>InstanceName</c>
    /// (<c>workslip:{environment}:</c>), so a <c>workslip:</c> here would produce the
    /// double-prefixed key <c>workslip:development:workslip:diagnostics:…</c>.
    /// </summary>
    internal const string ProbeKey = "diagnostics:distributed-cache-probe";

    /// <summary>
    /// How long the status endpoint waits for the cache to answer.
    ///
    /// Measured against <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> 10.0.11
    /// with this API's own <c>ConfigurationOptions</c> and an unreachable endpoint: the
    /// first <c>GetAsync</c> in a process blocks for the client's whole
    /// <c>ConnectTimeout</c> (2035 ms / 2032 ms / 92 ms over three cold runs at
    /// <c>ConnectTimeout = 2000</c>) and every later call fails in ~0.1 ms, because
    /// StackExchange.Redis' <c>BacklogPolicy.FailFast</c> already short-circuits once a
    /// connect has failed. A two-second budget therefore made the first status poll in
    /// each replica wait two seconds for an answer it was going to report as
    /// unreachable anyway. This budget caps that at 750 ms, which is roughly five times
    /// the connect and round trip a healthy in-region cache needs. A cold-but-healthy
    /// cache that overruns it is reported unreachable for one 15-second poll and
    /// reachable on the next, because the connect completes in the background.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// The closed set of failure reasons the diagnostics endpoints may return. It is a
    /// contract in both directions: the Superadmin cache screen maps these exact
    /// strings to Danish copy (<c>describeDistributedFailure</c> in
    /// <c>src/FE/src/features/superadmin/cacheApi.ts</c>) and falls back to a generic
    /// sentence rather than rendering anything it does not recognise. Changing a value
    /// here means changing that map too.
    /// </summary>
    public static class FailureReasons
    {
        public const string TimedOut = "Timed out waiting for the distributed cache.";

        public const string ConnectionFailed = "The distributed cache did not accept a connection.";

        public const string HostNotResolved = "The distributed cache host name could not be resolved.";

        public const string AuthenticationFailed = "The distributed cache rejected the connection credentials.";

        /// <summary>
        /// A prefix: <see cref="DescribeFailure"/> may append the exception's simple type
        /// name in parentheses, which is an identifier and so cannot carry an address.
        /// </summary>
        public const string Unexpected = "The distributed cache returned an unexpected error.";
    }

    public static async Task<DistributedCacheSnapshot> ProbeAsync(
        IDistributedCache? distributedCache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (distributedCache is null)
        {
            return DistributedCacheSnapshot.NotConfigured;
        }

        // The implementation type, not the connection string: it names the provider
        // ("RedisCacheImpl", "MemoryDistributedCache") without exposing where it lives.
        var provider = distributedCache.GetType().Name;
        var probeTimeout = timeout ?? DefaultTimeout;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(probeTimeout);

        Task<byte[]?>? probe = null;

        try
        {
            probe = distributedCache.GetAsync(ProbeKey, timeoutSource.Token);

            // WaitAsync bounds the wait for an implementation that ignores its token,
            // which is the only reason this is not a bare await. Unlike
            // Task.WhenAny(probe, Task.Delay(timeout, CancellationToken.None)) it stops
            // its own timer as soon as the probe answers, so the endpoint the frontend
            // polls every 15 seconds leaves no uncancellable timer behind per request.
            await probe.WaitAsync(probeTimeout, timeProvider);

            return new DistributedCacheSnapshot(
                true,
                DistributedCacheState.Reachable,
                provider,
                null,
                timeProvider.GetUtcNow());
        }
        catch (TimeoutException)
        {
            Observe(probe);
            return Unreachable(provider, FailureReasons.TimedOut, timeProvider);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Observe(probe);
            return Unreachable(provider, FailureReasons.TimedOut, timeProvider);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unreachable(provider, DescribeFailure(exception), timeProvider);
        }
    }

    /// <summary>
    /// The state a distributed-cache operation that has already run just
    /// demonstrated, without spending a second round trip: the operation is the
    /// probe. <paramref name="failure"/> is null when it succeeded.
    /// </summary>
    public static DistributedCacheSnapshot FromOutcome(
        IDistributedCache? distributedCache,
        Exception? failure,
        TimeProvider timeProvider)
    {
        if (distributedCache is null)
        {
            return DistributedCacheSnapshot.NotConfigured;
        }

        var provider = distributedCache.GetType().Name;

        return failure is null
            ? new DistributedCacheSnapshot(
                true,
                DistributedCacheState.Reachable,
                provider,
                null,
                timeProvider.GetUtcNow())
            : Unreachable(provider, DescribeFailure(failure), timeProvider);
    }

    /// <summary>
    /// Classifies a cache failure into one of <see cref="FailureReasons"/>. The
    /// exception's own text decides the category and is never part of the result: a
    /// StackExchange.Redis connection message carries the endpoint it could not
    /// reach, and truncating or regex-redacting such a message is a guess about where
    /// the address sits in it. Anything that logs one of these failures logs
    /// <see cref="DescribeFailureForLog"/> instead of <c>exception.Message</c> and
    /// instead of the exception object, for the same reason.
    /// </summary>
    public static string DescribeFailure(Exception exception) =>
        Classify(exception) switch
        {
            CacheFailureCategory.TimedOut => FailureReasons.TimedOut,
            CacheFailureCategory.ConnectionFailed => FailureReasons.ConnectionFailed,
            CacheFailureCategory.HostNotResolved => FailureReasons.HostNotResolved,
            CacheFailureCategory.AuthenticationFailed => FailureReasons.AuthenticationFailed,
            _ => TypeIdentifier(exception) is { } type
                ? $"{FailureReasons.Unexpected} ({type})"
                : FailureReasons.Unexpected
        };

    /// <summary>
    /// The log-side rendering of the same classification: the category name, plus the
    /// exception's simple type name when that is a bare identifier — for example
    /// <c>HostNotResolved (RedisConnectionException)</c> or
    /// <c>Unexpected (InvalidOperationException)</c>.
    /// </summary>
    /// <remarks>
    /// It is deliberately the category rather than the sentence
    /// <see cref="DescribeFailure"/> returns. Those sentences name the distributed
    /// cache as their subject, which is right in a response about the distributed
    /// cache and wrong in a claims-cache log line, because cached claims never reach
    /// the shared tier (<c>UserClaimsCache.EntryOptions</c>). The log message supplies
    /// the subject and this supplies the category, so an operator can still separate a
    /// name-resolution failure from a refused connection from a timeout from an
    /// authentication failure — with no address, no cache key and no provider text in
    /// any of them.
    /// </remarks>
    public static string DescribeFailureForLog(Exception exception)
    {
        var category = Classify(exception);

        return TypeIdentifier(exception) is { } type ? $"{category} ({type})" : category.ToString();
    }

    /// <summary>
    /// The category a failure falls into. The provider's text is read here and
    /// nowhere else, and nothing read here is returned.
    /// </summary>
    public static CacheFailureCategory Classify(Exception exception)
    {
        var chain = Flatten(exception).ToArray();

        foreach (var candidate in chain)
        {
            switch (candidate)
            {
                case OperationCanceledException or TimeoutException:
                    return CacheFailureCategory.TimedOut;

                case SocketException socket:
                    return socket.SocketErrorCode is SocketError.HostNotFound
                        or SocketError.NoData
                        or SocketError.TryAgain
                        ? CacheFailureCategory.HostNotResolved
                        : CacheFailureCategory.ConnectionFailed;
            }

            var candidateType = candidate.GetType().Name;

            if (candidateType.Contains("Timeout", StringComparison.Ordinal))
            {
                return CacheFailureCategory.TimedOut;
            }

            if (candidateType.Contains("Authentication", StringComparison.Ordinal))
            {
                return CacheFailureCategory.AuthenticationFailed;
            }
        }

        // StackExchange.Redis reports the failure kind inside the message rather than
        // through a type of its own, so the category has to be read out of the text.
        // Nothing that is read here is returned.
        var text = string.Join(' ', chain.Select(candidate => candidate.Message));

        if (ContainsAny(text, "NOAUTH", "WRONGPASS", "AuthenticationFailure", "invalid password", "requires authentication"))
        {
            return CacheFailureCategory.AuthenticationFailed;
        }

        if (ContainsAny(
                text,
                "No such host is known",
                "Name or service not known",
                "nodename nor servname",
                "Temporary failure in name resolution",
                "UnableToResolvePhysicalConnection"))
        {
            return CacheFailureCategory.HostNotResolved;
        }

        if (ContainsAny(text, "Timeout performing", "timed out", "timeout awaiting"))
        {
            return CacheFailureCategory.TimedOut;
        }

        if (ContainsAny(
                text,
                "UnableToConnect",
                "No connection is active",
                "It was not possible to connect",
                "SocketFailure",
                "SocketClosed",
                "connection refused",
                "ConnectionRefused",
                "connection was forcibly closed"))
        {
            return CacheFailureCategory.ConnectionFailed;
        }

        return CacheFailureCategory.Unexpected;
    }

    /// <summary>
    /// The exception's simple type name when it is a bare identifier, otherwise
    /// <see langword="null"/>. Safe to render into a response or a log field: an
    /// identifier cannot contain the dots of a host name or the punctuation of a
    /// connection string.
    /// </summary>
    private static string? TypeIdentifier(Exception exception)
    {
        var name = exception.GetType().Name;

        return IsIdentifier(name) ? name : null;
    }

    /// <summary>The exception and its inner exceptions, breadth first and bounded.</summary>
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        var pending = new Queue<Exception>();
        pending.Enqueue(exception);
        var visited = 0;

        while (pending.Count > 0 && visited < 16)
        {
            var current = pending.Dequeue();
            visited++;
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Enqueue(inner);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Enqueue(inner);
            }
        }
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A bare identifier: ASCII letters, digits and underscores only. A host name
    /// contains dots and a connection string contains punctuation, so a value that
    /// passes this cannot be either.
    /// </summary>
    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static DistributedCacheSnapshot Unreachable(
        string provider,
        string? error,
        TimeProvider timeProvider) =>
        new(true, DistributedCacheState.Unreachable, provider, error, timeProvider.GetUtcNow());

    /// <summary>
    /// Keeps a probe that lost the race from surfacing as an unobserved task
    /// exception once the slow cache finally answers.
    /// </summary>
    private static void Observe(Task? task) =>
        _ = task?.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
