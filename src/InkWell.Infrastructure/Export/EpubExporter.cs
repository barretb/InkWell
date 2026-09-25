using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Infrastructure.Export;

/// <summary>
/// Writes an EPUB 3 by assembling the zip directly (research.md §3).
/// </summary>
/// <remarks>
/// <para>
/// An EPUB is a structured zip, and building it by hand is about two hundred lines with no native
/// dependency, no licence question, and — the reason it was chosen over a library — complete
/// control over image embedding. Every inline image becomes a real zip entry under
/// <c>images/</c> and each <c>&lt;img src&gt;</c> is rewritten to point at it. Data URIs would have
/// been less work and are what the editor holds, but e-readers handle them poorly and EPUBCheck
/// complains, so the bytes are unpacked into files here (SC-009).
/// </para>
/// <para>
/// Two details are the classic ways to produce a file that looks like an EPUB and is not. The
/// <c>mimetype</c> entry must be first in the archive and stored uncompressed, which is why it is
/// written through <see cref="CompressionLevel.NoCompression"/> before anything else. And content
/// documents must be well-formed XML, which HTML5 is not — hence <see cref="IMarkdownService.ToXhtml"/>
/// rather than <c>ToHtml</c>. Both are asserted in <c>EpubExporterTests</c>.
/// </para>
/// </remarks>
public sealed partial class EpubExporter
{
    private readonly IMarkdownService _markdown;

    /// <summary>Creates the exporter.</summary>
    /// <param name="markdown">Renders chapter markdown to well-formed XHTML.</param>
    public EpubExporter(IMarkdownService markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="destinationPath"/> as an EPUB 3.
    /// </summary>
    /// <returns>How many images were embedded, so the caller can report it and a test can check it.</returns>
    public async Task<int> WriteAsync(
        ExportSource source,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // Built in memory and written once. An export that fails half way should not leave a
        // truncated file where the writer asked for their book.
        using var buffer = new MemoryStream();
        int imageCount = Build(source, buffer);

        buffer.Position = 0;
        await using FileStream file = File.Create(destinationPath);
        await buffer.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        return imageCount;
    }

    /// <summary>Assembles the archive into <paramref name="output"/>.</summary>
    /// <returns>The number of images embedded.</returns>
    public int Build(ExportSource source, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        var imageNames = new Dictionary<Guid, string>();
        int imageIndex = 0;
        foreach (InlineImage image in source.AllImages)
        {
            imageNames[image.Id] = $"images/img{++imageIndex:D4}{ExtensionFor(image.MimeType)}";
        }

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteMimetype(archive);
            WriteEntry(archive, "META-INF/container.xml", Container);
            WriteEntry(archive, "OEBPS/content.opf", BuildPackage(source, imageNames));
            WriteEntry(archive, "OEBPS/nav.xhtml", BuildNav(source));
            WriteEntry(archive, "OEBPS/toc.ncx", BuildNcx(source));
            WriteEntry(archive, "OEBPS/styles.css", ReaderStylesheet);

            for (int i = 0; i < source.Chapters.Count; i++)
            {
                WriteEntry(archive, $"OEBPS/{ChapterFileName(i)}", BuildChapter(source.Chapters[i], imageNames));
            }

            foreach (InlineImage image in source.AllImages)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"OEBPS/{imageNames[image.Id]}", CompressionLevel.Optimal);
                using Stream stream = entry.Open();
                stream.Write(image.Bytes, 0, image.Bytes.Length);
            }
        }

        return imageNames.Count;
    }

    /// <summary>
    /// Writes the <c>mimetype</c> entry: first in the archive and stored uncompressed, as the EPUB
    /// specification requires. Getting this wrong produces a file that many readers silently refuse.
    /// </summary>
    private static void WriteMimetype(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using Stream stream = entry.Open();
        byte[] bytes = Encoding.ASCII.GetBytes("application/epub+zip");
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private string BuildChapter(ExportChapter chapter, IReadOnlyDictionary<Guid, string> imageNames)
    {
        string body = _markdown.ToXhtml(chapter.Markdown);
        body = RewriteImageReferences(body, chapter, imageNames);

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{Escape(chapter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css" />
            </head>
            <body>
            <section epub:type="chapter">
            <h1>{Escape(chapter.Title)}</h1>
            {body}
            </section>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Points every <c>&lt;img&gt;</c> at the zip entry holding its bytes.
    /// </summary>
    /// <remarks>
    /// The editor stores images two ways — as <c>inkwell-img://{id}</c> references and, for anything
    /// pasted straight in, as a <c>data:</c> URI. Both have to end up as a relative path, so both
    /// are matched: references by id, and data URIs positionally against the chapter's image list,
    /// which is the order they were embedded in.
    /// </remarks>
    private static string RewriteImageReferences(
        string xhtml,
        ExportChapter chapter,
        IReadOnlyDictionary<Guid, string> imageNames)
    {
        int dataUriSeen = 0;

        return ImageTagPattern().Replace(xhtml, match =>
        {
            Group src = match.Groups["src"];
            string source = src.Value;
            string? replacement = null;

            if (TryParseReference(source, out Guid id) && imageNames.TryGetValue(id, out string? byId))
            {
                replacement = byId;
            }
            else if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && dataUriSeen < chapter.Images.Count)
            {
                replacement = imageNames[chapter.Images[dataUriSeen++].Id];
            }

            if (replacement is null)
            {
                return match.Value;
            }

            // Swap only the attribute's value, by offset, so nothing else in the tag — an alt text
            // containing quotes, a width the writer set — is disturbed.
            int start = src.Index - match.Index;
            return string.Concat(match.Value[..start], replacement, match.Value[(start + src.Length)..]);
        });
    }

    private static bool TryParseReference(string source, out Guid id)
    {
        const string Scheme = "inkwell-img://";
        id = Guid.Empty;
        return source.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(source[Scheme.Length..].Trim('/'), out id);
    }

    private static string BuildPackage(ExportSource source, Dictionary<Guid, string> imageNames)
    {
        var manifest = new StringBuilder();
        var spine = new StringBuilder();

        manifest.AppendLine("""    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />""");
        manifest.AppendLine("""    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />""");
        manifest.AppendLine("""    <item id="css" href="styles.css" media-type="text/css" />""");

        for (int i = 0; i < source.Chapters.Count; i++)
        {
            manifest.AppendLine(
                CultureInfo.InvariantCulture,
                $"""    <item id="ch{i}" href="{ChapterFileName(i)}" media-type="application/xhtml+xml" />""");
            spine.AppendLine(CultureInfo.InvariantCulture, $"""    <itemref idref="ch{i}" />""");
        }

        int index = 0;
        foreach (InlineImage image in source.AllImages)
        {
            manifest.AppendLine(
                CultureInfo.InvariantCulture,
                $"""    <item id="img{index++}" href="{imageNames[image.Id]}" media-type="{Escape(image.MimeType)}" />""");
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:{source.Identifier}</dc:identifier>
                <dc:title>{Escape(source.Title)}</dc:title>
                <dc:language>en</dc:language>
                <meta property="dcterms:modified">{source.ModifiedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}</meta>
              </metadata>
              <manifest>
            {manifest.ToString().TrimEnd()}
              </manifest>
              <spine toc="ncx">
            {spine.ToString().TrimEnd()}
              </spine>
            </package>
            """;
    }

    private static string BuildNav(ExportSource source)
    {
        var items = new StringBuilder();
        for (int i = 0; i < source.Chapters.Count; i++)
        {
            items.AppendLine(
                CultureInfo.InvariantCulture,
                $"""      <li><a href="{ChapterFileName(i)}">{Escape(source.Chapters[i].Title)}</a></li>""");
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{Escape(source.Title)}</title>
            </head>
            <body>
            <nav epub:type="toc" id="toc">
              <h1>Contents</h1>
              <ol>
            {items.ToString().TrimEnd()}
              </ol>
            </nav>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Builds the legacy NCX table of contents.
    /// </summary>
    /// <remarks>
    /// EPUB 3 replaced it with <c>nav.xhtml</c>, but plenty of e-readers in actual use still look
    /// for it first, and it costs a few lines. A book that opens everywhere is worth more than a
    /// strictly minimal package.
    /// </remarks>
    private static string BuildNcx(ExportSource source)
    {
        var points = new StringBuilder();
        for (int i = 0; i < source.Chapters.Count; i++)
        {
            points.AppendLine(
                CultureInfo.InvariantCulture,
                $"""
                  <navPoint id="np{i}" playOrder="{i + 1}">
                    <navLabel><text>{Escape(source.Chapters[i].Title)}</text></navLabel>
                    <content src="{ChapterFileName(i)}" />
                  </navPoint>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="urn:uuid:{source.Identifier}" />
              </head>
              <docTitle><text>{Escape(source.Title)}</text></docTitle>
              <navMap>
            {points.ToString().TrimEnd()}
              </navMap>
            </ncx>
            """;
    }

    private static string ChapterFileName(int index)
        => string.Create(CultureInfo.InvariantCulture, $"chapter{index + 1:D4}.xhtml");

    private static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        _ => ".img",
    };

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private const string Container = """
        <?xml version="1.0" encoding="utf-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
          </rootfiles>
        </container>
        """;

    // Deliberately restrained: an e-reader's own typography and the reader's chosen size should win
    // over anything the exporter imposes. This only keeps images inside the page.
    private const string ReaderStylesheet = """
        body { line-height: 1.5; }
        h1 { page-break-before: always; }
        img { max-width: 100%; height: auto; }
        figcaption { font-size: 0.9em; }
        """;

    // An <img> tag with its src attribute's value captured, so the value can be swapped by offset.
    [GeneratedRegex(
        """<img\b[^>]*?\bsrc\s*=\s*"(?<src>[^"]*)"[^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageTagPattern();
}
