---

description: "Task list for Chapter Reading Time Estimates"
---

# Tasks: Chapter Reading Time Estimates

**Input**: Design documents from `/specs/002-chapter-reading-time/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/reading-time-and-settings.md](./contracts/reading-time-and-settings.md), [quickstart.md](./quickstart.md)

**Tests**: REQUIRED. Constitution §II mandates TDD for every feature and the spec's Testing Requirements list unit, integration, accessibility, privacy, and performance tests. In every phase, write the test tasks first and confirm they **fail** before starting the implementation tasks that follow them.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 = per-chapter estimate (P1), US2 = Settings + reading speed (P2), US3 = manuscript total (P3)

## Conventions every task must follow

- Match the surrounding code: file-scoped namespaces, XML doc comments on every public member, `sealed` classes, `ConfigureAwait(false)` in Domain/Application/Infrastructure, `ConfigureAwait(true)` in view models, CommunityToolkit.Mvvm `[ObservableProperty] public partial` properties and `[RelayCommand]` methods.
- Failures are returned as `DomainResult` / `DomainResult<T>` (`src/InkWell.Domain/Abstractions/DomainResult.cs`), using `DomainResult<T>.Validation(message)` for invalid input.
- Repositories acquire connections with `using SqliteConnectionLease lease = await _factory.AcquireConnectionAsync(ct)`; timestamps are stored with `RowConversions.ToTicks` (see `src/InkWell.Infrastructure/Persistence/DailyGoalRepository.cs` as the model).
- Test names use the existing `Sentence_case_with_underscores` style with xUnit `[Fact]` / `[Theory]`.
- Constants: default 238 wpm; range 100–600 inclusive; setting key `reading.wordsPerMinute`; validation message exactly `Enter a whole number from 100 to 600.`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a green baseline before changing anything.

- [ ] T001 Confirm the working branch is `002-chapter-reading-time` and run `dotnet test InkWell.slnx` from the repo root; record the passing test count in the PR description draft as the baseline (no file changes; if anything fails, stop and report it before continuing)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain calculation, the encrypted settings store, and the in-memory reading-speed owner. Every user story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundation (write first, must fail)

- [ ] T002 [P] Write `ReadingSpeedTests` in `tests/InkWell.Domain.Tests/Services/ReadingSpeedTests.cs`: `Default.WordsPerMinute == 238` and `IsDefault`; `TryCreate` succeeds for 100, 238, 600 and fails with a Validation error for 99, 601, 0, -5; `TryParse` succeeds for `"250"` and `" 250 "`, and fails with message `Enter a whole number from 100 to 600.` for `null`, `""`, `"abc"`, `"250.5"`, `"+250"`, `"-250"`, `"2380"`, `"99"`; parsing is invariant-culture (run one case under `de-DE` with `CultureInfo.CurrentCulture` swapped and restored); two speeds with the same value are equal
- [ ] T003 [P] Write `ReadingTimeEstimatorTests` in `tests/InkWell.Domain.Tests/Services/ReadingTimeEstimatorTests.cs` as a `[Theory]` over the quickstart boundary table: (0,238)→None; (-10,238)→None; (100,238)→UnderOneMinute; (118,238)→UnderOneMinute; (119,238)→Minutes 1; (2380,238)→10; (2000,238)→8; (2000,200)→10; (11900,238)→50; (17850,238)→75 with Hours 1, RemainderMinutes 15; (71400,238)→300 with Hours 5, RemainderMinutes 0; plus `int.MaxValue` words at 100 wpm does not throw or overflow
- [ ] T004 [P] Write `ReadingTimeFormatterTests` in `tests/InkWell.Domain.Tests/Services/ReadingTimeFormatterTests.cs` covering every row of the formatter table in `specs/002-chapter-reading-time/data-model.md`: `Format` → `—`, `< 1 min`, `1 min`, `10 min`, `1 hr`, `1 hr 15 min`, `5 hr`; `Describe` → `no estimated reading time`, `estimated reading time less than 1 minute`, `estimated reading time 1 minute`, `estimated reading time 10 minutes`, `estimated reading time 1 hour`, `estimated reading time 1 hour 15 minutes`, `estimated reading time 5 hours`; also `2 hr 1 min` / `estimated reading time 2 hours 1 minute` for the singular-minute-with-hours case
- [ ] T005 [P] Extend `tests/InkWell.Infrastructure.Tests/Persistence/SchemaMigrationTests.cs`: update `Records_the_schema_version` to expect `2`; add `Creates_every_table` coverage of `AppSetting` via `DatabaseMigrator.TableNames`; add `Upgrades_a_version_1_database_in_place`, which creates a database, runs only the v1 statements and sets `PRAGMA user_version=1` with a manuscript row inserted, then calls `DatabaseMigrator.MigrateAsync` and asserts `user_version = 2`, the `AppSetting` table exists, and the manuscript row survives
- [ ] T006 [P] Write `AppSettingsRepositoryTests` in `tests/InkWell.Infrastructure.Tests/Persistence/AppSettingsRepositoryTests.cs` using the existing `StoreFixture`: `GetAsync` of an unknown key returns null; `SetAsync` then `GetAsync` round-trips; a second `SetAsync` on the same key overwrites the value and `ModifiedAt` (one row only); `RemoveAsync` returns true then false; `GetAllAsync` returns all rows ordered by `Key` with their `ModifiedAt`
- [ ] T007 [P] Create `FakeAppSettingsRepository` in `tests/InkWell.Application.Tests/Fakes/FakeAppSettingsRepository.cs`: an in-memory `IAppSettingsRepository` backed by a `Dictionary<string, StoredSetting>`, with a `ThrowOnWrite` flag that makes `SetAsync`/`RemoveAsync` throw `IOException`, plus `SetCallCount` and `RemoveCallCount` counters
- [ ] T008 Write `ReadingSpeedSettingsTests` in `tests/InkWell.Application.Tests/UseCases/ReadingSpeedSettingsTests.cs` (uses T007 and `FixedClock`), with one test per row of the Behaviour table in `specs/002-chapter-reading-time/contracts/reading-time-and-settings.md`: load with no row, load `"200"` (raises `Changed`), load `"abc"` and `"2380"` (Default, row untouched, no `Changed`); `TrySetAsync("250")` upserts and raises; repeat `"250"` performs no write and raises nothing; `"99"`/`"abc"`/`""` fail with the range message and change nothing; `"238"` when custom removes the row and raises; `ResetAsync` when custom removes and raises, and when already default raises nothing; with `ThrowOnWrite`, `TrySetAsync("250")` throws, `Current` is unchanged and `Changed` is not raised; `LoadAsync` called twice reads the repository once

### Implementation for Foundation

- [ ] T009 [P] Implement `ReadingSpeed` in `src/InkWell.Domain/Abstractions/ReadingSpeed.cs` per the contract: `readonly record struct` with `MinimumWordsPerMinute = 100`, `MaximumWordsPerMinute = 600`, `Default` (238), `WordsPerMinute`, `IsDefault`, `TryCreate(int)`, `TryParse(string?)`. Parse with `int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)` so signs, decimals and group separators are rejected (makes T002 pass)
- [ ] T010 [P] Implement `ReadingTimeKind`, `ReadingTimeEstimate` and `ReadingTimeEstimator` in `src/InkWell.Domain/Services/ReadingTimeEstimator.cs`. Clamp negative words to 0; compute `roundedMinutes = (2L * words + wpm) / (2L * wpm)`; map to None / UnderOneMinute / Minutes. Add a remarks comment explaining that this identity is round-half-up and that `roundedMinutes == 0` is exactly "under 30 seconds" (research.md §2) (makes T003 pass)
- [ ] T011 [P] Implement `ReadingTimeFormatter.Format` and `Describe` in `src/InkWell.Domain/Services/ReadingTimeFormatter.cs` using the exact strings from data-model.md, with singular/plural handling for hours and minutes; use `—` (U+2014) for None (makes T004 pass)
- [ ] T012 Add schema v2 to `src/InkWell.Infrastructure/Persistence/DatabaseMigrator.cs`: set `CurrentVersion = 2`; add `private static readonly string[] SchemaV2` with `CREATE TABLE IF NOT EXISTS AppSetting (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL, ModifiedAt INTEGER NOT NULL)`; run it in a new `if (version < 2)` block after the v1 block; append `"AppSetting"` to `TableNames`; update the class remarks to mention the settings table (makes T005 pass)
- [ ] T013 [P] Add `StoredSetting(string Key, string Value, DateTimeOffset ModifiedAt)` record with XML docs in `src/InkWell.Application/Abstractions/Dtos/SettingsDtos.cs`
- [ ] T014 [P] Add `IAppSettingsRepository` (GetAsync, SetAsync, RemoveAsync, GetAllAsync per the contract) and `public static class AppSettingKeys { public const string ReadingWordsPerMinute = "reading.wordsPerMinute"; }` in `src/InkWell.Application/Abstractions/IAppSettingsRepository.cs`
- [ ] T015 Add `internal sealed class AppSettingRow` (Key, Value, ModifiedAt as `long`, plus `ToStoredSetting()` using `RowConversions`) to `src/InkWell.Infrastructure/Persistence/Rows.cs`, following the existing `DailyGoalRow` pattern
- [ ] T016 Implement `AppSettingsRepository : IAppSettingsRepository` in `src/InkWell.Infrastructure/Persistence/AppSettingsRepository.cs`, modelled on `DailyGoalRepository`: `SetAsync` uses `INSERT INTO AppSetting (Key, Value, ModifiedAt) VALUES (?, ?, ?) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, ModifiedAt = excluded.ModifiedAt`; `RemoveAsync` returns `affected > 0`; `GetAllAsync` uses `ORDER BY Key` (makes T006 pass)
- [ ] T017 Implement `ReadingSpeedSettings` in `src/InkWell.Application/UseCases/ReadingSpeedSettings.cs` exactly per the contract's Behaviour table. Constructor takes `IAppSettingsRepository` and `IClock`. `LoadAsync` is idempotent (cache the load `Task` behind a lock so concurrent callers share one read). A value equal to the default is stored by **removing** the row. `Changed` is raised only after persistence succeeds and only when the value actually changes. Include a remarks comment pointing to research.md §3 on which settings belong here versus `IEditorPreferences` (makes T008 pass)
- [ ] T018 Register `IAppSettingsRepository → AppSettingsRepository` and `ReadingSpeedSettings` as singletons in `src/InkWell.Maui/MauiProgram.cs` (in the Infrastructure and Application use-case blocks respectively)
- [ ] T019 Wire the harness in `tests/InkWell.Maui.UiTests/Harness/AppHarness.cs`: inside `Wire()`, construct `AppSettingsRepository` over the harness connection factory and a `ReadingSpeedSettings` over it and `Clock`; expose them as `public IAppSettingsRepository AppSettings { get; private set; }` and `public ReadingSpeedSettings ReadingSpeed { get; private set; }`. Re-create them wherever `Wire()` is re-run (for example after delete-all) so the harness mirrors an app restart

**Checkpoint**: `dotnet test tests/InkWell.Domain.Tests tests/InkWell.Application.Tests tests/InkWell.Infrastructure.Tests` is green, including the 001 suite.

---

## Phase 3: User Story 1 - See a reading-time estimate for each chapter (Priority: P1) 🎯 MVP

**Goal**: Every chapter shows its estimated reading time in the chapter list, and the open chapter shows it in the editor status bar, updating as the writer types. Uses the current reading speed (the default until US2 lands).

**Independent Test**: Create chapters of 0, 100, 2,380 and 11,900 words. The list shows `—`, `< 1 min`, `10 min`, `50 min`. Open the 2,380-word chapter: the editor shows `10 min`. Type enough to change it and the estimate updates after autosave, with no explicit save.

### Tests for User Story 1 (write first, must fail)

- [ ] T020 [P] [US1] Write `ReadingTimeUserStory1Tests` in `tests/InkWell.Maui.UiTests/ReadingTimeUserStory1Tests.cs` using `AppHarness`: (a) seed a manuscript with chapters of 0/100/2,380/11,900 prose words through `ChapterUseCases`, load `harness.Manuscript`, and assert each `Chapters[i].ReadingTimeText` and `ReadingTimeDescription`; (b) a chapter containing markdown emphasis and an inline image has the same estimate as its plain-prose word count (FR-002); (c) open the 2,380-word chapter in `harness.Editor`, assert `ChapterReadingTimeText == "10 min"`, simulate typing via `FakeEditorHost` to add 300 words, flush autosave, and assert `11 min`; (d) with a custom speed of 200 already stored through `harness.ReadingSpeed.TrySetAsync("200")` before load, the list and editor show `12 min` for 2,380 words (estimates use the loaded speed, not always the default)
- [ ] T021 [P] [US1] Update existing tests for the `ChapterListItem` element type in `tests/InkWell.Maui.UiTests/UserStory1Tests.cs`, `tests/InkWell.Maui.UiTests/Accessibility/UserStory1AccessibilityTests.cs`, `tests/InkWell.Maui.UiTests/Performance/LargeManuscriptPerformanceTests.cs` and `tests/InkWell.Maui.UiTests/ExportAndDataControlsTests.cs`: wherever a test reads `ManuscriptViewModel.Chapters` as `ChapterSummary` or passes a `ChapterSummary` to a chapter command, use the `ChapterListItem` (or `.Summary`). Do not change what the tests assert

### Implementation for User Story 1

- [ ] T022 [US1] Create `ChapterListItem` in `src/InkWell.Presentation/ViewModels/ChapterListItem.cs`: `sealed partial class : ObservableObject`, constructor `(ChapterSummary summary, ReadingSpeed speed)`, pass-through `Summary`, `Id`, `Title`, `OrderIndex`, `WordCount`, observable `ReadingTimeText` and `ReadingTimeDescription`, and `ApplySpeed(ReadingSpeed)` that recomputes both via `ReadingTimeEstimator` and `ReadingTimeFormatter`
- [ ] T023 [US1] Update `src/InkWell.Presentation/ViewModels/ManuscriptViewModel.cs`: inject `ReadingSpeedSettings`; change `Chapters` to `ObservableCollection<ChapterListItem>`; in `LoadAsync`, `await _readingSpeed.LoadAsync()` before building rows and construct each row with `_readingSpeed.Current`; change `OpenChapterAsync`, `MoveChapterUpAsync`, `MoveChapterDownAsync`, `DeleteChapterAsync` and `MoveAsync` to take `ChapterListItem?` and use `.Summary`/`.Id` internally (their generated command names stay the same)
- [ ] T024 [US1] Update `src/InkWell.Presentation/ViewModels/EditorViewModel.cs`: inject `ReadingSpeedSettings`; add observable `ChapterReadingTimeText` and `ChapterReadingTimeDescription`; await `LoadAsync` on the settings before the first estimate in the existing load path; recompute both in a `partial void OnChapterWordCountChanged(int value)` hook so the existing assignments at load and after autosave (`result.ChapterWordCount`) drive it. Do **not** call any screen-reader announce for these
- [ ] T025 [US1] Update the `ManuscriptViewModel` and `EditorViewModel` construction in `tests/InkWell.Maui.UiTests/Harness/AppHarness.cs` (the `Manuscript` and `Editor` properties) to pass `ReadingSpeed`, and the DI-resolved construction still compiles in `src/InkWell.Maui/MauiProgram.cs` (no registration change beyond T018)
- [ ] T026 [US1] Update the chapter row template in `src/InkWell.Maui/Views/ManuscriptPage.xaml`: change the template's `x:DataType` to `vm:ChapterListItem`; add a `Label` with `Style="{StaticResource StatusLabel}"` bound to `ReadingTimeText` beside the existing word-count label, with `SemanticProperties.Description="{Binding ReadingTimeDescription}"`; keep `LineBreakMode` wrapping so it isn't cut off at 200% text size; update any `RelativeSource` command bindings whose parameter type changed
- [ ] T027 [US1] Add the editor estimate in `src/InkWell.Maui/Views/EditorPage.xaml`: a new `Label` with `Style="{StaticResource StatusLabel}"` directly below the `CountsSummary` label in the status bar, `Text="{Binding ChapterReadingTimeText, StringFormat='Reading time: {0}'}"` and `SemanticProperties.Description="{Binding ChapterReadingTimeDescription}"`

**Checkpoint**: T020 and the updated 001 suite pass. US1 is shippable at the default speed.

---

## Phase 4: User Story 2 - Adjust the reading speed in Settings (Priority: P2)

**Goal**: A new Settings page, reachable from the Library, Manuscript and Editor pages, where the writer sets the reading speed (applied on Enter or leaving the field, no Save button) or restores the default. Open screens recalculate immediately, the speed survives restart, and it's listed on Data Controls.

**Independent Test**: With a 2,000-word chapter, open Settings, enter `200`, press Enter: the estimate goes from `8 min` to `10 min` without reopening the manuscript. `99` and `abc` show the range error and change nothing. Restart (re-wire the harness) and `200` is still in effect. Restore default returns it to 238.

### Tests for User Story 2 (write first, must fail)

- [ ] T028 [P] [US2] Write `ReadingTimeUserStory2Tests` in `tests/InkWell.Maui.UiTests/ReadingTimeUserStory2Tests.cs`: (a) `harness.Library.OpenSettingsCommand`, the Manuscript VM's `OpenSettingsCommand` and the Editor VM's `OpenSettingsCommand` each navigate to `Routes.Settings` (assert via `FakeNavigationService`); (b) `SettingsViewModel.LoadAsync` shows `ReadingSpeedText == "238"` and `IsDefault`; (c) with a loaded `ManuscriptViewModel` showing a 2,000-word chapter at `8 min`, set `ReadingSpeedText = "200"` and execute `ApplyReadingSpeedCommand`: the **same, still-loaded** Manuscript and Editor VM instances now show `10 min` (FR-012, with no reload); (d) `"99"` and `"abc"` set `HasError`, `ErrorMessage == "Enter a whole number from 100 to 600."`, revert `ReadingSpeedText` to the current value, and leave estimates unchanged; (e) after applying `200`, dispose the harness's VMs, re-run `Wire()` against the same database, and a fresh `SettingsViewModel` shows `200`; (f) `RestoreDefaultCommand` returns to `238`, estimates return to `8 min`, and `harness.AppSettings.GetAsync("reading.wordsPerMinute")` is null; (g) a disposed `EditorViewModel` no longer receives `Changed` (subscribe-count or no-exception check after dispose)
- [ ] T029 [P] [US2] Extend `tests/InkWell.Infrastructure.Tests/Persistence/DataControlsTests.cs`: with `reading.wordsPerMinute = "200"` stored, `GetInventoryAsync().Settings` contains exactly that `StoredSetting`; with nothing stored, `Settings` is empty; after `DeleteAllDataAsync` and re-opening the store, `Settings` is empty
- [ ] T030 [P] [US2] Extend `tests/InkWell.Maui.UiTests/ExportAndDataControlsTests.cs`: after setting the speed to 300, `harness.DataControls` (after `LoadAsync`) exposes a stored-settings line `Reading speed: 300 words per minute`; after restoring the default the line is gone; after delete-all and re-wire, a fresh `SettingsViewModel` shows `238`

### Implementation for User Story 2

- [ ] T031 [US2] Add `public const string Settings = "settings";` with an XML doc comment ("Application settings, such as reading speed") to `src/InkWell.Presentation/Routes.cs`
- [ ] T032 [US2] Create `SettingsViewModel` in `src/InkWell.Presentation/ViewModels/SettingsViewModel.cs` (`sealed partial class : BaseViewModel`, constructor `(ReadingSpeedSettings readingSpeed, IErrorPresenter errors)`): observable `ReadingSpeedText`, `ErrorMessage`, `HasError` and `IsDefault`; `[RelayCommand] LoadAsync` awaits settings load and fills the text; `[RelayCommand] ApplyReadingSpeedAsync` calls `TrySetAsync(ReadingSpeedText)` and, on failure, sets the error, reverts the text to `Current` and announces the message once via an injected or static announcer abstraction that the tests can observe (reuse the existing announce pattern in `ManuscriptViewModel.MoveAsync` / `StatusMessage`); on success it clears the error; `[RelayCommand] RestoreDefaultAsync` calls `ResetAsync`; store exceptions go to `IErrorPresenter` and leave the text at `Current`
- [ ] T033 [US2] Subscribe to `ReadingSpeedSettings.Changed` in `src/InkWell.Presentation/ViewModels/ManuscriptViewModel.cs` (call `ApplySpeed` on every `ChapterListItem`) and in `src/InkWell.Presentation/ViewModels/EditorViewModel.cs` (recompute the chapter estimate). Marshal to the UI thread with the existing `UiThread` helper (`src/InkWell.Presentation/Services/UiThread.cs`). Unsubscribe in `EditorViewModel.DisposeAsync`; for `ManuscriptViewModel`, implement `IDisposable` and unsubscribe there, and call it from `ManuscriptPage.OnDisappearing`/handler-changing in `src/InkWell.Maui/Views/ManuscriptPage.xaml.cs` so a transient page can't be kept alive by the singleton
- [ ] T034 [US2] Add `[RelayCommand] OpenSettingsAsync() => _navigation.GoToAsync(Routes.Settings)` to `src/InkWell.Presentation/ViewModels/LibraryViewModel.cs`, `src/InkWell.Presentation/ViewModels/ManuscriptViewModel.cs` and `src/InkWell.Presentation/ViewModels/EditorViewModel.cs`, following the existing `OpenDataControlsAsync` style
- [ ] T035 [US2] Create `src/InkWell.Maui/Views/SettingsPage.xaml` and `SettingsPage.xaml.cs` (constructor-injected `SettingsViewModel`, `OnAppearing` calls `LoadAsync`, like `GoalsPage`). Layout: `ScrollView` → `VerticalStackLayout`; Level1 heading "Settings"; a "Reading" section with a Level2 heading; a `Label` "Reading speed (words per minute)"; an `Entry` (`Keyboard="Numeric"`, `Text="{Binding ReadingSpeedText}"`, `ReturnCommand="{Binding ApplyReadingSpeedCommand}"`, `SemanticProperties.Description="Reading speed in words per minute"`, `SemanticProperties.Hint="100 to 600. Applied when you press Enter or leave the field."`); an `Unfocused` handler in code-behind that executes `ApplyReadingSpeedCommand`; an error `Label` bound to `ErrorMessage`, visible on `HasError`, using an error colour that meets 4.5:1 contrast in `Colors.xaml` light and dark and also prefixed with the text "Error:" so it doesn't rely on colour alone; a `SecondaryButton` "Restore default (238)" bound to `RestoreDefaultCommand` and disabled when `IsDefault`; a short explanation `Label`: "Used to estimate how long each chapter takes to read." Leave a comment noting that new settings are added as new sections (FR-006b)
- [ ] T036 [US2] Register the route and screens: `Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage))` in `src/InkWell.Maui/AppShell.xaml.cs`; `AddTransient<SettingsViewModel>()` and `AddTransient<SettingsPage>()` in `src/InkWell.Maui/MauiProgram.cs`; add a `SettingsViewModel Settings => new(ReadingSpeed, Errors)` property to `tests/InkWell.Maui.UiTests/Harness/AppHarness.cs`
- [ ] T037 [US2] Add the entry points: in `src/InkWell.Maui/Views/LibraryPage.xaml`, wrap the Row 0 heading in a `Grid ColumnDefinitions="*,Auto"` with a `SecondaryButton` "Settings" bound to `OpenSettingsCommand` (`SemanticProperties.Hint="Opens application settings"`); in `src/InkWell.Maui/Views/ManuscriptPage.xaml`, add a "Settings" `SecondaryButton` to the header grid beside "Daily goal" (add a column); in `src/InkWell.Maui/Views/EditorPage.xaml`, add a "Settings" `SecondaryButton` to the header grid beside "Plot threads" (that header is already hidden in focus mode). Check that the tab order still reaches each button by keyboard
- [ ] T038 [US2] Add `IReadOnlyList<StoredSetting> Settings` (with a `<param>` doc) as the last parameter of `DataInventory` in `src/InkWell.Application/Abstractions/Dtos/ExportDtos.cs`, update the `IDataControlsRepository.GetInventoryAsync` doc in `src/InkWell.Application/Abstractions/IExportService.cs`, and populate it in `src/InkWell.Infrastructure/Persistence/DataControlsRepository.cs` with `SELECT * FROM AppSetting ORDER BY Key` on the same lease (makes T029 pass; fix any other `new DataInventory(...)` call sites the compiler reports)
- [ ] T039 [US2] Show stored settings on Data Controls: add `ObservableCollection<string> StoredSettings` and `bool HasStoredSettings` to `src/InkWell.Presentation/ViewModels/DataControlsViewModel.cs`, mapping key `reading.wordsPerMinute` to `Reading speed: {value} words per minute` and unknown keys to `{key}: {value}`; add a "Settings stored" section to `src/InkWell.Maui/Views/DataControlsPage.xaml`, visible only when `HasStoredSettings`, above the "delete everything" area (makes T030 pass)

**Checkpoint**: US1 and US2 tests pass. Changing the speed in Settings updates already-open screens, and it persists.

---

## Phase 5: User Story 3 - See the whole manuscript's reading time (Priority: P3)

**Goal**: The manuscript page header shows the total reading time, computed from the total word count (not the sum of rounded chapter estimates), and it updates on add, edit, reorder, delete and speed change.

**Independent Test**: Chapters totalling 71,400 words show `5 hr`. Deleting a 2,380-word chapter shows `4 hr 50 min`.

### Tests for User Story 3 (write first, must fail)

- [ ] T040 [P] [US3] Write `ReadingTimeUserStory3Tests` in `tests/InkWell.Maui.UiTests/ReadingTimeUserStory3Tests.cs`: (a) chapters totalling 71,400 words give `ReadingTimeText == "5 hr"` and `ReadingTimeDescription == "estimated reading time 5 hours"`; (b) after `DeleteChapterCommand` on a 2,380-word chapter (confirmation faked to yes) and reload, `4 hr 50 min`; (c) three chapters of 119 words each show `1 min` each, and the total for 357 words is `2 min`, which proves it is computed from total words, not by adding rounded chapter values; (d) after applying speed 300 via `harness.ReadingSpeed.TrySetAsync("300")`, the loaded VM's total updates without reload; (e) an empty manuscript shows `—`

### Implementation for User Story 3

- [ ] T041 [US3] Add observable `ReadingTimeText` and `ReadingTimeDescription` to `src/InkWell.Presentation/ViewModels/ManuscriptViewModel.cs`, recomputed in `partial void OnWordCountChanged(int value)` and in the `Changed` handler from T033, using `ReadingTimeEstimator.Estimate(WordCount, _readingSpeed.Current)`; append the total to the existing status sentence in `LoadAsync` (for example `"3 chapters, 71,400 words, about 5 hr to read."`) so screen readers hear it with the summary
- [ ] T042 [US3] Show the total in the header of `src/InkWell.Maui/Views/ManuscriptPage.xaml`: a `StatusLabel` under the Level1 heading with `Text="{Binding ReadingTimeText, StringFormat='Total reading time: {0}'}"` and `SemanticProperties.Description="{Binding ReadingTimeDescription}"`

**Checkpoint**: All three stories work and test independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Accessibility, performance, privacy, documentation, and the PR.

- [ ] T043 [P] Write `ReadingTimeAccessibilityTests` in `tests/InkWell.Maui.UiTests/Accessibility/ReadingTimeAccessibilityTests.cs` following `AccessibilityHarness` patterns: every `ChapterListItem.ReadingTimeDescription`, the manuscript `ReadingTimeDescription` and the editor `ChapterReadingTimeDescription` contain no abbreviations (`min`, `hr`) and start with `estimated reading time` or equal `no estimated reading time`; typing in the editor through `FakeEditorHost` across an estimate change records **zero** reading-time announcements; an invalid speed in `SettingsViewModel` produces exactly one announcement; the XAML for `SettingsPage`, `ManuscriptPage` and `EditorPage` gives every new interactive control a `SemanticProperties.Description` or `Hint` (use the same XAML-inspection approach as the existing accessibility tests)
- [ ] T044 [P] Extend `tests/InkWell.Maui.UiTests/Performance/LargeManuscriptPerformanceTests.cs` (using `LargeManuscriptSeeder`, 150,000 words / 50+ chapters): the existing open and typing budgets still pass with estimates enabled; applying a new speed through `ReadingSpeedSettings.TrySetAsync` updates every `ChapterListItem` in a loaded `ManuscriptViewModel` within 1 second (SC-005)
- [ ] T045 [P] Extend `tests/InkWell.Infrastructure.Tests/Privacy/DraftingPrivacyTests.cs`: after storing `reading.wordsPerMinute = "317"`, the raw database file bytes don't contain the ASCII strings `reading.wordsPerMinute` or `317` next to it, and opening it without the key fails (follow the existing encrypted-at-rest assertions in that file)
- [ ] T046 [P] Update `src/InkWell.Domain/README.md` (ReadingSpeed, estimator, formatter, rounding rule), `src/InkWell.Application/README.md` (`ReadingSpeedSettings`, `IAppSettingsRepository`, and the rule for choosing between `AppSetting` and `IEditorPreferences` from research.md §3), `src/InkWell.Infrastructure/README.md` (schema v2, `AppSetting` table, migration), and `src/InkWell.Maui/README.md` (Settings page and entry points)
- [ ] T047 [P] Add user-facing help for reading time and Settings wherever the 001 user help lives (search the repo for the existing help on "daily goal" and "export", and follow that location and format): what the estimate means, the 238 wpm default, how to change it in Settings, the 100–600 range, and that the setting is stored encrypted on the device and appears on the Data Controls page
- [ ] T048 Run every scenario in `specs/002-chapter-reading-time/quickstart.md` sections 1–6, and record the final automated test count against the T001 baseline in the PR description
- [ ] T049 Open a PR from `002-chapter-reading-time` to `main` per Constitution §VII with: a summary, links to spec/plan/tasks, test results (before/after counts), accessibility and privacy notes, the documentation updates, and a note that schema version moves 1 → 2

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none
- **Foundational (Phase 2)**: after Setup; **blocks all user stories**
- **US1 (Phase 3)**: after Foundational
- **US2 (Phase 4)**: after Foundational. Its live-update tests (T028 c, T033) exercise `ChapterListItem` and the editor estimate from US1, so in practice it runs **after US1**
- **US3 (Phase 5)**: after US1 (reuses `ManuscriptViewModel`'s settings injection from T023). T041's live recalculation hooks into the `Changed` handler from T033, so if US3 is built before US2, add the subscription in T041 instead
- **Polish (Phase 6)**: after all desired stories

### Within-phase ordering

- Foundation: T002–T007 in parallel → T008 (needs T007) → T009–T011 in parallel and T013–T014 in parallel → T012 → T015 → T016 → T017 → T018, T019
- US1: T020, T021 in parallel → T022 → T023, T024 (different files, both need T022/T017) → T025 → T026, T027
- US2: T028–T030 in parallel → T031 → T032 → T033, T034 → T035 → T036 → T037; T038 → T039 can run alongside T032–T037
- US3: T040 → T041 → T042
- Polish: T043–T047 in parallel → T048 → T049

### Story dependency graph

```text
Setup ─▶ Foundational ─▶ US1 (P1, MVP) ─┬─▶ US2 (P2) ─┐
                                        └─▶ US3 (P3) ─┴─▶ Polish
```

## Parallel Examples

### Foundational

```text
T002 ReadingSpeedTests        T003 ReadingTimeEstimatorTests   T004 ReadingTimeFormatterTests
T005 SchemaMigrationTests     T006 AppSettingsRepositoryTests  T007 FakeAppSettingsRepository
— then —
T009 ReadingSpeed             T010 ReadingTimeEstimator        T011 ReadingTimeFormatter
T013 StoredSetting DTO        T014 IAppSettingsRepository
```

### User Story 1

```text
T020 ReadingTimeUserStory1Tests   T021 update existing tests for ChapterListItem
— then, after T022 —
T023 ManuscriptViewModel          T024 EditorViewModel
— then —
T026 ManuscriptPage.xaml          T027 EditorPage.xaml
```

### User Story 2

```text
T028 ReadingTimeUserStory2Tests   T029 DataControlsTests   T030 ExportAndDataControlsTests
— alongside the Settings page work (T031–T037) —
T038 DataInventory.Settings ─▶ T039 Data Controls display
```

### Polish

```text
T043 accessibility   T044 performance   T045 privacy   T046 READMEs   T047 user help
```

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 → Phase 2 (foundation, including the encrypted settings store, so US1 already reads a stored speed if one exists)
2. Phase 3 (US1)
3. **Stop and validate**: run T020 plus the full 001 suite; walk through quickstart §3 step 1
4. Shippable: every chapter shows a reading time at 238 wpm

### Incremental delivery

1. US1 → validate → demo (per-chapter estimates)
2. US2 → validate → demo (Settings page, adjustable speed, Data Controls listing)
3. US3 → validate → demo (manuscript total)
4. Polish → PR

## Notes

- [P] = different files and no dependency on an incomplete task.
- Write each phase's tests first and watch them fail (Constitution §II).
- Commit after each task or logical group; never commit directly to `main` (Constitution §VII).
- Known pre-existing gap, **out of scope here**: `LibraryViewModel.OpenDataControlsAsync` exists but no Library page control calls it. The Data Controls listing added in T039 is only reachable once that entry point exists, so raise it as a separate issue rather than fixing it silently in this feature.
