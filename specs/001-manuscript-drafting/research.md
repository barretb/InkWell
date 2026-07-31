# Phase 0 Research: Manuscript Drafting

**Feature**: `001-manuscript-drafting` | **Date**: 2026-07-30

This document resolves the technical unknowns in the implementation plan. The
constitution fixes the platform (C# / .NET 10 / MAUI, clean architecture,
local-first + encrypted, WCAG 2.1 AA), so research focused on the three highest-risk
choices this spec forces: (1) a live-inline-rendering markdown editor, (2) an
encrypted local-first store, and (3) EPUB + PDF export — all running fully offline
in-process on Windows, macOS, iOS, and Android.

---

## 1. Live-inline-rendering markdown editor

**Decision**: Host the editor in MAUI's **`HybridWebView`** serving local assets from
`Resources/Raw/wwwroot`, running **CodeMirror 6** (`@codemirror/lang-markdown`) with an
Obsidian-style **live-preview decoration layer** that hides markdown tokens and renders
inline formatting/images on every line except the one holding the cursor. Markdown is the
editor's document buffer and the storage format (no lossy WYSIWYG round-trip). Inline
images are inserted as `data:` URIs in the markdown on paste/drop. One CodeMirror instance
is loaded **per chapter** (never the whole manuscript). JS↔C# bridge uses
`HybridWebView.InvokeDotNet` / `SendRawMessage` (JS→C#) and `InvokeJavaScriptAsync` /
`EvaluateJavaScriptAsync` (C#→JS).

**Rationale**:
- **Scale is the deciding factor.** CodeMirror 6 virtualizes to the viewport and stays
  responsive at line counts where ProseMirror-based editors lag; with per-chapter loading a
  single instance holds ~2–5k words, leaving large headroom against the 150k-word / 50-chapter
  target (SC-004).
- **Lossless storage.** In CM6 the document *is* the markdown text
  (`view.state.doc.toString()` / `EditorState.create({doc})`) — no WYSIWYG-tree → markdown
  serialization that could corrupt a long-lived manuscript.
- **Right host.** `HybridWebView` serves local HTML/JS/CSS as raw assets with a documented
  bidirectional bridge and no ASP.NET/Blazor render-tree overhead. Plain `WebView` lacks the
  structured interop; `BlazorWebView` adds runtime weight for no gain since the editor is
  inherently JS.
- **Images-as-bytes fit markdown natively** via `![alt](data:image/...;base64,...)`, satisfying
  "bytes embedded, not external references" (FR-003a) with no schema invention.

**Alternatives considered**:
- **Milkdown "Crepe" (ProseMirror) in HybridWebView** — truest pure-WYSIWYG (zero visible
  syntax), markdown-first philosophy. *Rejected as primary*: ProseMirror does not virtualize
  (loses the scaling safety margin), markdown is *derived* from the doc (round-trip/serialization
  risk, documented `getMarkdown()` edge-case bugs), and niceties like image-resize use
  non-standard markdown. Reconsider only if "zero visible syntax on the active line" is judged
  more important than raw scale and round-trip safety.
- **Native MAUI custom-drawn editor** — rejected: no realistic path to inline images + live
  markdown at this scale; multi-year effort. MAUI's `Editor` is plaintext only.
- **Syncfusion MAUI Rich Text Editor** — rejected: WYSIWYG over an HTML model, markdown is not
  the storage format. (Syncfusion's MAUI Markdown Viewer is render-only.)
- **TipTap / Lexical** — rejected: markdown is an import/export add-on, not the native model.

**Risks & mitigations**:
- **WCAG AA on mobile screen readers over `contenteditable`** (highest risk). Mitigate: expose
  `role="textbox"` + semantic HTML + ARIA live-region announcements; ensure live-preview
  decorations never remove the underlying text from the accessibility tree; test VoiceOver/TalkBack
  early. Ship a **native MAUI `Editor` "accessibility mode" fallback** (plain markdown source,
  fully SR-native) as cheap insurance and a strong AA story.
- **Live-preview extension is code we own** (no official CM6 package) — budget for cursor/selection
  edge cases and accessibility-tree correctness.
- **`data:` URI bloat** — downscale/re-encode images on insert, cap dimensions; per-chapter
  separation localizes the cost.
- **Bridge is JSON-serialized/async** — sync markdown deltas + debounce; never marshal full base64
  blobs on every keystroke.
- **Three WebView engines** (WebView2 / WKWebView / Android WebView) differ on `contenteditable`,
  IME, and paste — per-platform QA required.

---

## 2. Encrypted local-first data store

**Decision**: **`sqlite-net-sqlcipher`** (SQLite + SQLCipher via SQLitePCLRaw
`bundle_e_sqlcipher`) as the single encrypted store, keyed by a random 256-bit key held in
**MAUI `SecureStorage`** (Keychain / Android Keystore / DPAPI). Enable **`journal_mode=WAL`** +
**`synchronous=NORMAL`** + **`busy_timeout`**, with **debounced auto-save** (commit current chapter
after ~0.5–2 s idle, and force-flush on focus-loss, navigation, and app-lifecycle events). Store
inline image bytes in a dedicated **`Image` table (rowid-addressed BLOB), never inline in the
auto-saved chapter row**, loaded lazily on demand. All access sits behind repository interfaces
(`IManuscriptRepository`, `IWritingHistoryRepository`, `IKeyStore`) in the application layer;
SQLCipher lives only in infrastructure.

**Rationale**:
- `sqlite-net-sqlcipher` is the shortest proven path to a single encrypted store that works on all
  four MAUI targets in .NET 10 (its TFM list covers net10 android/ios/maccatalyst/windows), with the
  key in platform-standard secure storage.
- **WAL + `synchronous=NORMAL`** is durable across app crashes (only OS crash / power loss can drop
  the last transaction) — exactly the "lose no more than the last moments of typing" bound (FR-004,
  SC-003). Each debounced commit is one durable transaction.
- **In-DB image BLOBs in a separate table** keep the manuscript **self-contained** in one encrypted
  file (matches the spec clarification), give transactional consistency and trivially-correct
  export/backup, and — because bytes never touch the frequently auto-saved prose row and are streamed
  on demand — keep the auto-save hot path and manuscript-load query fast.
- Repository abstraction satisfies clean-architecture/TDD; integration tests exercise the *real*
  cipher against a temp keyed DB.

**Alternatives considered**:
- **EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite.Core` + SQLCipher bundle** — strongest runner-up
  (richer LINQ, versioned migrations for the manuscript→chapters/characters/threads model). *Rejected as
  default* for heavier footprint and more iOS AOT/startup considerations. Same native encryption layer,
  so no lock-in — switch if schema evolution/complex queries dominate.
- **Separate encrypted image side-files on disk** — better when images are very many/large, but adds
  bespoke AES-GCM code and DB↔file consistency/orphan risk; rejected for v1 in favor of single-file
  transactional simplicity. Retained as the escape hatch if image volume becomes a bottleneck.
- **Zetetic commercial SQLCipher for .NET** (official support, FIPS) — rejected on commercial licensing
  cost; open-source bundle suffices. Kept in reserve.
- **`SQLite3MC.PCLRaw.bundle`** — viable, more actively released alternative cipher bundle; the fallback
  if `bundle_e_sqlcipher` misbehaves on .NET 10 iOS/Mac Catalyst.

**Rules & risks**:
- **Never reference two SQLitePCLRaw bundles** (e.g. `sqlite-net-pcl` alongside a SQLCipher bundle, or
  non-`.Core` packages) — double-links native SQLite → iOS `e_sqlite3` crash. Enforce a single bundle via
  `Directory.Packages.props`.
- **iOS/Mac Catalyst Keychain entitlements** are the #1 `SecureStorage` pitfall (`MissingEntitlement`);
  Mac Catalyst is notoriously flaky. Prototype on a real Catalyst build early, not just Windows/Android.
- **`sqlite-net-sqlcipher` stable is 1.9.172 (Mar 2024)**; validate its native bundle loads on .NET 10
  iOS/Mac Catalyst in a spike before committing — **retire this risk first**.
- **Key loss = data loss** (Android restore / Keychain quirks) — mitigate with user-facing export/backup
  and explicit "key missing" handling.
- Close connections cleanly and checkpoint on shutdown to avoid un-checkpointed WAL "lost" data.

---

## 3. EPUB + PDF export (with embedded images)

**Decision**:
- **Markdown → XHTML/AST**: **Markdig** (MIT) for both pipelines — `Markdown.ToHtml()` for strings,
  `Markdown.Parse` for AST walking (image rewriting, PDF element mapping, prose word counting).
- **EPUB**: **build it manually** — Markdig XHTML + `System.IO.Compression.ZipArchive` assembling the
  `mimetype` (stored/first) + `META-INF/container.xml` + `.opf` manifest + EPUB3 `nav.xhtml` (+ `toc.ncx`)
  + one XHTML per chapter + `images/`. Extract embedded image bytes to real zip entries and rewrite each
  `<img src>` to the relative path (not data-URIs, which e-readers/EPUBCheck handle poorly).
- **PDF**: **PdfSharp 6 + MigraDoc** (MIT, pure-managed) — walk the Markdig AST and emit MigraDoc
  elements (heading→styled `Paragraph`, image node→`AddImage` from embedded bytes). MigraDoc's flowing-
  document model fits prose.
- **Save to user location**: **`CommunityToolkit.Maui.Storage.FileSaver`** (`FileSaver.Default.SaveAsync`)
  — native "choose destination" dialog on all four targets; `FolderPicker` for per-chapter batch export.
- Whole-manuscript vs per-chapter (FR-018) is the same generator over a different chapter set.

**Rationale**:
- **QuestPDF is out**: as of v2024.3.0 it replaced SkiaSharp with a custom native Skia layer and **dropped
  iOS/Android/MAUI**; a June 2026 maintainer statement confirms no plans to restore it. It throws on mobile
  regardless of its friendly license — a non-starter for a four-platform app.
- **Manual EPUB** = full control over granularity + spec-correct image embedding, zero native deps, no
  licensing questions (BCL only). EPUB is just a structured ZIP.
- **PdfSharp 6 + MigraDoc** is the only *free* PDF stack that runs pure-managed on all four MAUI targets
  (survives iOS AOT / Android), with a flowing-document model ideal for a novel.

**Alternatives considered**:
- **iText 9 + pdfHTML** — technically cleanest (official MAUI-AOT support; direct HTML→PDF would collapse
  EPUB and PDF onto one Markdig→XHTML pipeline). *Rejected as primary on licensing*: AGPLv3 forces full
  source disclosure for a distributed closed-source app, and the commercial license is a significant
  recurring cost. **Escape hatch** if the Markdig-AST→MigraDoc mapping proves too fiddly and a paid license
  is acceptable.
- **QuestPDF** — rejected: no mobile support (see above), despite great API + Community license.
- **PdfSharpCore / MigraDocCore** (Xamarin-era fork) — superseded by mainline PdfSharp 6 with direct MAUI
  support; the fork's image path leaned on an alpha ImageSharp.
- **EpubSharp** — its README warns write support "might not work at all"; too risky. **QuickEPUB** (MIT) is
  the viable prebuilt fallback but offers less control than ~200 lines over `ZipArchive`.
- **IronPDF / Syncfusion / Nutrient** — commercial and/or Chromium-backed (the headless-browser dependency
  we must avoid).

**Risks & mitigations**:
- **PdfSharp fonts on mobile** (highest risk) — the Core build needs a custom `IFontResolver`; bundle the
  TTFs (serif body + heading) and wire the resolver, or text won't render. **Retire in a device spike first.**
- **Images on Android** — feed raw bytes via `MemoryStream`; confirm PNG/JPEG round-trip on device.
- **XHTML well-formedness** — Markdig emits HTML5, not strict XHTML; post-process to self-close void
  elements and add the root namespace, and validate EPUB output with EPUBCheck in CI.
- **EPUB `mimetype`** must be the first, stored (uncompressed) entry — classic gotcha; validate in CI.
- **macOS sandbox** export needs `com.apple.security.files.user-selected.read-write`; Android needs storage
  permission only below API 33.

---

## 4. Resolved cross-cutting decisions

| Unknown | Decision |
|---|---|
| **Language / Framework** | C# 13 on .NET 10 (LTS, GA Nov 2025) with .NET MAUI — mandated by constitution §IV. |
| **Target platforms** | Windows, macOS (Mac Catalyst), iOS, Android — constitution §IV. |
| **Architecture** | Clean architecture: `Domain` (entities + pure domain services) → `Application` (use cases + repository/service interfaces) → `Infrastructure` (SQLCipher, SecureStorage, export, markdown) → `Maui` (Views/ViewModels, MVVM via CommunityToolkit.Mvvm). |
| **Testing** | xUnit for Domain/Application unit tests and Infrastructure integration tests (real keyed SQLite in temp dir); `IKeyStore`/`ISecureStorage` faked in-memory. Keyboard-only + screen-reader/contrast accessibility checks per user story; export validated with EPUBCheck. |
| **Word count** (FR-009, SC-005) | Parse chapter markdown to the Markdig AST; count whitespace-delimited tokens only within literal prose inline nodes, excluding syntax tokens, link/image markup, and image markers. Pure domain service — unit-tested against independent counts. |
| **Daily rollover** (FR-012, SC-005) | Device local-calendar-day boundaries. A pure `DailyProgress` domain service attributes words to the day they were typed; a `DailyWritingRecord` per date forms history. Post-midnight typing counts to the new day. |
| **Performance** (constitution §V, SC-004/SC-006) | UI feedback ≤16 ms; no blocking I/O on the UI thread (all store/export off-thread); per-chapter editor loading; distraction-free enter/exit <1 s. |
| **Auto-save durability** | WAL + `synchronous=NORMAL` + debounced commit; force-flush on lifecycle events. |

All NEEDS CLARIFICATION items from Technical Context are resolved. No open unknowns remain.

---

## 5. Spike results (recorded during implementation)

### 5.1 `sqlite-net-sqlcipher` on .NET 10 — **Windows verified, Apple/Android outstanding** (T012)

Verified on Windows (net10.0, `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11 + `sqlite-net-sqlcipher`
1.9.172) by `InkWell.Infrastructure.Tests`, against a real keyed database rather than a mock:

- `PRAGMA cipher_version` answers, so SQLCipher — not plain SQLite — is the loaded provider.
- The database opens with the stored key and reopens with the same key after a close.
- A different key fails to open the file.
- The raw file on disk contains neither chapter prose, chapter/manuscript titles, nor embedded
  image bytes, and does not begin with the `SQLite format 3` magic string.
- `journal_mode=WAL`, `synchronous=NORMAL` and `foreign_keys=ON` are all in effect, and
  `wal_checkpoint(TRUNCATE)` empties the WAL.

**Still outstanding**: the same checks on iOS, Mac Catalyst, and Android. They need a device or
simulator and could not be run on the Windows development host. Until they are, the fallback plan
(`SQLite3MC.PCLRaw.bundle`) stands, and the Keychain-entitlement risk on Apple platforms is
unretired — the entitlements are in place (`Platforms/iOS/Entitlements.plist`,
`Platforms/MacCatalyst/Entitlements.plist`) but unexercised.

One implementation note worth keeping: sqlite-net's `ExecuteNonQuery` treats an unexpected
`SQLITE_ROW` as the error "not an error". Several PRAGMAs answer with a row, so every PRAGMA in
`SqlCipherConnectionFactory` is issued through `ExecuteScalar`.

### 5.2 `HybridWebView` bridge — **not yet verified on any engine** (T013)

The bridge is implemented on both sides (`Controls/EditorHostView.cs`,
`Resources/Raw/wwwroot/editor.js`) and the app compiles for the Windows target, but no round trip
has been observed on WebView2, WKWebView, or Android WebView. The JS side listens on both
`HybridWebView.AddRawMessageListener` and the `HybridWebViewMessageReceived` window event so that it
works regardless of which channel a given MAUI version exposes; which of the two is live on each
engine is one of the things this spike still has to establish.

### 5.3 Packaging decisions made during setup

- **Central package management** required a repository-scoped `NuGet.config`: the build host carried
  a second package source, and CPM refuses to restore with more than one unmapped source.
- `Microsoft.Maui.Controls` is pinned to 10.0.90 rather than the workload manifest's 10.0.20,
  because `CommunityToolkit.Maui` 15.0.0 requires ≥ 10.0.60.
- `net10.0-ios` and `net10.0-maccatalyst` are excluded from the build unless `EnableAppleTargets` is
  set, so the solution builds on a non-Mac host. CI on macOS must set it.
- A MAUI **application** project cannot be referenced from another MAUI-enabled project — the
  single-project asset pipeline re-processes the app icon in the consumer and fails on duplicate
  output names. This was resolved during Phase 4 by extracting the presentation layer into
  `src/InkWell.Presentation`, a MAUI **class library** (no icon or splash assets), which both the
  app and `tests/InkWell.Maui.UiTests` reference. The layer diagram in plan.md gains one project:
  `Domain → Application → {Infrastructure, Presentation} → Maui`.

### 5.4 The editor bridge needs a readiness handshake (bug fix)

Chapter content did not save at all. The cause was a handshake that was designed but never wired
up: the web editor announced itself with `editorReady`, and `EditorHostView` had no case for that
message, so it fell through to `default`. The host therefore pushed `loadChapter` as soon as the
page appeared — before the WebView had finished loading `editor.bundle.js` — and
`SendRawMessage` into a not-yet-loaded page is silently discarded.

The consequence was total rather than partial, because `chapterId` in `editor.js` is only ever set
by `loadChapter`, and `reportContent` guards on it:

```js
function reportContent(type) {
    if (!view || !chapterId) { return; }   // ← every keystroke suppressed, forever
```

So no `contentChanged` was ever sent, autosave had nothing to commit, and nothing reported a
problem. Three lessons went into the fix:

1. **The host waits.** Every send now awaits an `editorReady` handshake, so ordering is guaranteed
   and `loadChapter` cannot be the message that gets dropped.
2. **The editor keeps announcing.** `window.HybridWebView` is injected by the platform and is not
   reliably present the instant the bundle runs, so the announcement repeats until the host replies
   with anything.
3. **Silence is not acceptable.** A bridge that never comes up now raises
   `IEditorHost.BridgeFailed` after 15 seconds, and the editor tells the writer their typing is not
   being saved. FR-004 exists to stop work disappearing; work disappearing *quietly* is the worst
   version of it.

A second announcement is treated as a WebView reload — which Android does freely when the app is
backgrounded — and the open chapter is pushed back in, so the writer does not return to an empty
editor whose keystrokes go nowhere.

### 5.5 Nothing in the app has a save button (bug fix)

The characters, plot-threads, and goal screens shipped with per-item **Save** buttons. That made
them the only places in InkWell where forgetting to press something lost work, directly against
FR-004 ("without requiring an explicit save action"). They are gone; those screens now use a shared
`Debouncer` — the same hold-newest-edit, commit-on-pause, always-flush-on-exit shape as
`AutoSaveCoordinator`, minus the word-count and daily-record transaction that only chapters need.

Two details are deliberate. Half-typed input is not an error worth interrupting anyone for, so an
unparseable goal or an empty name is reported in the status line and simply not written, never in a
dialog. And *creating* a record keeps its button: creation is an act of intent, not an edit that
should happen because you typed.

### 5.6 Distraction-free mode is a layout change, not a re-render (Phase 4)

Focus mode toggles a single `distraction-free` class on the document element; the CodeMirror state,
document, and selection are never touched. That is what makes "return to their exact cursor
position" (SC-006) a property of the design rather than something to restore by hand — and it is
asserted directly: `FakeEditorHost` counts document replacements, and
`DistractionFreeBridgeTests.The_transition_never_replaces_the_document` fails if a transition ever
introduces one.

The keyboard route (`Mod-Shift-F` to enter, `Escape` to leave) is bound inside CodeMirror rather
than as a native accelerator, because a focused WebView consumes key events before MAUI sees them.
It is raised back to the host as `IEditorHost.DistractionFreeToggleRequested`, so the shortcut and
the visible button converge on one method in `EditorViewModel` — FR-008's "both routes behave
identically" holds by construction, not by keeping two implementations in step.
