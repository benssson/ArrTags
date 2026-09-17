using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArrTags.State;

/// <summary>
/// Serializes and verifies versioned <see cref="StateEnvelope"/> records. Every
/// envelope carries a schema version and a SHA-256 integrity hash over its
/// payload, so a torn or tampered record fails closed rather than being parsed
/// as current state.
/// </summary>
public static class StateEnvelopeCodec
{
    /// <summary>
    /// The current envelope schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Serializes a payload into a versioned, integrity-tagged envelope.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="updatedUtc">The update timestamp.</param>
    /// <param name="terminal">Whether the record is terminal and eligible for retention cleanup.</param>
    /// <returns>The encoded envelope bytes.</returns>
    public static byte[] Serialize<T>(
        StateAuthority authority,
        string kind,
        string recordId,
        T payload,
        DateTimeOffset updatedUtc,
        bool terminal)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        var envelope = new StateEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Kind = kind,
            RecordId = recordId,
            Authority = authority,
            Terminal = terminal,
            UpdatedUtc = updatedUtc,
            PayloadSha256 = ComputeHash(payloadJson),
            PayloadJson = payloadJson,
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
    }

    /// <summary>
    /// Attempts to verify and deserialize an envelope and its payload.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="bytes">The encoded envelope bytes.</param>
    /// <param name="envelope">The parsed envelope when successful.</param>
    /// <param name="payload">The parsed payload when successful.</param>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public static bool TryDeserialize<T>(
        ReadOnlySpan<byte> bytes,
        out StateEnvelope? envelope,
        out T? payload,
        out string reason)
        where T : class
    {
        envelope = null;
        payload = null;

        if (!TryReadEnvelope(bytes, out var parsed, out reason))
        {
            return false;
        }

        if (!string.Equals(parsed!.PayloadSha256, ComputeHash(parsed.PayloadJson), StringComparison.Ordinal))
        {
            reason = "The state record failed its integrity check.";
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<T>(parsed.PayloadJson, SerializerOptions);
        }
        catch (JsonException)
        {
            reason = "The state record payload could not be deserialized.";
            return false;
        }

        if (payload is null)
        {
            reason = "The state record payload was empty.";
            return false;
        }

        envelope = parsed;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Attempts to read only the envelope metadata without validating or
    /// deserializing the payload.
    /// </summary>
    /// <param name="bytes">The encoded envelope bytes.</param>
    /// <param name="envelope">The parsed envelope when successful.</param>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the envelope shape is readable.</returns>
    public static bool TryReadEnvelope(ReadOnlySpan<byte> bytes, out StateEnvelope? envelope, out string reason)
    {
        StateEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StateEnvelope>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            envelope = null;
            reason = "The state record is not valid JSON.";
            return false;
        }

        if (parsed is null)
        {
            envelope = null;
            reason = "The state record is empty.";
            return false;
        }

        if (parsed.SchemaVersion != CurrentSchemaVersion)
        {
            envelope = null;
            reason = "The state record has an incompatible schema version.";
            return false;
        }

        envelope = parsed;
        reason = string.Empty;
        return true;
    }

    private static string ComputeHash(string payloadJson)
    {
        var bytes = Encoding.UTF8.GetBytes(payloadJson);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
