# Research: Chapter Reading Time Estimates

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-01

The spec left no `[NEEDS CLARIFICATION]` markers; the three clarifications (Settings scope,
auto-apply, encrypted storage) are recorded in the spec. This document records the design decisions
needed to turn those into code that fits the existing InkWell architecture.

---

## 1. Where the reading-time calculation lives

**Decision**: A pure domain service, `ReadingTimeEstimator`, in `InkWell.Domain/Services`, next to
`ProseWordCounter`. It takes a prose word count and a `ReadingSpeed` and returns a
`ReadingTimeEstimate` value. Formatting to display text and to full-word accessible text is a
second pure function, `ReadingTimeFormatter`, in the same folder.

**Rationale**: The estimate depends only on two integers. Keeping it in Domain makes every rounding
and formatting rule unit-testable with no database, no UI, and no clock, the same way word counting
and goal evaluation already are (Constitution §I, §II). Taking a *word count*, not markdown, means
reading time can't disagree with the word count the writer sees (FR-002): both come from
`ProseWordCounter`, and reading time never re-counts.

**Alternatives considered**:
- *Compute in the view models*: rejected; it would duplicate rounding rules across the chapter list,
  the editor, and the manuscript total.
- *Store estimates in the database*: rejected; they are derived values that change whenever the speed
  changes, so storing them would add a write path and a staleness risk for no gain.

## 2. Rounding and the "< 1 min" rule, in integer arithmetic

**Decision**: `roundedMinutes = (2 × words + wpm) / (2 × wpm)` using integer division. Then:
`words == 0` → *None* ("—"); `words > 0 && roundedMinutes == 0` → *UnderOneMinute* ("< 1 min");
otherwise *Minutes(roundedMinutes)*, shown as "N min" below 60, and "H hr" or "H hr M min" from 60.

**Rationale**: `(2w + s) / 2s` is `round-half-up(w / s)` exactly, with no floating point, so
there are no boundary errors at the half-minute points the spec's edge cases name. `roundedMinutes == 0`
is exactly "under 30 seconds" (`2w < s`), so the "< 1 min" rule (FR-006) falls out of the same
expression instead of needing a separate seconds calculation. Inputs are bounded (words ≤ int range,
wpm ≤ 600), so `2w + s` can't overflow for any realistic manuscript; the estimator still widens to
`long` defensively.

**Alternatives considered**: `Math.Round(double, MidpointRounding.AwayFromZero)`: correct for these
values, but floating-point representation makes boundary tests harder to reason about.

## 3. How the reading speed is stored (clarification Q3: encrypted database)

**Decision**: Add a key–value `AppSetting` table to the SQLCipher database in schema **v2**
(`Key TEXT PRIMARY KEY, Value TEXT NOT NULL, ModifiedAt INTEGER NOT NULL`). The reading speed is stored
under the key `reading.wordsPerMinute` as an invariant-culture integer string. `DatabaseMigrator`
gains a `version < 2` step; `CurrentVersion` becomes 2.

**Rationale**:
- The spec requires the encrypted store (clarification Q3), listing on Data Controls, and removal by
  "delete all data". The encrypted DB already gives all three: "delete all data" deletes the database
  file and its key, so the setting goes with it and the default applies on next launch (edge case
  "Missing or unreadable reading speed").
- A key–value table, not a `ReadingSpeed` column, is what FR-006b ("laid out so more settings can be
  added later") needs on the storage side: a future setting is a new key, not a migration.
- `user_version` already versions the schema, so this is the first numbered step after v1, as the
  migrator's own remarks anticipate.

**Alternatives considered**:
- *MAUI `Preferences`* (where `IEditorPreferences` lives): rejected by clarification Q3.
- *A typed single-row `Settings` table*: rejected; each new setting would need a schema migration.

**Consequence, noted for future settings**: InkWell now has two places settings can live:
`IEditorPreferences` (plain platform preferences, readable before the database is unlocked) and
`AppSetting` (encrypted, inventoried, deleted with the data). Rule of thumb for future settings:
anything that must be known *before* the database opens (e.g., which editor surface to construct)
stays in platform preferences; everything else goes in `AppSetting`. The accessible-editor toggle is
deliberately **not** moved (spec FR-006b).

## 4. Keeping estimates current when the speed changes (FR-012)

**Decision**: An application-layer singleton, `ReadingSpeedSettings`, owns the current `ReadingSpeed`
in memory. It loads once from `IAppSettingsRepository` (falling back to the default on missing,
unparseable, or out-of-range values), and raises a `Changed` event after a successful set or reset.
`ManuscriptViewModel` and `EditorViewModel` read `Current` when they load and subscribe to `Changed`
so visible estimates recalculate without reloading the page or querying the database.

**Rationale**: `ManuscriptPage` already reloads on `OnAppearing`, but the editor doesn't, and FR-012
says "immediately" with no reliance on navigation order. One in-memory owner also keeps the
estimate calculation off the database on the keystroke path: the editor recomputes reading time from
the `ChapterWordCount`/`ManuscriptWordCount` that autosave already returns (`AutoSaveResult`), with no
extra query (SC-004).

View models unsubscribe on dispose/disappear to avoid leaking transient pages through a singleton's
event; the editor already implements `IAsyncDisposable`.

**Alternatives considered**: *`WeakReferenceMessenger` from CommunityToolkit.Mvvm*: workable, but the
codebase has no messenger usage today; a typed event on one service is simpler and easier to fake in
tests.

## 5. Settings UI: entry point, input control, auto-apply (clarifications Q1, Q2)

**Decision**:
- New `SettingsPage` + `SettingsViewModel`, route `settings`, registered in `AppShell.xaml.cs` like
  the other pushed pages.
- **Entry points**: a "Settings" button in the Library page header (beside the "new manuscript" controls)
  and a "Settings" button in the header bar of the Manuscript and Editor pages (the same pattern as "Daily goal", "Characters", and "Plot threads"). The editor header is already hidden in distraction-free mode,
  so no extra work is needed there. That makes Settings reachable from every main screen
  (US2 scenario 1) without enabling the Shell flyout.
- **Input**: a numeric `Entry` (keyboard `Numeric`) labelled "Reading speed (words per minute)" with
  hint text "100 to 600". Applied on `Completed` (Enter) and `Unfocused` (leaving the field), per Q2.
  An adjacent "Restore default (238)" button (FR-011). No Save button (FR-008a).
- **Validation**: invalid input shows an inline error label ("Enter a whole number from 100 to 600.")
  announced via `SemanticScreenReader.Announce`, the field reverts to the current speed, and nothing
  is persisted (FR-009).
- **Layout**: the page body is a `VerticalStackLayout` of grouped sections; "Reading" is the first
  section. A future setting is a new section, not a restructure (FR-006b).

**Rationale**: An `Entry` is the most accessible control for an exact number from 100 to 600. A
`Slider` is hard to set precisely by keyboard and screen reader, and a `Stepper` would take hundreds
of presses to cross the range. Applying on Enter/blur, not on every keystroke, avoids persisting
"2" on the way to "250".

**Alternatives considered**: Shell flyout with a Settings item: a bigger navigation change than the
feature needs, and it would change every existing page's chrome.

## 6. Showing estimates in the chapter list without breaking the DTO boundary

**Decision**: Introduce a presentation-layer `ChapterListItem` (an `ObservableObject`) that wraps a
`ChapterSummary` and exposes `Id`, `Title`, `OrderIndex`, `WordCount` (pass-through) plus
`ReadingTimeText` and `ReadingTimeDescription`. `ManuscriptViewModel.Chapters` becomes
`ObservableCollection<ChapterListItem>`; chapter commands take `ChapterListItem`. When the speed
changes, the view model calls `item.ApplySpeed(speed)` on each row.

**Rationale**: `ChapterSummary` is an application DTO that comes straight from the repository and
shouldn't know about a user preference. A wrapper keeps the DTO untouched (so all
repository/export tests stay green) while letting rows update in place without rebuilding the list.
Mirroring `ChapterSummary`'s property names keeps the existing XAML bindings and most
`vm.Chapters[i].Title` style test code unchanged.

**Alternatives considered**: *An `IMultiValueConverter` binding `WordCount` + the view model's speed*:
it works, but puts the formatting in the MAUI project where the device-free UI tests can't reach it.

## 7. Accessibility of estimates (FR-016)

**Decision**: Every displayed estimate has a paired accessible string from
`ReadingTimeFormatter.Describe` ("estimated reading time 1 hour 15 minutes", "estimated reading time
less than 1 minute", "no estimated reading time"), bound to `SemanticProperties.Description`. The
editor's estimate is a separate label from the word counts, and is **not** passed to
`SemanticScreenReader.Announce` on change, so typing never triggers announcements; it is read when
focused. Singular and plural forms are handled ("1 minute", "2 minutes", "1 hour").

**Rationale**: Meets spec Accessibility bullets 2–3 and SC-006, and matches the existing pattern where
`CountsSummary` is exposed as a description rather than announced.

## 8. Data Controls inventory (FR-010, spec Data Controls)

**Decision**: `DataInventory` gains `IReadOnlyList<StoredSetting> Settings`, populated by
`DataControlsRepository.GetInventoryAsync` from the `AppSetting` table. `DataControlsViewModel`
maps known keys to labels ("Reading speed: 200 words per minute"). Only settings the writer has
actually changed appear, so a writer who never changed it sees nothing, which is accurate: nothing is
stored. "Delete all data" needs no change; it already removes the database.

**Rationale**: Keeps "view all my data" truthful as soon as the setting is stored, and generic for
future keys.
