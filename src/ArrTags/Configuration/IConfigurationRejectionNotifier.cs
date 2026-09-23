using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// The plugin-owned boundary for surfacing a rejected configuration save to the
/// administrator. The contract is deliberately Jellyfin-free: it accepts only
/// bounded, secret-free validation reason strings, so the host activity-log API
/// is isolated in a single implementation (ADR-021).
/// </summary>
/// <remarks>
/// Caller invariant (ADR-021, security finding SEC-9.3-03): the only production
/// caller is <c>Plugin.TryNotifyRejected</c>, which passes the secret-free
/// messages of the configuration validator's result. Reasons must never be a
/// candidate value, a secret value, a lease, a URL, a header, or a request body.
/// Implementations additionally sanitize the reason text (strip control
/// characters and collapse whitespace) and cap the count and length.
/// </remarks>
public interface IConfigurationRejectionNotifier
{
    /// <summary>
    /// Surfaces a rejected configuration save to the administrator. The supplied
    /// reasons are bounded and secret-free. Implementations must never throw into
    /// the host.
    /// </summary>
    /// <param name="reasons">The bounded, secret-free validation reasons.</param>
    void NotifyRejected(IReadOnlyList<string> reasons);
}
