using System;
using System.Collections.Generic;

namespace ArrTags.Updates;

/// <summary>
/// A bounded, provider-neutral queued unit of update work. It carries the
/// coalescing and single-flight <see cref="WorkItemKey"/>, the reason the item
/// is relevant, and the safe configuration generation observed when the item
/// was enqueued. It never carries an API key, secret lease, credential, provider
/// DTO, path, or unbounded payload, so it is safe to queue, retain, and report
/// as a diagnostic (ADR-005).
/// </summary>
public readonly struct LibraryWorkItem : IEquatable<LibraryWorkItem>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWorkItem"/> struct.
    /// </summary>
    /// <param name="key">The coalescing and single-flight key.</param>
    /// <param name="reason">The bounded reason the item is relevant.</param>
    /// <param name="configurationVersion">The safe configuration generation observed at enqueue time.</param>
    public LibraryWorkItem(WorkItemKey key, LibraryWorkReason reason, long configurationVersion)
    {
        Key = key;
        Reason = reason;
        ConfigurationVersion = configurationVersion;
    }

    /// <summary>
    /// Gets the coalescing and single-flight key.
    /// </summary>
    public WorkItemKey Key { get; }

    /// <summary>
    /// Gets the bounded reason this item is a candidate for update work.
    /// </summary>
    public LibraryWorkReason Reason { get; }

    /// <summary>
    /// Gets the configuration generation observed when the work was enqueued. A
    /// worker must still resolve the current public snapshot and deadline; this
    /// is only a safe freshness hint and never a credential.
    /// </summary>
    public long ConfigurationVersion { get; }

    /// <summary>
    /// Determines whether two items are equal.
    /// </summary>
    /// <param name="left">The left item.</param>
    /// <param name="right">The right item.</param>
    /// <returns><see langword="true"/> when every item field is equal.</returns>
    public static bool operator ==(LibraryWorkItem left, LibraryWorkItem right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two items differ.
    /// </summary>
    /// <param name="left">The left item.</param>
    /// <param name="right">The right item.</param>
    /// <returns><see langword="true"/> when any item field differs.</returns>
    public static bool operator !=(LibraryWorkItem left, LibraryWorkItem right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether this item equals another item.
    /// </summary>
    /// <param name="other">The other item.</param>
    /// <returns><see langword="true"/> when every item field is equal.</returns>
    public bool Equals(LibraryWorkItem other)
    {
        return Key.Equals(other.Key)
            && Reason == other.Reason
            && ConfigurationVersion == other.ConfigurationVersion;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LibraryWorkItem other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Key, Reason, ConfigurationVersion);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant($"{Key} {Reason} v{ConfigurationVersion}");
    }

    /// <summary>
    /// Creates the queued item for a bounded library work hint. The connection is
    /// left unresolved and the unindexed Primary surface is assumed.
    /// </summary>
    /// <param name="hint">The bounded library work hint.</param>
    /// <returns>The queued work item.</returns>
    public static LibraryWorkItem FromHint(in LibraryWorkHint hint)
    {
        return new LibraryWorkItem(WorkItemKey.FromHint(in hint), hint.Reason, hint.ConfigurationVersion);
    }
}
