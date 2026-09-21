using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Concurrency;
using ArrTags.Media;
using ArrTags.Providers;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyLimitedArrMetadataReader{TReader}"/> class.
    /// </summary>
    /// <param name="inner">The provider-neutral reader to bound.</param>
    /// <param name="limiter">The provider concurrency limiter resolved from the current configuration snapshot.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ConcurrencyLimitedArrMetadataReader(TReader inner, ProviderConcurrencyLimiter limiter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
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
        return await _inner.ReadAsync(identity, connection, cancellationToken).ConfigureAwait(false);
    }
}
