using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Concurrency;

namespace ArrTags.Rendering;

/// <summary>
/// Enforces the ADR-004 render concurrency limit around a provider-neutral
/// renderer. It acquires the configured number of render permits resolved from
/// the current configuration snapshot, delegates the render unchanged, and
/// releases the permits when the render completes. The wrapped renderer keeps
/// its existing bounded decode/layout/output behavior; this decorator adds only
/// the bounded concurrency boundary.
/// </summary>
public sealed class ConcurrencyLimitedRenderer : IRenderer
{
    private readonly IRenderer _inner;
    private readonly DynamicConcurrencyLimiter _limiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyLimitedRenderer"/> class.
    /// </summary>
    /// <param name="inner">The renderer to bound.</param>
    /// <param name="limiter">The render concurrency limiter resolved from the current configuration snapshot.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ConcurrencyLimitedRenderer(IRenderer inner, DynamicConcurrencyLimiter limiter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    }

    /// <inheritdoc />
    public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.RenderAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
