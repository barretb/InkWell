# InkWell.Infrastructure

The only project that touches SQLCipher, platform secure storage, Markdig rendering, and the export
libraries. Everything here implements an interface defined in `InkWell.Application`.

- `Persistence/` — SQLCipher connection factory (WAL, `synchronous=NORMAL`, foreign keys on),
  schema migrations, and repositories
- `Security/` — the `SecureStorage`-backed key store and first-run key bootstrap
- `Markdown/` — Markdig-based rendering and AST access
- `Export/` — `EpubExporter` (`ZipArchive`) and `PdfExporter` (PDFsharp + MigraDoc)

⚠️ Exactly one SQLitePCLRaw bundle may be referenced solution-wide; see the comment in
`Directory.Packages.props`.
