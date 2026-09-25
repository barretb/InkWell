# InkWell.Infrastructure

The only project that touches SQLCipher, platform secure storage, Markdig rendering, and the export
libraries. Everything here implements an interface defined in `InkWell.Application`.

- `Persistence/` — SQLCipher connection factory (WAL, `synchronous=NORMAL`, foreign keys on),
  schema migrations, the repositories, and `DataControlsRepository`, which counts everything stored
  and erases it
- `Security/` — the `SecureStorage`-backed key store and first-run key bootstrap
- `Markdown/` — Markdig-based rendering, including the XHTML pass an EPUB's content documents need
- `Export/` — `ExportService` over `EpubExporter` (hand-built `ZipArchive`) and `PdfExporter`
  (PDFsharp + MigraDoc), plus `BundledFontResolver` and the TrueType faces it embeds

⚠️ Exactly one SQLitePCLRaw bundle may be referenced solution-wide; see the comment in
`Directory.Packages.props`.

## The fonts live here

`Export/Fonts/*.ttf` are embedded resources of this assembly. PDFsharp's Core build reads no system
fonts, so without a resolver handing it real bytes a PDF exported on iOS or Android contains no text
at all — an embedded resource is the only source guaranteed to be present on every target.

`InkWell.Maui` links the same files for its own UI typography rather than keeping a second copy, so
the dependency points inward and the app and the exported book are set in the same faces.
