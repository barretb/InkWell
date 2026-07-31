# Contract: Export & Data Controls

**Serves**: Cross-cutting · FR-016, FR-017, FR-018 · SC-008, SC-009

Interface: `IExportService` (Application layer), implemented by `EpubExporter` (ZipArchive) and
`PdfExporter` (PdfSharp 6 + MigraDoc) over `IMarkdownService` (Markdig). File destination is chosen by
the user via `CommunityToolkit.Maui` `FileSaver`/`FolderPicker`. Export is the **only** outbound path
(FR-017).

## Export (FR-018, SC-009)

| Operation | Behavior & rules |
|---|---|
| `ExportManuscript(manuscriptId, format, destination) → ExportResult` | `format ∈ { Epub, Pdf }`. Renders all chapters in order with **every inline image embedded** in the output file. Returns the written path. |
| `ExportChapter(chapterId, format, destination) → ExportResult` | Same, single chapter (per-chapter granularity, FR-018). |
| `ExportManuscriptAllChapters(manuscriptId, format, folder) → ExportResult[]` | Convenience: one file per chapter into a chosen folder. |

**Embedding rules**
- **EPUB**: image bytes become real zip entries under `images/`; each `<img src>` is rewritten to the
  relative path; `mimetype` is the first, stored (uncompressed) entry; EPUB3 `nav.xhtml` (+ `toc.ncx`).
- **PDF**: image nodes render via MigraDoc `AddImage` from the embedded bytes; bundled TTF fonts via a
  custom `IFontResolver` on mobile.
- Markdown → XHTML/AST via Markdig; XHTML is well-formed (void elements closed).

## Data controls (FR-016, FR-017, FR-018, SC-008)

| Operation | Behavior & rules |
|---|---|
| `GetAllStoredData(...) → DataInventory` | Lists everything stored (manuscripts, chapters, characters, plot threads, goals, history) so writers can view all their data (FR-018). |
| `DeleteManuscriptData(manuscriptId) → void` | Deletes a manuscript and all associated data; confirmed first (FR-005). |
| `DeleteAllData() → void` | Drops all tables and removes the encryption key from `SecureStorage`; no residual content remains (SC-008). Confirmed first. |

## Guarantees & tests

- Nothing is transmitted off-device; export writes only to the user-chosen location (FR-017). A privacy
  test asserts no network egress during any operation (SC-002).
- Local store (incl. image bytes) is encrypted at rest (FR-016) — verified by inspecting the raw DB file
  for absence of plaintext prose.
- **Contract tests**: EPUB validates with EPUBCheck and contains every source image; PDF opens and embeds
  every source image; both at whole-manuscript and single-chapter granularity (SC-009). `DeleteAllData`
  leaves no recoverable content (SC-008).
