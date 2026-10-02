# Implementation Plan: Chapter Reading Time Estimates

**Branch**: `002-chapter-reading-time` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-chapter-reading-time/spec.md`

## Summary

Show an estimated reading time for every chapter (chapter list and editor), plus the whole
manuscript, calculated from the existing prose word count and a reading speed the writer can change
in a new application Settings page (default 238 wpm, allowed 100–600, applied automatically on
Enter/leaving the field).

**Technical approach**: Reading time is a pure domain calculation (`ReadingTimeEstimator` +
`ReadingTimeFormatter`) over the word counts InkWell already maintains (`ChapterSummary.WordCount`,
`AutoSaveResult`), so it adds no work on the keystroke path and can never disagree with the word count.
The reading speed is stored in a new key–value `AppSetting` table in the existing SQLCipher database
(schema v2), owned in memory by an application-layer `ReadingSpeedSettings` singleton that raises
`Changed` so open screens recalculate immediately. A new `SettingsPage` (route `settings`) is reachable
from the Library, Manuscript, and Editor pages, and the setting appears in the Data Controls inventory.
See [research.md](./research.md).

## Technical Context

**Language/Version**: C# 13 on .NET 10 (LTS), unchanged from feature 001

**Primary Dependencies**: .NET MAUI; CommunityToolkit.Mvvm; `sqlite-net-sqlcipher`. **No new packages.**

**Storage**: Existing SQLCipher database; new `AppSetting(Key, Value, ModifiedAt)` table via schema
migration v1 → v2 (`PRAGMA user_version`)

**Testing**: xUnit across the four existing test projects: Domain (estimator, formatter,
`ReadingSpeed`), Application (`ReadingSpeedSettings` with a fake repository), Infrastructure (migration,
repository round-trip, inventory, encryption at rest), Maui.UiTests (per-story flows through
`AppHarness`, accessibility, large-manuscript performance)

**Target Platform**: Windows, macOS (Mac Catalyst), iOS, Android, via MAUI

**Project Type**: Cross-platform desktop + mobile app (MAUI) over clean-architecture libraries

**Performance Goals**: No added cost per keystroke (estimates reuse autosave's returned counts);
estimate visible within 1 s of a typing pause (SC-003); speed change reflected on 50+ chapter rows
within 1 s (SC-005); no perceptible change to the existing 150k-word open/typing budgets (SC-004)

**Constraints**: Fully offline; setting encrypted at rest (clarification Q3); no Save button
(clarification Q2); Settings holds reading speed only and existing preferences stay put (clarification
Q1); WCAG 2.1 AA, estimates described in full words, and no per-keystroke announcements

**Scale/Scope**: Single local user; up to 150k+ words / 50+ chapters; three user stories (P1
per-chapter estimate, P2 Settings + reading speed, P3 manuscript total); ~1 new page, 1 new table

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature MUST comply with the InkWell Constitution v1.1.0. Mandatory gates:

- [x] **Clean Architecture**: Calculation and formatting are pure Domain services; `ReadingSpeedSettings`
  and `IAppSettingsRepository` live in Application; `AppSettingsRepository` and the migration live in
  Infrastructure; `ChapterListItem`/`SettingsViewModel` in Presentation; pages in Maui. Dependencies
  still point inward, and the `ChapterSummary` DTO is not made aware of a user preference (research §6).
- [x] **Test-Driven**: Tests first for every component: estimator boundaries, formatter strings,
  `ReadingSpeed` parsing, `ReadingSpeedSettings` state table (contract), v2 migration from a v1 file,
  repository upsert/remove, inventory, one UI-harness test per user story, accessibility, performance,
  privacy.
- [x] **WCAG 2.1 AA**: Full-word `SemanticProperties.Description` on every estimate; no announcement on
  typing; labelled numeric entry with an announced error; keyboard path to Settings from every main
  screen; 200% text-size check (research §5, §7).
- [x] **.NET Stack**: .NET 10 + MAUI only; no native or JS changes. The editor WebView is untouched;
  estimates render in the native status bar.
- [x] **Performance**: Estimates are O(1) integer math over cached counts; settings I/O runs off the UI
  thread through existing connection leases; validated by the existing large-manuscript performance
  tests plus a 50-row speed-change test.
- [x] **Documentation**: XML docs on all new public types; comments on the rounding identity and the
  two-settings-stores rule; `InkWell.Domain`, `InkWell.Application`, `InkWell.Infrastructure`, and
  `InkWell.Maui` READMEs updated; user help for reading time and Settings.
- [x] **PR Workflow**: Work on `002-chapter-reading-time`; merged to `main` by PR with test results and
  docs, the same as #1.
- [x] **Data Privacy**: Only new stored datum is one integer preference; no consent needed (nothing
  leaves the device); listed on Data Controls; removed by delete-all (spec Data Privacy section).
- [x] **Storage**: Local-first, encrypted SQLCipher table; no cloud; works fully offline.

**Gate result (pre-research)**: PASS, no violations.

**Gate result (post-design re-check)**: PASS. The design adds no project, package, or pattern beyond
what 001 established. One noted consequence (not a violation): settings now have two homes, platform
preferences for the pre-unlock editor toggle and the encrypted `AppSetting` table for everything else.
The rule for choosing between them is recorded in research §3.

## Project Structure

### Documentation (this feature)

```text
specs/002-chapter-reading-time/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions
├── data-model.md        # Phase 1: entities, table, lifecycle
├── quickstart.md        # Phase 1: validation guide
├── contracts/
│   └── reading-time-and-settings.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks; not created here)
```

### Source Code (repository root)

New files marked `+`, modified files marked `~`.

```text
src/
├── InkWell.Domain/
│   ├── Abstractions/
│   │   └── + ReadingSpeed.cs
│   └── Services/
│       ├── + ReadingTimeEstimator.cs        # ReadingTimeKind, ReadingTimeEstimate, Estimate()
│       └── + ReadingTimeFormatter.cs        # Format(), Describe()
├── InkWell.Application/
│   ├── Abstractions/
│   │   ├── + IAppSettingsRepository.cs      # + AppSettingKeys
│   │   ├── ~ IExportService.cs              # IDataControlsRepository doc: inventory includes settings
│   │   └── Dtos/
│   │       ├── + SettingsDtos.cs            # StoredSetting
│   │       └── ~ ExportDtos.cs              # DataInventory.Settings
│   └── UseCases/
│       └── + ReadingSpeedSettings.cs
├── InkWell.Infrastructure/
│   └── Persistence/
│       ├── ~ DatabaseMigrator.cs            # CurrentVersion 2, SchemaV2, TableNames
│       ├── + AppSettingsRepository.cs
│       ├── ~ DataControlsRepository.cs      # populate Settings
│       └── ~ Rows.cs                        # AppSettingRow
├── InkWell.Presentation/
│   ├── ~ Routes.cs                          # Settings
│   └── ViewModels/
│       ├── + ChapterListItem.cs
│       ├── + SettingsViewModel.cs
│       ├── ~ ManuscriptViewModel.cs         # ChapterListItem rows, manuscript total, Changed subscription, OpenSettings
│       ├── ~ EditorViewModel.cs             # chapter estimate, Changed subscription, OpenSettings
│       ├── ~ LibraryViewModel.cs            # OpenSettings
│       └── ~ DataControlsViewModel.cs       # stored-settings lines
└── InkWell.Maui/
    ├── ~ AppShell.xaml.cs                   # register settings route
    ├── ~ MauiProgram.cs                     # DI registrations
    └── Views/
        ├── + SettingsPage.xaml(.cs)
        ├── ~ LibraryPage.xaml               # Settings button
        ├── ~ ManuscriptPage.xaml            # row estimate, header total, Settings header button
        ├── ~ EditorPage.xaml(.cs)           # status-bar estimate, Settings header button (header already hidden in focus mode)
        └── ~ DataControlsPage.xaml          # "Settings stored" section

tests/
├── InkWell.Domain.Tests/Services/
│   ├── + ReadingSpeedTests.cs
│   ├── + ReadingTimeEstimatorTests.cs
│   └── + ReadingTimeFormatterTests.cs
├── InkWell.Application.Tests/
│   ├── Fakes/+ FakeAppSettingsRepository.cs
│   └── UseCases/+ ReadingSpeedSettingsTests.cs
├── InkWell.Infrastructure.Tests/
│   ├── Persistence/+ AppSettingsRepositoryTests.cs
│   ├── Persistence/~ SchemaMigrationTests.cs     # v2, v1→v2 upgrade
│   ├── Persistence/~ DataControlsTests.cs        # settings in inventory; gone after delete-all
│   └── Privacy/~ DraftingPrivacyTests.cs         # AppSetting unreadable without key
└── InkWell.Maui.UiTests/
    ├── ~ Harness/AppHarness.cs                   # wire ReadingSpeedSettings + SettingsViewModel
    ├── + ReadingTimeUserStory1Tests.cs
    ├── + ReadingTimeUserStory2Tests.cs
    ├── + ReadingTimeUserStory3Tests.cs
    ├── Accessibility/+ ReadingTimeAccessibilityTests.cs
    ├── Performance/~ LargeManuscriptPerformanceTests.cs  # 50-row speed change ≤ 1 s
    └── ~ UserStory1Tests.cs, Accessibility/UserStory1AccessibilityTests.cs  # ChapterListItem
```

**Structure Decision**: Keep the existing four-layer layout from feature 001 (`src/InkWell.*` plus a
matching `tests/InkWell.*.Tests` per layer). No new projects. The only cross-cutting ripple is
`ManuscriptViewModel.Chapters` changing element type to `ChapterListItem`. Because the wrapper mirrors
`ChapterSummary`'s property names, existing XAML bindings and most test code compile unchanged
(research §6).

## Implementation order (for /speckit-tasks)

1. **Foundation**: `ReadingSpeed`, estimator, formatter (Domain, tests first); schema v2 +
   `AppSettingsRepository` (Infrastructure, tests first); `ReadingSpeedSettings` (Application).
2. **US1 (P1)**: `ChapterListItem`, chapter-list estimate, editor status-bar estimate; UI-harness test.
   Shippable alone at the default speed.
3. **US2 (P2)**: `SettingsPage`/`SettingsViewModel`, route + entry points, `Changed` subscriptions, Data
   Controls listing; UI-harness test.
4. **US3 (P3)**: manuscript total in the header; UI-harness test.
5. **Polish**: accessibility suite, performance test, privacy test, READMEs and user help.

## Complexity Tracking

No constitution violations; nothing to justify.
