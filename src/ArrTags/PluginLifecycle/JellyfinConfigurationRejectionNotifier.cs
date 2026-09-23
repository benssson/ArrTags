using System;
using System.Collections.Generic;
using System.Text;
using ArrTags.Configuration;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Surfaces a rejected configuration save as exactly one bounded, secret-free
/// administrator-visible activity-log entry through the pinned host
/// <see cref="IActivityManager"/> (ADR-021). The entry has a fixed name and type,
/// is attributed to ArrTags, and is built only from bounded validation reasons,
/// so it never contains a candidate value or a secret. The write is a bounded
/// synchronous wait and never throws into the host; a valid save writes no entry.
/// </summary>
public sealed class JellyfinConfigurationRejectionNotifier : IConfigurationRejectionNotifier
{
    /// <summary>
    /// The fixed activity-log entry type (the ActivityLog column bound is 256
    /// characters).
    /// </summary>
    public const string EntryType = "ArrTagsConfigurationRejected";

    /// <summary>
    /// The fixed activity-log entry name (the ActivityLog column bound is 512
    /// characters).
    /// </summary>
    public const string EntryName = "ArrTags configuration rejected";

    /// <summary>
    /// The bounded number of validation reasons surfaced in one entry.
    /// </summary>
    public const int MaxSurfacedReasons = 8;

    /// <summary>
    /// The default bounded time the save path waits for the activity-log write.
    /// </summary>
    public static readonly TimeSpan DefaultBoundedWait = TimeSpan.FromSeconds(5);

    private const int MaxFieldLength = 512;
    private const int MaxReasonLength = 256;

    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _boundedWait;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinConfigurationRejectionNotifier"/> class.
    /// </summary>
    /// <param name="serviceProvider">The host service provider used to resolve the activity manager lazily.</param>
    /// <exception cref="ArgumentNullException">The service provider is <see langword="null"/>.</exception>
    public JellyfinConfigurationRejectionNotifier(IServiceProvider serviceProvider)
        : this(serviceProvider, DefaultBoundedWait)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinConfigurationRejectionNotifier"/> class
    /// with an explicit bounded wait. This overload exists so the timeout
    /// containment can be tested without a multi-second wait.
    /// </summary>
    /// <param name="serviceProvider">The host service provider used to resolve the activity manager lazily.</param>
    /// <param name="boundedWait">The bounded time to wait for the activity-log write.</param>
    /// <exception cref="ArgumentNullException">The service provider is <see langword="null"/>.</exception>
    public JellyfinConfigurationRejectionNotifier(IServiceProvider serviceProvider, TimeSpan boundedWait)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _boundedWait = boundedWait > TimeSpan.Zero ? boundedWait : DefaultBoundedWait;
    }

    /// <inheritdoc />
    public void NotifyRejected(IReadOnlyList<string> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        try
        {
            if (_serviceProvider.GetService(typeof(IActivityManager)) is not IActivityManager activityManager)
            {
                return;
            }

            var entry = BuildEntry(reasons);

            // The save response must not race the entry, so a bounded synchronous
            // wait is deliberate and consistent with the uninstall drain. The
            // wait and every failure are contained below.
#pragma warning disable CA1849 // The save path cannot await; the wait is bounded.
            activityManager.CreateAsync(entry).WaitAsync(_boundedWait).GetAwaiter().GetResult();
#pragma warning restore CA1849
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A rejection notification is best-effort: the save is already
            // rejected and the last valid configuration is already retained.
        }
    }

    private static ActivityLog BuildEntry(IReadOnlyList<string> reasons)
    {
        var bounded = BoundReasons(reasons);
        return new ActivityLog(EntryName, EntryType, Guid.Empty)
        {
            Overview = Truncate(BuildOverview(bounded, reasons.Count)),
            ShortOverview = Truncate(BuildShortOverview(bounded, reasons.Count)),
            LogSeverity = LogLevel.Warning,
        };
    }

    private static List<string> BoundReasons(IReadOnlyList<string> reasons)
    {
        var bounded = new List<string>(MaxSurfacedReasons);
        foreach (var reason in reasons)
        {
            if (bounded.Count == MaxSurfacedReasons)
            {
                break;
            }

            var sanitized = Sanitize(reason);
            if (sanitized.Length == 0)
            {
                continue;
            }

            bounded.Add(sanitized.Length <= MaxReasonLength ? sanitized : sanitized[..MaxReasonLength]);
        }

        return bounded;
    }

    /// <summary>
    /// Strips control characters and collapses whitespace runs to one space so a
    /// reason cannot inject markup, a line break, or a malformed value into the
    /// activity entry. The secret-free contract is upheld by the single caller,
    /// which passes validator messages only.
    /// </summary>
    /// <param name="reason">The raw reason text.</param>
    /// <returns>The bounded, single-line reason text.</returns>
    private static string Sanitize(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(reason.Length);
        var pendingSpace = false;
        foreach (var character in reason)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string BuildOverview(IReadOnlyList<string> bounded, int total)
    {
        if (bounded.Count == 0)
        {
            return "The saved ArrTags configuration was rejected; the last valid configuration remains active.";
        }

        var builder = new StringBuilder();
        builder.Append("The saved ArrTags configuration was rejected; the last valid configuration remains active. Reasons: ");
        builder.Append(string.Join("; ", bounded));
        var omitted = total - bounded.Count;
        if (omitted > 0)
        {
            builder.Append(" (+").Append(omitted).Append(" more)");
        }

        return builder.ToString();
    }

    private static string BuildShortOverview(IReadOnlyList<string> bounded, int total)
    {
        return bounded.Count == 0
            ? "ArrTags configuration rejected."
            : FormattableString.Invariant(
                $"ArrTags configuration rejected ({total} validation error(s)): {bounded[0]}");
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];
    }
}
