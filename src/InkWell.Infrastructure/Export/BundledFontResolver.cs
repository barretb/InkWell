using System.Collections.Concurrent;
using System.Reflection;
using PdfSharp.Fonts;

namespace InkWell.Infrastructure.Export;

/// <summary>
/// Supplies PDFsharp with font bytes embedded in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp's Core build does not read system fonts. Without a resolver it finds no typeface at all
/// on iOS and Android and the exported PDF comes out empty of text — the single highest risk in the
/// export plan (research.md §3). Embedded resources are the only font source guaranteed to exist on
/// every target: no file system access, no MAUI asset pipeline, and nothing for iOS AOT to trim
/// away.
/// </para>
/// <para>
/// The face set is deliberately small. A novel needs a text face and a heading face, so the two
/// bundled OpenSans weights cover the whole document, and every family name PDFsharp asks about
/// resolves onto one of them rather than failing. Requests for bold or italic that the bundle has
/// no real face for fall back to the nearest weight instead of returning null, because a missing
/// face in PDFsharp is not a degraded style — it is an exception in the middle of an export.
/// </para>
/// </remarks>
public sealed class BundledFontResolver : IFontResolver
{
    /// <summary>The family name the exporter asks for body text.</summary>
    public const string BodyFamily = "InkWell Text";

    /// <summary>The family name the exporter asks for headings.</summary>
    public const string HeadingFamily = "InkWell Heading";

    private const string RegularFace = "InkWell#Regular";
    private const string SemiboldFace = "InkWell#Semibold";

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>The shared resolver instance.</summary>
    /// <remarks>
    /// PDFsharp holds one global resolver, so this is a singleton and every export goes through the
    /// same byte cache rather than re-reading two manifest streams per document.
    /// </remarks>
    public static BundledFontResolver Instance { get; } = new();

    /// <summary>
    /// Installs this resolver as PDFsharp's global resolver, once per process.
    /// </summary>
    /// <remarks>
    /// PDFsharp throws if the resolver is replaced after fonts have been resolved, so the exporters
    /// call this before touching a document and it is safe to call repeatedly.
    /// </remarks>
    public static void EnsureInstalled()
    {
        if (!ReferenceEquals(GlobalFontSettings.FontResolver, Instance))
        {
            GlobalFontSettings.FontResolver = Instance;
        }
    }

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // A heading, or any bold request, gets the semibold face; everything else gets regular.
        // Italic has no bundled face, so it resolves to the same bytes: PDFsharp will not synthesise
        // one, and a slanted-looking absence is better than an exception mid-export.
        bool wantsHeavy = bold
            || string.Equals(familyName, HeadingFamily, StringComparison.OrdinalIgnoreCase);

        return new FontResolverInfo(wantsHeavy ? SemiboldFace : RegularFace);
    }

    /// <inheritdoc />
    public byte[]? GetFont(string faceName)
        => Cache.GetOrAdd(faceName, static name => ReadEmbedded(
            name == SemiboldFace ? "OpenSans-Semibold.ttf" : "OpenSans-Regular.ttf"));

    private static byte[] ReadEmbedded(string fileName)
    {
        Assembly assembly = typeof(BundledFontResolver).Assembly;
        string resource = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The bundled font '{fileName}' is missing from {assembly.GetName().Name}. " +
                "Without it, PDF export renders no text on iOS or Android (research.md §3).");

        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
