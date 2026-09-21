using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// The operational limits accepted by ADR-004 and recorded in
/// <c>docs/architecture.md</c> section 12. The defaults are applied on
/// construction and validated at configuration load time.
/// </summary>
public sealed class OperationalLimits
{
    /// <summary>
    /// The default finite provider request timeout in seconds.
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 15;

    private const long Kibibyte = 1024L;
    private const long Mebibyte = 1024L * Kibibyte;
    private const long Gibibyte = 1024L * Mebibyte;
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;

    /// <summary>
    /// The default metadata last-known-good window in minutes (ADR-004).
    /// </summary>
    public const int DefaultMetadataStaleWindowMinutes = 24 * MinutesPerHour;

    /// <summary>
    /// The default maximum inbound webhook request payload size in bytes
    /// (ADR-012).
    /// </summary>
    public const long DefaultWebhookMaxPayloadBytes = 256L * Kibibyte;

    /// <summary>
    /// Gets or sets the maximum number of pending update queue entries.
    /// </summary>
    public int QueueCapacity { get; set; } = 512;

    /// <summary>
    /// Gets or sets the maximum in-flight work per item and image surface.
    /// </summary>
    public int PerItemInFlightWork { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum concurrent provider requests per connection.
    /// </summary>
    public int ProviderConcurrencyPerConnection { get; set; } = 4;

    /// <summary>
    /// Gets or sets the maximum concurrent provider requests across all connections.
    /// </summary>
    public int ProviderConcurrencyGlobal { get; set; } = 8;

    /// <summary>
    /// Gets or sets the maximum concurrent image renders.
    /// </summary>
    public int RenderConcurrency { get; set; } = 2;

    /// <summary>
    /// Gets or sets the finite provider request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    /// <summary>
    /// Gets or sets the number of transient retry attempts.
    /// </summary>
    public int TransientRetryCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets the initial retry backoff in seconds.
    /// </summary>
    public int RetryBackoffInitialSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the retry backoff multiplier.
    /// </summary>
    public int RetryBackoffFactor { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum retry backoff in seconds.
    /// </summary>
    public int RetryBackoffMaxSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum provider JSON response size in bytes.
    /// </summary>
    public long ProviderResponseLimitBytes { get; set; } = 8L * Mebibyte;

    /// <summary>
    /// Gets or sets the maximum inbound webhook request payload size in bytes.
    /// An oversized request is rejected before parsing and produces no work.
    /// </summary>
    public long WebhookMaxPayloadBytes { get; set; } = DefaultWebhookMaxPayloadBytes;

    /// <summary>
    /// Gets or sets the maximum source artifact size in bytes.
    /// </summary>
    public long SourceArtifactLimitBytes { get; set; } = 32L * Mebibyte;

    /// <summary>
    /// Gets or sets the maximum derived artifact size in bytes.
    /// </summary>
    public long DerivedArtifactLimitBytes { get; set; } = 32L * Mebibyte;

    /// <summary>
    /// Gets or sets the maximum decoded image dimension per side in pixels.
    /// </summary>
    public int MaxImageDimensionPixels { get; set; } = 8192;

    /// <summary>
    /// Gets or sets the full-reconciliation batch size in records.
    /// </summary>
    public int ReconciliationBatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the metadata last-known-good window in minutes.
    /// </summary>
    public int MetadataStaleWindowMinutes { get; set; } = DefaultMetadataStaleWindowMinutes;

    /// <summary>
    /// Gets or sets the render work-cache time-to-live in minutes.
    /// </summary>
    public int RenderCacheTtlMinutes { get; set; } = 24 * MinutesPerHour;

    /// <summary>
    /// Gets or sets the render work-cache quota in bytes.
    /// </summary>
    public long RenderCacheQuotaBytes { get; set; } = Gibibyte;

    /// <summary>
    /// Gets or sets the authoritative artifact and provenance storage quota in bytes.
    /// </summary>
    public long ArtifactStorageQuotaBytes { get; set; } = 4L * Gibibyte;

    /// <summary>
    /// Gets or sets the terminal provenance retention in days.
    /// </summary>
    public int TerminalProvenanceRetentionDays { get; set; } = 30;

    /// <summary>
    /// Validates every limit and appends a safe message for each violation.
    /// </summary>
    /// <param name="errors">The bounded error collection to append to.</param>
    public void Validate(ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        AddRangeError(errors, nameof(QueueCapacity), QueueCapacity, 1, 100000);
        AddRangeError(errors, nameof(PerItemInFlightWork), PerItemInFlightWork, 1, 1);
        AddRangeError(errors, nameof(ProviderConcurrencyPerConnection), ProviderConcurrencyPerConnection, 1, 16);
        AddRangeError(errors, nameof(ProviderConcurrencyGlobal), ProviderConcurrencyGlobal, 1, 32);
        AddRangeError(errors, nameof(RenderConcurrency), RenderConcurrency, 1, 8);
        AddRangeError(errors, nameof(RequestTimeoutSeconds), RequestTimeoutSeconds, 1, 120);
        AddRangeError(errors, nameof(TransientRetryCount), TransientRetryCount, 0, 5);
        AddRangeError(errors, nameof(RetryBackoffInitialSeconds), RetryBackoffInitialSeconds, 1, 60);
        AddRangeError(errors, nameof(RetryBackoffFactor), RetryBackoffFactor, 1, 10);
        AddRangeError(errors, nameof(RetryBackoffMaxSeconds), RetryBackoffMaxSeconds, 1, 120);
        AddRangeError(errors, nameof(ProviderResponseLimitBytes), ProviderResponseLimitBytes, 64L * Kibibyte, 64L * Mebibyte);
        AddRangeError(errors, nameof(WebhookMaxPayloadBytes), WebhookMaxPayloadBytes, 4L * Kibibyte, 4L * Mebibyte);
        AddRangeError(errors, nameof(SourceArtifactLimitBytes), SourceArtifactLimitBytes, 64L * Kibibyte, 128L * Mebibyte);
        AddRangeError(errors, nameof(DerivedArtifactLimitBytes), DerivedArtifactLimitBytes, 64L * Kibibyte, 128L * Mebibyte);
        AddRangeError(errors, nameof(MaxImageDimensionPixels), MaxImageDimensionPixels, 512, 16384);
        AddRangeError(errors, nameof(ReconciliationBatchSize), ReconciliationBatchSize, 1, 1000);
        AddRangeError(errors, nameof(MetadataStaleWindowMinutes), MetadataStaleWindowMinutes, 5, 7 * HoursPerDay * MinutesPerHour);
        AddRangeError(errors, nameof(RenderCacheTtlMinutes), RenderCacheTtlMinutes, 1, 30 * HoursPerDay * MinutesPerHour);
        AddRangeError(errors, nameof(RenderCacheQuotaBytes), RenderCacheQuotaBytes, 64L * Mebibyte, 64L * Gibibyte);
        AddRangeError(errors, nameof(ArtifactStorageQuotaBytes), ArtifactStorageQuotaBytes, 256L * Mebibyte, 256L * Gibibyte);
        AddRangeError(errors, nameof(TerminalProvenanceRetentionDays), TerminalProvenanceRetentionDays, 1, 365);

        if (RetryBackoffMaxSeconds < RetryBackoffInitialSeconds)
        {
            errors.Add("Operational limit 'RetryBackoffMaxSeconds' must be greater than or equal to 'RetryBackoffInitialSeconds'.");
        }
    }

    /// <summary>
    /// Creates an independent copy of these limits.
    /// </summary>
    /// <returns>A new <see cref="OperationalLimits"/> with the same values.</returns>
    public OperationalLimits Clone()
    {
        return new OperationalLimits
        {
            QueueCapacity = QueueCapacity,
            PerItemInFlightWork = PerItemInFlightWork,
            ProviderConcurrencyPerConnection = ProviderConcurrencyPerConnection,
            ProviderConcurrencyGlobal = ProviderConcurrencyGlobal,
            RenderConcurrency = RenderConcurrency,
            RequestTimeoutSeconds = RequestTimeoutSeconds,
            TransientRetryCount = TransientRetryCount,
            RetryBackoffInitialSeconds = RetryBackoffInitialSeconds,
            RetryBackoffFactor = RetryBackoffFactor,
            RetryBackoffMaxSeconds = RetryBackoffMaxSeconds,
            ProviderResponseLimitBytes = ProviderResponseLimitBytes,
            WebhookMaxPayloadBytes = WebhookMaxPayloadBytes,
            SourceArtifactLimitBytes = SourceArtifactLimitBytes,
            DerivedArtifactLimitBytes = DerivedArtifactLimitBytes,
            MaxImageDimensionPixels = MaxImageDimensionPixels,
            ReconciliationBatchSize = ReconciliationBatchSize,
            MetadataStaleWindowMinutes = MetadataStaleWindowMinutes,
            RenderCacheTtlMinutes = RenderCacheTtlMinutes,
            RenderCacheQuotaBytes = RenderCacheQuotaBytes,
            ArtifactStorageQuotaBytes = ArtifactStorageQuotaBytes,
            TerminalProvenanceRetentionDays = TerminalProvenanceRetentionDays,
        };
    }

    private static void AddRangeError(ICollection<string> errors, string name, long value, long minimum, long maximum)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(FormattableString.Invariant(
                $"Operational limit '{name}' must be between {minimum} and {maximum}."));
        }
    }
}
