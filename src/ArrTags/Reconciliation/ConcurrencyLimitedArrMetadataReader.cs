using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Concurrency;
using ArrTags.Logging;
using ArrTags.Media;
using ArrTags.Providers;
using Microsoft.Extensions.Logging;

namespace ArrTags.Reconciliation;

/// <summary>
/// Enforces the ADR-004 provider concurrency limits around a provider-neutral
/// reconciliation read. It acquires the configured global and per-connection
/// permits for the resolved connection, delegates the read unchanged, and
/// releases the permits when the read completes. The wrapped reader keeps all
/// provider DTO handling inside the provider layer; this decorator adds only the
/// bounded concurrency boundary. The generic reader parameter gives each provider
/// a distinct closed registration so the DI enumerable registration stays
/// idempotent and unambiguous.
/// </summary>
/// <typeparam name="TReader">The concrete provider-neutral reader to bound.</typeparam>
public sealed class ConcurrencyLimitedArrMetadataReader<TReader> : IArrMetadataReader
    where TReader : IArrMetadataReader
{
    private readonly TReader _inner;
    private readonly ProviderConcurrencyLimiter _limiter;
    private readonly IArrTagsLog<ConcurrencyLimitedArrMetadataReader<TReader>>? _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyLimitedArrMetadataReader{TReader}"/> class.
    /// </summary>
    /// <param name="inner">The provider-neutral reader to bound.</param>
    /// <param name="limiter">The provider concurrency limiter resolved from the current configuration snapshot.</param>
    /// <param name="log">The optional bounded, secret-free provider-boundary log.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ConcurrencyLimitedArrMetadataReader(
        TReader inner,
        ProviderConcurrencyLimiter limiter,
        IArrTagsLog<ConcurrencyLimitedArrMetadataReader<TReader>>? log = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _log = log;
    }

    /// <inheritdoc />
    public ArrProviderKind Kind => _inner.Kind;

    /// <inheritdoc />
    public async Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var lease = await _limiter
            .AcquireAsync(connection.ConnectionId, cancellationToken)
            .ConfigureAwait(false);
        var result = await _inner.ReadAsync(identity, connection, cancellationToken).ConfigureAwait(false);
        LogReadOutcome(connection, result);
        return result;
    }

    /// <summary>
    /// Writes one bounded, secret-free provider-read record. Only the stable
    /// connection identity, the provider kind, and the bounded
    /// <see cref="ArrProviderError"/> code/retryability/message are emitted; the
    /// API key, the request URL, the headers, and any raw provider payload are
    /// never available here (ADR-020 clause 4).
    /// </summary>
    private void LogReadOutcome(ArrConnection connection, ArrMetadataReadResult result)
    {
        if (_log is null)
        {
            return;
        }

        var connectionId = connection.ConnectionId.Value;
        var providerKind = connection.Provider.Kind.ToApiName();
        if (result.Error is { } error)
        {
            if (!_log.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            _log.Write(
                LogLevel.Warning,
                ArrTagsLogEvent.ProviderReadFailed,
                FormattableString.Invariant(
                    $"Provider read failed for connection '{connectionId}' ({providerKind}): {error.Code} ({error.Retryability}). {error.Message}"));
            return;
        }

        if (!_log.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _log.Write(
            LogLevel.Debug,
            ArrTagsLogEvent.ProviderReadSucceeded,
            FormattableString.Invariant(
                $"Provider read completed for connection '{connectionId}' ({providerKind})."));
    }
}
