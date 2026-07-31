# Implementation Plan: Manuscript Drafting

**Branch**: `001-manuscript-drafting` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-manuscript-drafting/spec.md`

## Summary

InkWell is an offline-first, cross-platform desktop/mobile app for novelists to draft
chapters, assemble them into a manuscript, write in a distraction-free live-rendering
markdown editor, track daily word-count goals, and keep character/plot-thread notes — with
everything stored locally, encrypted at rest, and nothing leaving the device except through
user-initiated EPUB/PDF export.

**Technical approach**: A .NET 10 MAUI app in clean-architecture layers. The editor is
**CodeMirror 6** with an Obsidian-style live-preview layer, hosted in MAUI's **`HybridWebView`**,
with markdown as the storage buffer and inline images embedded as data-URIs. Persistence is a
single **SQLite + SQLCipher** database (`sqlite-net-sqlcipher`) keyed from **`SecureStorage`**,
using WAL + `synchronous=NORMAL` + debounced auto-save to bound crash loss; image bytes live in a
dedicated BLOB table so the manuscript stays self-contained and the auto-save hot path stays fast.
Export uses **Markdig** → manual **EPUB** (`ZipArchive`) and **PdfSharp 6 + MigraDoc** for PDF, both
with embedded images, saved via the **MAUI Community Toolkit `FileSaver`**. Word counting and daily
rollover are pure domain services. See [research.md](./research.md) for decisions and rationale.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (LTS)

**Primary Dependencies**: .NET MAUI; CommunityToolkit.Mvvm; CommunityToolkit.Maui (`FileSaver`,
`FolderPicker`); `sqlite-net-sqlcipher` (SQLitePCLRaw `bundle_e_sqlcipher`); Markdig; PdfSharp 6 +
MigraDoc; CodeMirror 6 (bundled JS/CSS assets under `Resources/Raw/wwwroot`)

**Storage**: Single local SQLite database encrypted with SQLCipher (AES-256), key held in MAUI
`SecureStorage`; WAL journal mode; inline images stored as rowid-addressed BLOBs in a dedicated table

**Testing**: xUnit (Domain/Application unit tests; Infrastructure integration tests against a real
keyed SQLite file in a temp dir); in-memory fakes for `IKeyStore`/secure storage; keyboard-only +
screen-reader/contrast accessibility checks per user story; EPUBCheck validation of exported EPUBs

**Target Platform**: Windows, macOS (Mac Catalyst), iOS, Android — via MAUI

**Project Type**: Cross-platform desktop + mobile app (MAUI) over shared clean-architecture libraries

**Performance Goals**: UI feedback ≤16 ms (60 fps); keystroke echo perceived as instantaneous in a
150,000-word / 50+-chapter manuscript (SC-004); distraction-free enter/exit <1 s (SC-006); no blocking
I/O on the UI thread

**Constraints**: Fully offline for 100% of capabilities (SC-002); encrypted at rest incl. image bytes
(FR-016); nothing leaves the device except user-initiated export (FR-017); auto-save loses ≤ the last
few seconds of typing on unexpected shutdown (FR-004, SC-003); WCAG 2.1 AA throughout, no status by
color alone (FR-019)

**Scale/Scope**: Single local user per device; manuscripts up to 150k+ words across 50+ chapters;
four user stories (P1 draft/organize, P2 distraction-free, P2 goals, P3 characters/plot threads) plus
cross-cutting privacy/export/accessibility

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature MUST comply with the InkWell Constitution v1.1.0. Mandatory gates:

- [x] **Clean Architecture**: Four layers — `InkWell.Domain` (entities + pure domain services),
  `InkWell.Application` (use cases + repository/service interfaces), `InkWell.Infrastructure`
  (SQLCipher, SecureStorage, markdown, export), `InkWell.Maui` (Views/ViewModels). Dependencies point
  inward; infrastructure is referenced only through interfaces. Editor JS assets are isolated in `wwwroot`.
- [x] **Test-Driven**: Every component has planned tests — domain services (word count, daily rollover,
  goal met/exceeded, chapter ordering) unit-tested; repositories integration-tested against real keyed
  SQLite; one integration test per user story; export and accessibility tests. Red-Green-Refactor per §II.
- [x] **WCAG 2.1 AA**: Native MAUI UI uses `SemanticProperties`; the WebView editor exposes
  `role="textbox"` + semantic HTML + ARIA live regions, with a native-`Editor` accessibility-mode fallback.
  Keyboard operability (incl. distraction-free toggle and chapter reorder) and no-color-alone status are
  acceptance criteria for every story (FR-019, SC-007).
- [x] **.NET Stack**: .NET 10 + MAUI throughout; native/JS code confined to the editor WebView with
  documented rationale (no native control does live inline markdown at scale).
- [x] **Performance**: ≤16 ms UI target; all store/export I/O off the UI thread; per-chapter editor loading;
  debounced auto-save. Validated by performance tests against a 150k-word manuscript (SC-004, SC-006).
- [x] **Documentation**: Per-project READMEs, XML doc comments on public APIs (auto-generated API docs),
  code comments on the live-preview layer and crypto/auto-save logic, and user-facing help for editor,
  goals, and export.
- [x] **PR Workflow**: Work proceeds on `001-manuscript-drafting`; no direct commits to `main`; PRs carry
  description, linked issues, test results, and doc updates; review approval required before merge.
- [x] **Data Privacy**: No content leaves the device except user-initiated export (FR-017); no cloud,
  telemetry, or analytics in scope; view/export/delete controls for all data; privacy tests assert no
  unexpected egress. Documented in spec §Data Privacy & User Consent.
- [x] **Storage**: Local-first single encrypted SQLite database is the source of truth; fully offline;
  cloud sync explicitly out of scope (spec §Cloud Sync — Not applicable).

**Result**: PASS — no violations. Complexity Tracking table left empty.

*Post-Design re-check (after Phase 1)*: PASS. The data model, contracts, and quickstart preserve layer
boundaries (all persistence/export behind Application interfaces), keep every capability offline and
encrypted, and carry accessibility + tests through each user story. No new violations introduced; no
entries added to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-manuscript-drafting/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — technical decisions & rationale
├── data-model.md        # Phase 1 output — entities, relationships, validation
├── quickstart.md        # Phase 1 output — runnable validation scenarios
├── contracts/           # Phase 1 output — application-layer service/repository contracts
│   ├── README.md
│   ├── manuscript-service.md
│   ├── chapter-editor-bridge.md
│   ├── word-count-and-goals.md
│   ├── reference-service.md
│   └── export-service.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── InkWell.Domain/                 # Entities + pure domain services (no framework deps)
│   ├── Entities/                   # Manuscript, Chapter, InlineImage, Character, PlotThread,
│   │                               #   DailyGoal, DailyWritingRecord
│   ├── Services/                   # ProseWordCounter, DailyProgressCalculator, ChapterOrdering,
│   │                               #   GoalEvaluator
│   └── Abstractions/               # Value objects, domain result types
├── InkWell.Application/            # Use cases + interfaces (ports)
│   ├── Abstractions/               # IManuscriptRepository, IChapterRepository,
│   │                               #   IReferenceRepository, IWritingHistoryRepository,
│   │                               #   IKeyStore, IExportService, IMarkdownService, IClock
│   └── UseCases/                   # Manuscript/Chapter/Goal/Reference/Export orchestration
├── InkWell.Infrastructure/         # Adapters (implements Application interfaces)
│   ├── Persistence/                # SQLCipher connection factory, repositories, migrations, WAL/autosave
│   ├── Security/                   # SecureStorage-backed IKeyStore, key bootstrap
│   ├── Markdown/                   # Markdig-based IMarkdownService (HTML/XHTML, AST word-count support)
│   └── Export/                     # EpubExporter (ZipArchive), PdfExporter (PdfSharp/MigraDoc)
├── InkWell.Presentation/           # MAUI class library — the testable half of the presentation layer
│   ├── ViewModels/                 # MVVM (CommunityToolkit.Mvvm)
│   ├── Services/                   # Navigation, confirmation, error presentation, platform storage
│   └── Controls/                   # HybridWebView editor host + C# bridge, native Editor a11y fallback
└── InkWell.Maui/                   # MAUI app host
    ├── Views/                      # Library, Manuscript, Editor, Goals, Characters, PlotThreads,
    │                               #   DataControls (XAML pages binding to InkWell.Presentation)
    ├── Resources/Raw/wwwroot/      # CodeMirror 6 bundle: index.html, editor.js, live-preview.js, styles
    ├── Resources/Styles/           # WCAG AA contrast tokens, focus visuals, text-bearing status styles
    └── Platforms/                  # iOS/Android/Windows/MacCatalyst (entitlements, font resolver)

Note (Phase 4): ViewModels, services, and the editor host live in `InkWell.Presentation` rather than
inside the app project. A MAUI *application* project cannot be referenced from another MAUI-enabled
project — its single-project asset pipeline re-processes the app icon in the consumer and fails on
duplicate output names — so this split is what makes story-level ViewModel tests possible at all.

tests/
├── InkWell.Domain.Tests/          # Unit: word count, daily rollover, goal met/exceeded, ordering
├── InkWell.Application.Tests/     # Unit: use cases with faked repositories/clock/keystore
├── InkWell.Infrastructure.Tests/  # Integration: real keyed SQLite round-trips; EPUB/PDF export + EPUBCheck
└── InkWell.Maui.UiTests/          # Per-story integration + keyboard-only / accessibility checks
```

**Structure Decision**: Clean-architecture multi-project solution. `Domain` and `Application` are pure
.NET libraries with no MAUI/native dependencies, making the core (word counting, daily-goal logic,
ordering, use-case orchestration) fully unit-testable without a device. `Infrastructure` holds the only
code that touches SQLCipher, SecureStorage, Markdig, and the export libraries, all behind Application
interfaces. `InkWell.Maui` is the presentation host; the CodeMirror editor lives as isolated web assets in
`Resources/Raw/wwwroot` bridged through a single `HybridWebView` control. This layout directly serves the
constitution's separation-of-concerns, testability, and local-first mandates.

## Complexity Tracking

> No constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
