using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ArrTags.Rendering;

/// <summary>
/// The code-owned identity of a bundled renderer font asset. ADR-010 makes the
/// font family, style, version, exact byte length, and SHA-256 part of the
/// renderer identity, so a changed font asset changes the render fingerprint by
/// construction. V1 has exactly one bundled font and no font fallback; the
/// <see cref="BundledDejaVuSansBold"/> instance is the identity the renderer
/// uses, and other instances only express a different policy identity.
/// </summary>
public sealed class RenderFontIdentity
{
    /// <summary>
    /// The stable logical name of the embedded DejaVu Sans Bold 2.37 resource.
    /// </summary>
    public const string DejaVuSansBoldLogicalName = "ArrTags.Resources.DejaVuSans-Bold.ttf";

    /// <summary>
    /// The uppercase SHA-256 of the exact embedded DejaVu Sans Bold 2.37 bytes.
    /// </summary>
    public const string DejaVuSansBoldSha256 = "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895";

    /// <summary>
    /// The exact byte length of the embedded DejaVu Sans Bold 2.37 resource.
    /// </summary>
    public const int DejaVuSansBoldByteLength = 708920;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderFontIdentity"/> class.
    /// </summary>
    /// <param name="family">The font family name.</param>
    /// <param name="style">The font style name.</param>
    /// <param name="version">The font file version.</param>
    /// <param name="byteLength">The exact font asset byte length.</param>
    /// <param name="sha256">The 64-character hexadecimal SHA-256 of the font asset bytes.</param>
    /// <param name="resourceLogicalName">The stable logical name of the embedded resource.</param>
    /// <exception cref="ArgumentException">A required string is empty, or the hash is not a 64-character hexadecimal value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The byte length is not positive.</exception>
    public RenderFontIdentity(
        string family,
        string style,
        string version,
        int byteLength,
        string sha256,
        string resourceLogicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentException.ThrowIfNullOrEmpty(style);
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        ArgumentException.ThrowIfNullOrEmpty(resourceLogicalName);

        if (sha256.Length != 64)
        {
            throw new ArgumentException("A font identity requires a 64-character SHA-256 hex value.", nameof(sha256));
        }

        Family = family;
        Style = style;
        Version = version;
        ByteLength = byteLength;
        Sha256 = sha256;
        ResourceLogicalName = resourceLogicalName;
    }

    /// <summary>
    /// Gets the ADR-010 bundled DejaVu Sans Bold 2.37 identity.
    /// </summary>
    public static RenderFontIdentity BundledDejaVuSansBold { get; } = new RenderFontIdentity(
        "DejaVu Sans",
        "Bold",
        "2.37",
        DejaVuSansBoldByteLength,
        DejaVuSansBoldSha256,
        DejaVuSansBoldLogicalName);

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string Family { get; }

    /// <summary>
    /// Gets the font style name.
    /// </summary>
    public string Style { get; }

    /// <summary>
    /// Gets the font file version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the exact font asset byte length.
    /// </summary>
    public int ByteLength { get; }

    /// <summary>
    /// Gets the hexadecimal SHA-256 of the font asset bytes.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Gets the stable logical name of the embedded font resource.
    /// </summary>
    public string ResourceLogicalName { get; }

    /// <summary>
    /// Gets the deterministic descriptor over every identity field. It is the
    /// value the render fingerprint records for
    /// <see cref="RenderOutputPolicy.FontIdentity"/>, so changing any field,
    /// including the font SHA-256, changes the fingerprint.
    /// </summary>
    public string Descriptor => FormattableString.Invariant(
        $"{Family}|{Style}|{Version}|{ByteLength}|{Sha256}|{ResourceLogicalName}");

    /// <summary>
    /// Opens a new read-only stream over the exact embedded font resource bytes.
    /// </summary>
    /// <returns>A read-only stream positioned at the start of the font bytes.</returns>
    /// <exception cref="InvalidOperationException">The embedded font resource is missing.</exception>
    public Stream OpenResourceStream()
    {
        var stream = typeof(RenderFontIdentity).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceLogicalName);
        if (stream is null)
        {
            throw new InvalidOperationException($"The embedded font resource '{ResourceLogicalName}' was not found.");
        }

        return stream;
    }

    /// <inheritdoc/>
    public override string ToString() => Descriptor;
}
