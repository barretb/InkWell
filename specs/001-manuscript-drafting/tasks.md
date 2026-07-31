# Tasks: Manuscript Drafting

**Input**: Design documents from `/specs/001-manuscript-drafting/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: MANDATORY. Constitution v1.1.0 §II requires tests for EVERY feature, and spec.md §Testing
Requirements specifies unit, integration, accessibility, privacy, export, and performance tests. Tests
are written FIRST and MUST fail before the corresponding implementation task (Red-Green-Refactor).

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and
delivered independently.

## Implementation status

Phases 1–6 (Setup, Foundational, and all four user stories) are implemented; 256 tests pass and the
solution builds. Phases 7–8 — cross-cutting export and data controls, then polish — have not been
started, so FR-018, SC-008, and SC-009 are not yet delivered.

**Two FR-004 bugs found and fixed after Phase 6** (see [research.md](./research.md) §5.4, §5.5):

- **Chapter content did not save at all.** The `editorReady` handshake was designed but never wired
  up on the host side, so `loadChapter` was pushed before the WebView had loaded and was discarded;
  the editor never learned its chapter id and suppressed every change. Fixed with a real handshake,
  a repeating readiness announcement from the web side, and — because the failure was silent — a
  `BridgeFailed` path that tells the writer their typing is not being saved.
- **Three screens had save buttons.** Characters, plot threads, and the daily goal required an
  explicit save, which FR-004 forbids and which made them the only places where forgetting cost you
  your work. They now autosave through a shared `Debouncer`; only *creating* a record keeps a
  button. `AutoSaveEverywhereTests` and `EditorBridgeFailureTests` are the regression cover.

**Structural change made during Phase 4**: the presentation layer was extracted from the app project
into `src/InkWell.Presentation`, a MAUI class library holding the ViewModels, prompt/navigation
services, and the editor host. A MAUI *application* project cannot be referenced from another
MAUI-enabled project — its single-project asset pipeline re-processes the app icon in the consumer
and fails on duplicate output names — so without this, no story could be tested at the ViewModel
level. `tests/InkWell.Maui.UiTests` now drives the real ViewModels against a real encrypted
database, substituting only the editor surface (`IEditorHost`) and the three prompt services.

Deviations and known gaps inside the completed phases:

- **T012 / T013 spikes are only partly retired.** SQLCipher is verified on Windows against a real
  keyed database; iOS, Mac Catalyst, and Android need a device. The `HybridWebView` bridge has not
  been exercised on any engine. See [research.md](./research.md) §5.
- **Accessibility is verified only where code can decide it.** Keyboard-only completion, text-carried
  state, and grammatical announcements are covered by tests for US2, US3, and US4. Contrast ratios
  and real screen-reader output still need a device pass — that is what keeps T034, T054, T071,
  T082, T096, and T106 open. The presentation extraction has unblocked T054, which no longer needs a
  device.
- **T050 and T098 have no dedicated files in `InkWell.Application.Tests`.** The manuscript, chapter,
  and reference use cases — validation, `NotFound` handling, and freeform notes included — are
  covered end to end by `InkWell.Infrastructure.Tests` against a real encrypted database rather than
  by unit tests over fakes. That project currently holds only the shared fakes.
- **T070 (native `Editor` accessibility fallback) is not built.**
- **T082 and T096 are open only for their device half.** The code-level gap T075 exposed — the Shell
  navigation bar staying visible in focus mode — was found and fixed (`Shell.NavBarIsVisible`).
  US3's day rollover *is* verified automatically, by advancing an injected clock past midnight; what
  remains for T096 is contrast and screen-reader verification on a device.
- **`RecordWordsForToday` is deliberately absent from `GoalUseCases`** (contracts/word-count-and-goals.md
  lists it). Recording a day's words happens inside the autosave transaction so prose and its day's
  total cannot diverge; a second, non-transactional write path for the same fact would be a way for
  them to. `IWritingHistoryRepository.AddWordsAsync` remains for tooling and shares the same upsert.
- The solution file is `InkWell.slnx` (the .NET 10 default), not `InkWell.sln`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4); Setup/Foundational/Cross-cutting/Polish
  tasks carry no story label
- Exact file paths are included in every task

## Path Conventions

Clean-architecture multi-project solution per [plan.md](./plan.md) §Project Structure:

- `src/InkWell.Domain/`, `src/InkWell.Application/`, `src/InkWell.Infrastructure/`,
  `src/InkWell.Presentation/` (ViewModels, services, editor host), `src/InkWell.Maui/` (app host,
  Views, composition root)
- `tests/InkWell.Domain.Tests/`, `tests/InkWell.Application.Tests/`, `tests/InkWell.Infrastructure.Tests/`,
  `tests/InkWell.Maui.UiTests/`
- Editor web assets: `src/InkWell.Maui/Resources/Raw/wwwroot/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton, projects, package management, and platform prerequisites

- [X] T001 Create solution skeleton: `InkWell.sln` at repo root plus empty `src/` and `tests/` directories per plan.md §Project Structure
- [X] T002 Create the Domain class library `src/InkWell.Domain/InkWell.Domain.csproj` (net10.0, no MAUI/native dependencies) and add it to `InkWell.sln`
- [X] T003 Create the Application class library `src/InkWell.Application/InkWell.Application.csproj` referencing `InkWell.Domain`, and add it to `InkWell.sln`
- [X] T004 Create the Infrastructure class library `src/InkWell.Infrastructure/InkWell.Infrastructure.csproj` referencing `InkWell.Application`, and add it to `InkWell.sln`
- [X] T005 Create the MAUI app `src/InkWell.Maui/InkWell.Maui.csproj` multi-targeting `net10.0-windows10.0.19041.0`, `net10.0-maccatalyst`, `net10.0-ios`, `net10.0-android`, referencing Application + Infrastructure, and add it to `InkWell.sln`
- [X] T006 Create the four xUnit test projects `tests/InkWell.Domain.Tests/`, `tests/InkWell.Application.Tests/`, `tests/InkWell.Infrastructure.Tests/`, `tests/InkWell.Maui.UiTests/` with project references to their targets, and add them to `InkWell.sln`
- [X] T007 Add central package management in `Directory.Packages.props` pinning CommunityToolkit.Mvvm, CommunityToolkit.Maui, `sqlite-net-sqlcipher`, `SQLitePCLRaw.bundle_e_sqlcipher`, Markdig, PdfSharp 6 + MigraDoc, and xUnit — with a comment enforcing the research.md §2 rule that exactly ONE SQLitePCLRaw bundle may ever be referenced (never `sqlite-net-pcl` alongside it)
- [X] T008 [P] Add `Directory.Build.props` and `.editorconfig` at repo root enabling nullable reference types, `TreatWarningsAsErrors`, `GenerateDocumentationFile`, and .NET analyzers for all projects
- [X] T009 [P] Add platform prerequisites: `src/InkWell.Maui/Platforms/iOS/Entitlements.plist` and `src/InkWell.Maui/Platforms/MacCatalyst/Entitlements.plist` with Keychain Sharing (for `SecureStorage`) and `com.apple.security.files.user-selected.read-write` (for export), plus Android storage permission for API < 33 in `src/InkWell.Maui/Platforms/Android/AndroidManifest.xml`
- [X] T010 [P] Scaffold the CodeMirror 6 asset workspace: `src/InkWell.Maui/editor-src/package.json` + bundler config, output wired to `src/InkWell.Maui/Resources/Raw/wwwroot/` (`index.html`, `editor.js`, `live-preview.js`, `styles.css`), with a documented build script
- [X] T011 [P] Add `README.md` at repo root and one per project under `src/` and `tests/` describing layer responsibility and how to build/test (Constitution §VI)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, application ports, the encrypted store, the editor host, and test
infrastructure that every user story depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Risk-retirement spikes (research.md §2, §1)

- [ ] T012 Spike: verify the `bundle_e_sqlcipher` native bundle opens a keyed database on .NET 10 for iOS, Mac Catalyst, Android, and Windows; record the result (and the `SQLite3MC.PCLRaw.bundle` fallback decision) in `specs/001-manuscript-drafting/research.md` §2
- [ ] T013 Spike: verify the `HybridWebView` JS↔C# bridge round-trips a message on WebView2, WKWebView (iOS + Mac Catalyst), and Android WebView; record findings in `specs/001-manuscript-drafting/research.md` §1

### Domain tests (write first — MUST fail before T017–T019, T026–T027)

- [X] T014 [P] Unit tests for title validation and domain result/error types in `tests/InkWell.Domain.Tests/Abstractions/EntityTitleTests.cs` (1–200 chars, trimmed, required — data-model.md validation rules)
- [X] T015 [P] Unit tests for `ProseWordCounter` in `tests/InkWell.Domain.Tests/Services/ProseWordCounterTests.cs` — counts match an independent prose count for headings, emphasis, lists, links, and embedded images; markdown syntax tokens and image markers are never counted (FR-009, SC-005)
- [X] T016 [P] Unit tests for `ChapterOrdering` in `tests/InkWell.Domain.Tests/Services/ChapterOrderingTests.cs` — reorder produces contiguous zero-based indices; removal re-packs indices; unknown/missing ids are rejected (FR-002)

### Domain entities and services

- [X] T017 [P] Create `EntityTitle` value object and `DomainResult`/`DomainError` (`NotFound`, `ValidationError`) in `src/InkWell.Domain/Abstractions/`
- [X] T018 [P] Create `Manuscript` entity in `src/InkWell.Domain/Entities/Manuscript.cs` (Id, Title, CreatedAt, ModifiedAt per data-model.md)
- [X] T019 [P] Create `Chapter` entity in `src/InkWell.Domain/Entities/Chapter.cs` (Id, ManuscriptId, Title, ContentMarkdown, OrderIndex, WordCount, timestamps)
- [X] T020 [P] Create `InlineImage` entity in `src/InkWell.Domain/Entities/InlineImage.cs` (Id, ChapterId, Bytes, MimeType, AltText?, ByteLength, CreatedAt)
- [X] T021 [P] Create `Character` entity in `src/InkWell.Domain/Entities/Character.cs` (Id, ManuscriptId, Name, Notes, timestamps)
- [X] T022 [P] Create `PlotThread` entity in `src/InkWell.Domain/Entities/PlotThread.cs` (Id, ManuscriptId, Title, Notes, timestamps)
- [X] T023 [P] Create `DailyGoal` entity in `src/InkWell.Domain/Entities/DailyGoal.cs` (Id, ManuscriptId, TargetWords, IsActive, timestamps)
- [X] T024 [P] Create `DailyWritingRecord` entity in `src/InkWell.Domain/Entities/DailyWritingRecord.cs` (Id, ManuscriptId, Date, WordsWritten, GoalTarget?, GoalMet)
- [X] T025 [P] Add the Markdig package reference to `src/InkWell.Domain/InkWell.Domain.csproj` with a comment recording that it is pure-managed and introduces no MAUI/native dependency (plan.md §Structure Decision)
- [X] T026 Implement `ProseWordCounter` over the Markdig AST in `src/InkWell.Domain/Services/ProseWordCounter.cs`, counting whitespace-delimited tokens only inside literal prose inline nodes (makes T015 pass)
- [X] T027 Implement `ChapterOrdering` in `src/InkWell.Domain/Services/ChapterOrdering.cs` (makes T016 pass)

### Application ports and DTOs

- [X] T028 [P] Define `IManuscriptRepository` and `IChapterRepository` in `src/InkWell.Application/Abstractions/` per [contracts/manuscript-service.md](./contracts/manuscript-service.md)
- [X] T029 [P] Define `IInlineImageRepository`, `IReferenceRepository`, `IDailyGoalRepository`, and `IWritingHistoryRepository` in `src/InkWell.Application/Abstractions/` per [contracts/reference-service.md](./contracts/reference-service.md) and [contracts/word-count-and-goals.md](./contracts/word-count-and-goals.md)
- [X] T030 [P] Define platform ports `IKeyStore`, `IClock`, `IMarkdownService`, and `IExportService` in `src/InkWell.Application/Abstractions/`
- [X] T031 [P] Define shared DTOs `ManuscriptSummary`, `ManuscriptDetail`, `ChapterContent`, `DailyProgress` (+ `GoalStatus` enum), `DataInventory`, and `ExportResult` in `src/InkWell.Application/Abstractions/Dtos/`

### Test infrastructure

- [X] T032 [P] Create in-memory `FakeKeyStore` and `FixedClock` (advanceable, for day-rollover tests) in `tests/InkWell.Application.Tests/Fakes/`
- [X] T033 [P] Create the keyed-database integration fixture `tests/InkWell.Infrastructure.Tests/Fixtures/KeyedDatabaseFixture.cs` that provisions and deletes a temp SQLCipher DB per test class
- [ ] T034 [P] Create the accessibility test harness (keyboard-only driver + contrast assertion helpers) in `tests/InkWell.Maui.UiTests/Accessibility/AccessibilityHarness.cs`
- [X] T035 [P] Create the privacy test harness asserting zero network egress during an operation in `tests/InkWell.Infrastructure.Tests/Privacy/NoEgressAssert.cs`

### Encrypted store (tests first)

- [X] T036 Integration test for the encrypted connection in `tests/InkWell.Infrastructure.Tests/Persistence/EncryptedConnectionTests.cs` — opens with the correct key, fails with a wrong key, and reports `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`
- [X] T037 Integration test for schema creation in `tests/InkWell.Infrastructure.Tests/Persistence/SchemaMigrationTests.cs` — all seven tables and the data-model.md indexes exist; cascade delete from Manuscript leaves no orphan rows
- [X] T038 Implement `SqlCipherConnectionFactory` in `src/InkWell.Infrastructure/Persistence/SqlCipherConnectionFactory.cs` applying key, WAL, `synchronous=NORMAL`, `busy_timeout`, and `foreign_keys=ON` (makes T036 pass)
- [X] T039 Implement the `SecureStorage`-backed `KeyStore` with first-run 256-bit key generation and explicit "key missing" handling in `src/InkWell.Infrastructure/Security/KeyStore.cs`
- [X] T040 Implement `DatabaseMigrator` with schema v1 (all seven tables + indexes + unique constraints per data-model.md) in `src/InkWell.Infrastructure/Persistence/DatabaseMigrator.cs` (makes T037 pass)
- [X] T041 [P] Implement `MarkdownService` (Markdig) providing HTML/XHTML rendering and AST access in `src/InkWell.Infrastructure/Markdown/MarkdownService.cs`
- [X] T042 [P] Implement `SystemClock` (device local calendar day) in `src/InkWell.Infrastructure/SystemClock.cs`

### MAUI shell and editor host

- [X] T043 Wire the DI composition root in `src/InkWell.Maui/MauiProgram.cs` registering repositories, domain services, clock, key store, markdown/export services, and CommunityToolkit.Maui
- [X] T044 Create the app shell and navigation skeleton in `src/InkWell.Maui/AppShell.xaml` + `.cs` with routes for Library, ManuscriptShell, Editor, Goals, Characters, PlotThreads, and DataControls
- [X] T045 [P] Add accessible theme resources in `src/InkWell.Maui/Resources/Styles/` — WCAG AA contrast color tokens for all themes, visible focus indicators, and text-bearing status styles (never color alone, FR-019)
- [X] T046 [P] Create `BaseViewModel` and the navigation + confirmation-dialog services in `src/InkWell.Maui/ViewModels/BaseViewModel.cs` and `src/InkWell.Maui/Services/` (confirmation service is used by every destructive action, FR-005)
- [X] T047 Implement the `HybridWebView` editor host control and JS↔C# bridge plumbing in `src/InkWell.Maui/Controls/EditorHostView.cs` + `EditorBridge.cs` per [contracts/chapter-editor-bridge.md](./contracts/chapter-editor-bridge.md)
- [X] T048 Build the CodeMirror 6 base bundle (markdown language + bridge handshake, `role="textbox"`, semantic DOM, ARIA live region) in `src/InkWell.Maui/Resources/Raw/wwwroot/index.html` and `editor.js`

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Draft and Organize a Manuscript (Priority: P1) 🎯 MVP

**Goal**: A novelist creates a manuscript, adds chapters, writes markdown prose with inline images in a
live-rendering editor, reorders chapters, and finds everything intact after restart — all offline and
encrypted, with autosave and confirmed deletes.

**Independent Test**: Create a manuscript, add three chapters, write text in each, reorder them, close and
reopen the app, and confirm all content and ordering are preserved.

### Tests for User Story 1 (MANDATORY) ⚠️

> Write these FIRST and confirm they FAIL before implementing T055 onward.

- [X] T049 [P] [US1] Contract tests for manuscript/chapter persistence in `tests/InkWell.Infrastructure.Tests/Persistence/ManuscriptRepositoryTests.cs` — create→list round-trip, rename persists, reorder survives a simulated restart, delete cascades with no orphan rows (contracts/manuscript-service.md)
- [ ] T050 [P] [US1] Unit tests for manuscript/chapter use cases against fakes in `tests/InkWell.Application.Tests/UseCases/ManuscriptUseCasesTests.cs` — title validation, `ModifiedAt` bumps, `ValidationError` when the reorder id set does not match the manuscript's chapters
- [X] T051 [P] [US1] Contract tests for autosave in `tests/InkWell.Infrastructure.Tests/Persistence/AutoSaveTests.cs` — debounced commit lands within the durability window, `flushNow` commits synchronously, and `loadChapter`→edit→`contentChanged`→reopen store yields identical markdown (FR-004, SC-003)
- [X] T052 [P] [US1] Contract tests for inline images in `tests/InkWell.Infrastructure.Tests/Persistence/InlineImageRepositoryTests.cs` — bytes are embedded in the encrypted store, survive deletion of the simulated source file, and cascade-delete with their chapter (FR-003a)
- [X] T053 [P] [US1] Integration test of the US1 journey in `tests/InkWell.Maui.UiTests/UserStory1Tests.cs` — create manuscript, add three chapters, type prose, reorder, restart, verify content and order, delete a chapter only after confirmation
- [ ] T054 [P] [US1] Accessibility tests in `tests/InkWell.Maui.UiTests/Accessibility/UserStory1AccessibilityTests.cs` — complete the journey keyboard-only (including chapter reorder), verify screen-reader announcement of manuscript/chapter structure and AA contrast (FR-019, SC-007)
- [X] T055 [P] [US1] Privacy tests in `tests/InkWell.Infrastructure.Tests/Privacy/DraftingPrivacyTests.cs` — the raw DB file contains no plaintext prose or image bytes, and drafting produces zero network egress (FR-016, FR-017, SC-002)

### Implementation for User Story 1

- [X] T056 [P] [US1] Implement `ManuscriptRepository` in `src/InkWell.Infrastructure/Persistence/ManuscriptRepository.cs` (list newest-modified-first, create, rename, transactional cascade delete, detail load without chapter content)
- [X] T057 [P] [US1] Implement `ChapterRepository` in `src/InkWell.Infrastructure/Persistence/ChapterRepository.cs` (append at max+1, rename, transactional `ReorderChapters` via `ChapterOrdering`, delete with index re-pack, `GetChapterContent`)
- [X] T058 [P] [US1] Implement `InlineImageRepository` in `src/InkWell.Infrastructure/Persistence/InlineImageRepository.cs` — rowid-addressed BLOB table, lazy byte loading, downscale/cap oversized images on insert
- [X] T059 [US1] Implement `ManuscriptUseCases` (List/Create/Rename/Delete/Get) in `src/InkWell.Application/UseCases/ManuscriptUseCases.cs`
- [X] T060 [US1] Implement `ChapterUseCases` (Add/Rename/Reorder/Delete/GetContent) in `src/InkWell.Application/UseCases/ChapterUseCases.cs`
- [X] T061 [US1] Implement `AutoSaveCoordinator` in `src/InkWell.Application/UseCases/AutoSaveCoordinator.cs` — ~0.5–2 s debounce, `FlushNow`, prose `WordCount` recompute, single transaction per commit, all off the UI thread (FR-004, SC-003)
- [X] T062 [US1] Implement the JS→C# bridge handlers `contentChanged`, `flushNow`, `insertImageRequested`, and `imageMissingAltText` in `src/InkWell.Maui/Controls/EditorBridge.cs`
- [X] T063 [US1] Implement the C#→JS calls `loadChapter` and `focusEditor` with one CodeMirror instance per chapter in `src/InkWell.Maui/Controls/EditorHostView.cs` and `src/InkWell.Maui/Resources/Raw/wwwroot/editor.js`
- [X] T064 [US1] Implement the Obsidian-style live-preview decoration layer (hide markdown tokens, render inline formatting and images, keep the cursor line raw, keep underlying text in the accessibility tree) in `src/InkWell.Maui/Resources/Raw/wwwroot/live-preview.js`
- [X] T065 [P] [US1] Implement `LibraryView.xaml` + `LibraryViewModel.cs` in `src/InkWell.Maui/Views/` and `src/InkWell.Maui/ViewModels/` — list, create, rename, confirmed delete, and empty-state guidance
- [X] T066 [P] [US1] Implement `ManuscriptShellView.xaml` + `ManuscriptShellViewModel.cs` — chapter list with add/rename/confirmed delete and keyboard-operable reorder, plus no-chapters empty state
- [X] T067 [US1] Implement `EditorView.xaml` + `EditorViewModel.cs` hosting `EditorHostView`, loading one chapter at a time and showing save status as text (never color alone)
- [X] T068 [US1] Implement inline image insertion UX (pick/paste/drop, alt-text prompt, non-blocking missing-alt-text indicator) in `src/InkWell.Maui/Views/EditorView.xaml.cs` and `live-preview.js`
- [X] T069 [US1] Implement app-lifecycle flush and WAL checkpoint on sleep/close in `src/InkWell.Maui/App.xaml.cs` and `src/InkWell.Infrastructure/Persistence/SqlCipherConnectionFactory.cs`
- [ ] T070 [US1] Implement the native MAUI `Editor` accessibility-mode fallback (plain markdown source, screen-reader native) with a settings toggle in `src/InkWell.Maui/Controls/AccessibleEditorFallbackView.xaml` (research.md §1)
- [ ] T071 [US1] Verify WCAG 2.1 AA compliance for US1 (keyboard-only completion, screen reader, contrast) and fix any gaps found by T054
- [ ] T072 [US1] Verify US1 end-to-end with networking fully disabled and confirm encryption at rest, per quickstart.md §US1 steps 1–6 (FR-006, SC-002)

**Checkpoint**: User Story 1 is fully functional, testable, accessible, and privacy-compliant — this is the MVP

---

## Phase 4: User Story 2 - Distraction-Free Writing (Priority: P2)

**Goal**: The writer toggles a focused mode that hides navigation, panels, and non-essential toolbars,
with editing and autosave behaving identically and cursor/content preserved across the transition.

**Independent Test**: Open a chapter, activate distraction-free mode, confirm non-essential UI is hidden
and the text is still fully editable, then exit and confirm the full interface returns with content intact.

### Tests for User Story 2 (MANDATORY) ⚠️

- [X] T073 [P] [US2] Contract tests for `setDistractionFree` in `tests/InkWell.Maui.UiTests/DistractionFreeBridgeTests.cs` — both the visible control and the keyboard shortcut toggle the mode and preserve cursor/selection and content (contracts/chapter-editor-bridge.md)
- [X] T074 [P] [US2] Integration test of the US2 journey in `tests/InkWell.Maui.UiTests/UserStory2Tests.cs` — enter mode, type and confirm autosave parity, exit, verify cursor and content unchanged
- [X] T075 [P] [US2] Accessibility test in `tests/InkWell.Maui.UiTests/Accessibility/UserStory2AccessibilityTests.cs` — enter and exit keyboard-only, and the mode change is announced to screen readers
- [X] T076 [P] [US2] Performance test in `tests/InkWell.Maui.UiTests/Performance/DistractionFreePerformanceTests.cs` — enter and exit each complete in under 1 second (SC-006)

### Implementation for User Story 2

- [X] T077 [US2] Implement the `setDistractionFree` JS handler and the chrome-hidden layout in `src/InkWell.Maui/Resources/Raw/wwwroot/editor.js` and `styles.css` (editing area fills available space)
- [X] T078 [US2] Add distraction-free state to `src/InkWell.Maui/ViewModels/EditorViewModel.cs` and hide navigation, panels, and non-essential toolbars in `src/InkWell.Maui/Views/EditorView.xaml`, leaving an unobtrusive exit control (FR-007)
- [X] T079 [US2] Bind the enter/exit keyboard shortcut across Windows, Mac Catalyst, iOS, and Android in `src/InkWell.Maui/Views/EditorView.xaml.cs` (FR-008)
- [X] T080 [US2] Send `flushNow` on every distraction-free toggle and verify autosave behaves identically in the mode, in `src/InkWell.Maui/Controls/EditorBridge.cs`
- [X] T081 [US2] Preserve and restore cursor/selection across the transition via `focusEditor` in `src/InkWell.Maui/Resources/Raw/wwwroot/editor.js` and `src/InkWell.Maui/Controls/EditorHostView.cs`
- [ ] T082 [US2] Verify WCAG 2.1 AA compliance and privacy parity for US2 and fix gaps found by T075

**Checkpoint**: User Stories 1 and 2 both work independently

---

## Phase 5: User Story 3 - Track Daily Word-Count Goals (Priority: P2)

**Goal**: The writer sets a daily word-count target and sees live chapter/manuscript counts plus daily
progress, remaining words, and a met/exceeded status that resets at local midnight while retaining history.

**Independent Test**: Set a daily goal of 500 words, write 200 words, confirm progress shows 200/500 (40%),
write 300 more, and confirm the goal is marked met for the day.

### Tests for User Story 3 (MANDATORY) ⚠️

- [X] T083 [P] [US3] Unit tests for `DailyProgressCalculator` in `tests/InkWell.Domain.Tests/Services/DailyProgressCalculatorTests.cs` — 200/500 → InProgress, 40%, remaining 300; +300 → Met; a `FixedClock` advanced past local midnight resets today and preserves the prior record (FR-011, FR-012, SC-005)
- [X] T084 [P] [US3] Unit tests for `GoalEvaluator` in `tests/InkWell.Domain.Tests/Services/GoalEvaluatorTests.cs` — `NoGoal`/`InProgress`/`Met` (==) / `Exceeded` (>) boundary behavior
- [X] T085 [P] [US3] Contract tests for goal and history persistence in `tests/InkWell.Infrastructure.Tests/Persistence/GoalAndHistoryRepositoryTests.cs` — one goal per manuscript, clear retains history, upsert is unique per (ManuscriptId, Date), `GetHistory` returns prior days
- [X] T086 [P] [US3] Integration test of the US3 journey in `tests/InkWell.Maui.UiTests/UserStory3Tests.cs` — set goal, write, verify live counts exclude markdown syntax and image markers, cross a day boundary, verify reset plus retained history
- [X] T087 [P] [US3] Accessibility test in `tests/InkWell.Maui.UiTests/Accessibility/UserStory3AccessibilityTests.cs` — progress and goal status are announced via ARIA live region / semantic properties and conveyed by text label, never color alone (FR-019, SC-007)

### Implementation for User Story 3

- [X] T088 [US3] Implement `DailyProgressCalculator` in `src/InkWell.Domain/Services/DailyProgressCalculator.cs` — attributes words to the local calendar day they were typed (makes T083 pass)
- [X] T089 [US3] Implement `GoalEvaluator` in `src/InkWell.Domain/Services/GoalEvaluator.cs` returning the `GoalStatus` enum (makes T084 pass)
- [X] T090 [P] [US3] Implement `DailyGoalRepository` in `src/InkWell.Infrastructure/Persistence/DailyGoalRepository.cs` (set/change/clear, unique per manuscript)
- [X] T091 [P] [US3] Implement `WritingHistoryRepository` in `src/InkWell.Infrastructure/Persistence/WritingHistoryRepository.cs` (upsert today by local date, range query for history)
- [X] T092 [US3] Implement `GoalUseCases` (`SetDailyGoal`, `ClearDailyGoal`, `GetTodayProgress`, `RecordWordsForToday`, `GetHistory`) in `src/InkWell.Application/UseCases/GoalUseCases.cs` per contracts/word-count-and-goals.md
- [X] T093 [US3] Wire `RecordWordsForToday` into the autosave commit so chapter prose and the day's `DailyWritingRecord` upsert land in one transaction, in `src/InkWell.Application/UseCases/AutoSaveCoordinator.cs` (counts never diverge from content)
- [X] T094 [US3] Implement `GetManuscriptWordCount` (sum of chapter counts) in `src/InkWell.Application/UseCases/ChapterUseCases.cs` and surface live chapter + manuscript counts in `src/InkWell.Maui/ViewModels/EditorViewModel.cs` with an ARIA live region announcement
- [X] T095 [P] [US3] Implement `GoalsView.xaml` + `GoalsViewModel.cs` in `src/InkWell.Maui/Views/` and `src/InkWell.Maui/ViewModels/` — set/change/clear the target, show progress, percentage, remaining words, text status, and prior-day history
- [ ] T096 [US3] Verify WCAG 2.1 AA compliance for US3 (no status by color alone) and manually confirm day rollover per quickstart.md §US3 step 4

**Checkpoint**: User Stories 1, 2, and 3 all work independently

---

## Phase 6: User Story 4 - Track Characters and Plot Threads (Priority: P3)

**Goal**: The writer keeps character profiles and plot threads scoped to a manuscript, viewable while
drafting without losing their place in the editor.

**Independent Test**: Create a character with notes and a plot thread with notes, associate them with a
manuscript, close and reopen the app, and confirm both are retained and viewable alongside the manuscript.

### Tests for User Story 4 (MANDATORY) ⚠️

- [X] T097 [P] [US4] Contract tests in `tests/InkWell.Infrastructure.Tests/Persistence/ReferenceRepositoryTests.cs` — create→list→reopen-store round-trip for characters and plot threads, edits persist, delete removes only the target row and leaves the manuscript intact (contracts/reference-service.md)
- [ ] T098 [P] [US4] Unit tests for reference use cases in `tests/InkWell.Application.Tests/UseCases/ReferenceUseCasesTests.cs` — name/title validation (1–200, trimmed), freeform notes, `NotFound` handling
- [X] T099 [P] [US4] Integration test of the US4 journey in `tests/InkWell.Maui.UiTests/UserStory4Tests.cs` — create both, open the reference while drafting and return to the exact caret, edit one, confirm-delete another, restart and verify retention
- [X] T100 [P] [US4] Accessibility test in `tests/InkWell.Maui.UiTests/Accessibility/UserStory4AccessibilityTests.cs` — keyboard-only CRUD and screen-reader list semantics for both reference types

### Implementation for User Story 4

- [X] T101 [US4] Implement `ReferenceRepository` (characters and plot threads) in `src/InkWell.Infrastructure/Persistence/ReferenceRepository.cs` with name-sorted listing
- [X] T102 [US4] Implement `ReferenceUseCases` (create/list/update/delete for both types) in `src/InkWell.Application/UseCases/ReferenceUseCases.cs`
- [X] T103 [P] [US4] Implement `CharactersView.xaml` + `CharactersViewModel.cs` in `src/InkWell.Maui/Views/` and `src/InkWell.Maui/ViewModels/` with confirmed delete (FR-005)
- [X] T104 [P] [US4] Implement `PlotThreadsView.xaml` + `PlotThreadsViewModel.cs` with confirmed delete
- [X] T105 [US4] Implement the reference panel presentation that opens alongside/over the editor and restores the exact caret on close, in `src/InkWell.Maui/Views/EditorView.xaml.cs` and `src/InkWell.Maui/Controls/EditorHostView.cs` (FR-015)
- [ ] T106 [US4] Verify WCAG 2.1 AA compliance for US4 and fix gaps found by T100

**Checkpoint**: All four user stories are independently functional

---

## Phase 7: Cross-Cutting - Export, Data Controls & Privacy

**Purpose**: The only outbound path (user-initiated EPUB/PDF export) plus view/export/delete data controls
required by FR-016, FR-017, FR-018, SC-008, and SC-009. Applies across all stories.

- [ ] T107 Spike: verify PdfSharp 6 renders text with a custom `IFontResolver` and bundled TTFs on a real iOS and Android device; record the outcome in `specs/001-manuscript-drafting/research.md` §3 (highest export risk)
- [ ] T108 [P] Contract tests for EPUB export in `tests/InkWell.Infrastructure.Tests/Export/EpubExporterTests.cs` — output validates with EPUBCheck, `mimetype` is the first stored (uncompressed) entry, every source image is embedded under `images/`, at both whole-manuscript and single-chapter granularity (SC-009)
- [ ] T109 [P] Contract tests for PDF export in `tests/InkWell.Infrastructure.Tests/Export/PdfExporterTests.cs` — the file opens, every source image is embedded, at both granularities
- [ ] T110 [P] Tests for data controls in `tests/InkWell.Infrastructure.Tests/Persistence/DataControlsTests.cs` — `GetAllStoredData` inventories every entity type, `DeleteManuscriptData` cascades, `DeleteAllData` leaves no recoverable content and removes the key (SC-008)
- [ ] T111 [P] Privacy test in `tests/InkWell.Infrastructure.Tests/Privacy/ExportPrivacyTests.cs` — export produces zero network egress and writes only to the caller-supplied destination (FR-017)
- [ ] T112 Add XHTML post-processing (self-close void elements, add the root namespace) to `src/InkWell.Infrastructure/Markdown/MarkdownService.cs` so Markdig output is well-formed for EPUB
- [ ] T113 Implement `EpubExporter` in `src/InkWell.Infrastructure/Export/EpubExporter.cs` — `ZipArchive` with stored-first `mimetype`, `META-INF/container.xml`, `.opf` manifest, EPUB3 `nav.xhtml` + `toc.ncx`, one XHTML per chapter, image bytes extracted to `images/` with `<img src>` rewritten to relative paths
- [ ] T114 Implement `PdfExporter` in `src/InkWell.Infrastructure/Export/PdfExporter.cs` — walk the Markdig AST into MigraDoc elements (headings to styled paragraphs, image nodes via `AddImage` from embedded bytes)
- [ ] T115 Implement the bundled-TTF `IFontResolver` in `src/InkWell.Infrastructure/Export/BundledFontResolver.cs` and add the serif body + heading fonts to `src/InkWell.Maui/Resources/Fonts/`
- [ ] T116 Implement `ExportService` (`ExportManuscript`, `ExportChapter`, `ExportManuscriptAllChapters`) in `src/InkWell.Infrastructure/Export/ExportService.cs` per contracts/export-service.md
- [ ] T117 Wire `CommunityToolkit.Maui` `FileSaver`/`FolderPicker` destination selection and off-UI-thread export with progress and error handling in `src/InkWell.Maui/ViewModels/ExportViewModel.cs` and the editor/library export entry points
- [ ] T118 Implement `DataControlsView.xaml` + `DataControlsViewModel.cs` in `src/InkWell.Maui/Views/` and `src/InkWell.Maui/ViewModels/` — view all stored data, delete a manuscript, delete all app data, each behind a confirmation (FR-005, FR-018)
- [ ] T119 Implement `DeleteAllData` (drop every table and remove the encryption key from `SecureStorage`) in `src/InkWell.Infrastructure/Persistence/DataControlsRepository.cs` (SC-008)
- [ ] T120 Add EPUBCheck validation to the test pipeline in `tests/InkWell.Infrastructure.Tests/Export/` so exported EPUBs are validated on every run

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Performance validation, documentation, and final compliance sweeps across all stories

- [ ] T121 [P] Implement the large-manuscript seed generator (150,000+ words across 50+ chapters) in `tests/InkWell.Maui.UiTests/Performance/LargeManuscriptSeeder.cs`
- [ ] T122 [P] Performance test in `tests/InkWell.Maui.UiTests/Performance/LargeManuscriptPerformanceTests.cs` — opening a chapter and typing stay responsive with keystroke feedback within the 16 ms target against the seeded manuscript (SC-004)
- [ ] T123 [P] Audit and eliminate blocking I/O on the UI thread across repositories, autosave, and export in `src/InkWell.Infrastructure/` and `src/InkWell.Maui/ViewModels/`
- [ ] T124 [P] Implement empty-state guidance for no manuscripts, no chapters, and an empty chapter in `src/InkWell.Maui/Views/LibraryView.xaml`, `ManuscriptShellView.xaml`, and `EditorView.xaml`
- [ ] T125 [P] Implement user-facing error handling and messaging for missing encryption key, unreadable database, and export failure in `src/InkWell.Maui/Services/ErrorPresenter.cs`
- [ ] T126 [P] Add XML doc comments to all public APIs across `src/` and enable auto-generated API documentation output (Constitution §VI)
- [ ] T127 [P] Write user-facing help documentation for the editor, goals, and export in `docs/help/` and update every project `README.md`
- [ ] T128 [P] Add explanatory code comments to the live-preview decoration layer (`wwwroot/live-preview.js`), the key/crypto bootstrap (`Security/KeyStore.cs`), and the autosave durability logic (`AutoSaveCoordinator.cs`)
- [ ] T129 Run the cross-platform WebView QA matrix (contenteditable, IME, paste) on WebView2, WKWebView (iOS + Mac Catalyst), and Android WebView; record results in `specs/001-manuscript-drafting/research.md` §1
- [ ] T130 Run a full accessibility sweep across every view (contrast, screen reader, keyboard-only) including the native-`Editor` fallback, and fix all findings (SC-007)
- [ ] T131 Execute every scenario in `specs/001-manuscript-drafting/quickstart.md` and check off its Definition of Done
- [ ] T132 Final code cleanup and refactoring pass across `src/` with all tests green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational — no dependency on other stories
- **User Story 2 (Phase 4)**: Depends on Foundational — independently testable; shares the editor host built in Phase 2
- **User Story 3 (Phase 5)**: Depends on Foundational — T093 extends the autosave coordinator created in US1 (T061); if US3 is built before US1, implement T061 first
- **User Story 4 (Phase 6)**: Depends on Foundational — fully independent of US1–US3 except the reference-panel caret restore (T105), which needs the editor host from Phase 2
- **Cross-Cutting Export (Phase 7)**: Depends on Foundational; exercises whatever stories are complete (needs US1 content to export)
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### Within Each User Story

- Tests are written first and MUST fail before the matching implementation task
- Domain services before repositories; repositories before use cases; use cases before ViewModels/Views
- Accessibility and privacy verification close out every story

### Parallel Opportunities

- Setup: T008–T011 run in parallel after the projects exist
- Foundational: T014–T016 (tests) in parallel; then T017–T025 (entities/abstractions) in parallel; T032–T035 (test harnesses) in parallel; T041–T042 and T045–T046 in parallel
- US1: T049–T055 (all tests) in parallel; then T056–T058 (repositories) in parallel; T065–T066 (views) in parallel
- US2: T073–T076 in parallel
- US3: T083–T087 in parallel; then T090–T091 in parallel
- US4: T097–T100 in parallel; then T103–T104 in parallel
- Export: T108–T111 in parallel
- Polish: T121–T128 in parallel
- With a team, US1–US4 can be developed concurrently once Phase 2 completes

---

## Parallel Example: User Story 1

```bash
# Launch all User Story 1 tests together (they must fail first):
Task: "Contract tests for manuscript/chapter persistence in tests/InkWell.Infrastructure.Tests/Persistence/ManuscriptRepositoryTests.cs"
Task: "Unit tests for manuscript/chapter use cases in tests/InkWell.Application.Tests/UseCases/ManuscriptUseCasesTests.cs"
Task: "Contract tests for autosave in tests/InkWell.Infrastructure.Tests/Persistence/AutoSaveTests.cs"
Task: "Contract tests for inline images in tests/InkWell.Infrastructure.Tests/Persistence/InlineImageRepositoryTests.cs"
Task: "Integration test of the US1 journey in tests/InkWell.Maui.UiTests/UserStory1Tests.cs"
Task: "Accessibility tests in tests/InkWell.Maui.UiTests/Accessibility/UserStory1AccessibilityTests.cs"
Task: "Privacy tests in tests/InkWell.Infrastructure.Tests/Privacy/DraftingPrivacyTests.cs"

# Then launch the three repositories together:
Task: "Implement ManuscriptRepository in src/InkWell.Infrastructure/Persistence/ManuscriptRepository.cs"
Task: "Implement ChapterRepository in src/InkWell.Infrastructure/Persistence/ChapterRepository.cs"
Task: "Implement InlineImageRepository in src/InkWell.Infrastructure/Persistence/InlineImageRepository.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational — retire the SQLCipher and HybridWebView spikes (T012, T013) before building on them
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run the US1 quickstart scenario offline, keyboard-only, and confirm restart persistence
5. Ship/demo — a chapter-organized drafting tool is already useful on its own

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Add User Story 1 → validate independently → MVP
3. Add User Story 2 (distraction-free) → validate independently
4. Add User Story 3 (goals) → validate independently
5. Add User Story 4 (characters/plot threads) → validate independently
6. Add Phase 7 (export + data controls) → satisfies FR-018/SC-008/SC-009
7. Phase 8 polish → performance, docs, full a11y sweep

### Parallel Team Strategy

With multiple developers, once Phase 2 completes:

- Developer A: User Story 1 (P1, MVP — the critical path)
- Developer B: User Story 3 (goals) then User Story 2 (distraction-free)
- Developer C: User Story 4 (references) then Phase 7 (export + data controls)

---

## Notes

- [P] tasks touch different files and have no dependency on incomplete work
- Every destructive action (manuscript, chapter, character, plot thread, delete-all) goes through the shared
  confirmation service from T046 (FR-005)
- Never reference two SQLitePCLRaw bundles — enforced in `Directory.Packages.props` (T007); violating this
  crashes iOS at runtime (research.md §2)
- Word counts are always recomputed by `ProseWordCounter` on save and never trusted from the editor
  (data-model.md)
- Verify tests fail before implementing; commit after each task or logical group
- Stop at any checkpoint to validate a story independently
