# Data Model: Chapter Reading Time Estimates

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md) | **Date**: 2026-10-01

This feature adds one stored record type (`AppSetting`), two domain value types (`ReadingSpeed`,
`ReadingTimeEstimate`), and one presentation model (`ChapterListItem`). Existing entities
(`Chapter`, `Manuscript`) are read but not changed.

---

## Domain (pure, no storage)

### ReadingSpeed *(value object, `InkWell.Domain/Abstractions`)*

| Field | Type | Rules |
|-------|------|-------|
| `WordsPerMinute` | `int` | 100 ≤ value ≤ 600 (FR-008) |

| Member | Purpose |
|--------|---------|
| `Default` | 238 wpm (FR-007) |
| `MinimumWordsPerMinute` / `MaximumWordsPerMinute` | 100 / 600 |
| `TryCreate(int)` → `DomainResult<ReadingSpeed>` | Fails with a range error outside 100–600 |
| `TryParse(string?)` → `DomainResult<ReadingSpeed>` | Trims; accepts invariant-culture whole numbers only; rejects empty, decimals, signs, non-digits (FR-009) |
| `IsDefault` | `WordsPerMinute == 238` |

Equality is by value, so "set to 238" equals the default.

### ReadingTimeEstimate *(value, `InkWell.Domain/Services`)*

| Field | Type | Meaning |
|-------|------|---------|
| `Kind` | `ReadingTimeKind` | `None` (0 words) · `UnderOneMinute` (> 0 words, rounds to 0 min) · `Minutes` |
| `TotalMinutes` | `int` | Rounded half-up minutes; 0 unless `Kind == Minutes` |
| `Hours` / `RemainderMinutes` | `int` | `TotalMinutes / 60`, `TotalMinutes % 60` (derived) |

Produced by `ReadingTimeEstimator.Estimate(int wordCount, ReadingSpeed speed)`. Negative word counts are
treated as 0. Rounding rule: [research.md §2](./research.md#2-rounding-and-the--1-min-rule-in-integer-arithmetic).

### ReadingTimeFormatter *(pure functions, `InkWell.Domain/Services`)*

| Estimate | `Format` (display) | `Describe` (screen reader) |
|----------|--------------------|-----------------------------|
| None | `—` | `no estimated reading time` |
| UnderOneMinute | `< 1 min` | `estimated reading time less than 1 minute` |
| 1 min | `1 min` | `estimated reading time 1 minute` |
| 10 min | `10 min` | `estimated reading time 10 minutes` |
| 60 min | `1 hr` | `estimated reading time 1 hour` |
| 75 min | `1 hr 15 min` | `estimated reading time 1 hour 15 minutes` |
| 300 min | `5 hr` | `estimated reading time 5 hours` |

## Stored (encrypted SQLCipher database, schema v2)

### AppSetting *(new table)*

| Column | Type | Constraints |
|--------|------|-------------|
| `Key` | `TEXT` | `NOT NULL PRIMARY KEY` |
| `Value` | `TEXT` | `NOT NULL`; invariant-culture string |
| `ModifiedAt` | `INTEGER` | `NOT NULL`; UTC ticks (same convention as other tables via `RowConversions.ToTicks`) |

**Known keys**

| Key | Value format | Default when absent/invalid |
|-----|--------------|-----------------------------|
| `reading.wordsPerMinute` | whole number `100`–`600` | `238` |

**Lifecycle**
- *Absent* → no row: the default applies (first launch, or after "delete all data").
- *Set* → upsert (`INSERT … ON CONFLICT(Key) DO UPDATE`) when a valid speed is applied (FR-008a).
- *Restore default* → the row is **deleted**, not set to 238, so the inventory shows only
  settings that differ from the defaults and "nothing stored" stays true for a writer who never
  changed it.
- *Unreadable* → a row whose value fails `ReadingSpeed.TryParse` is ignored on load (default applies)
  and is overwritten on the next valid set. It is not auto-deleted on read, so a read never writes.
- *Delete all data* → removed with the database file and key (no new code path).

Not owned by any manuscript: there is no `ManuscriptId` and no cascade. The setting is global
(spec Assumptions: one global reading speed).

**Migration**: `DatabaseMigrator.CurrentVersion` 1 → 2; step `version < 2` runs
`CREATE TABLE IF NOT EXISTS AppSetting (...)`. `TableNames` gains `"AppSetting"`. A v1 database
upgrades in place with no data change; a fresh database runs v1 then v2.

## Application DTOs

| Type | Change |
|------|--------|
| `StoredSetting(string Key, string Value, DateTimeOffset ModifiedAt)` | **New**. One `AppSetting` row, for the inventory |
| `DataInventory` | **Adds** `IReadOnlyList<StoredSetting> Settings` (FR-010 "listed in the Data Controls inventory") |
| `ChapterSummary`, `ManuscriptDetail`, `AutoSaveResult` | Unchanged; already carry the word counts the estimates need |

## Presentation

### ChapterListItem *(new, `InkWell.Presentation/ViewModels`)*

| Member | Source |
|--------|--------|
| `Summary` | the wrapped `ChapterSummary` |
| `Id`, `Title`, `OrderIndex`, `WordCount` | pass-through from `Summary` (keeps existing bindings) |
| `ReadingTimeText` | `ReadingTimeFormatter.Format(estimate)` (observable) |
| `ReadingTimeDescription` | `ReadingTimeFormatter.Describe(estimate)` (observable) |
| `ApplySpeed(ReadingSpeed)` | recomputes the two strings in place (FR-012) |

### View-model additions

| View model | New members |
|------------|-------------|
| `ManuscriptViewModel` | `Chapters : ObservableCollection<ChapterListItem>`; `ReadingTimeText`, `ReadingTimeDescription` for the manuscript total (FR-013) |
| `EditorViewModel` | `ChapterReadingTimeText`, `ChapterReadingTimeDescription`, recomputed whenever `ChapterWordCount` changes or the speed changes (FR-004, FR-005) |
| `SettingsViewModel` *(new)* | `ReadingSpeedText` (bound to the entry), `ErrorMessage`, `HasError`, `IsDefault`, `ApplyReadingSpeedCommand`, `RestoreDefaultCommand` |
| `DataControlsViewModel` | `StoredSettings` display lines mapped from `DataInventory.Settings` |

## Relationships

```text
ReadingSpeedSettings (singleton) ──loads/saves──▶ IAppSettingsRepository ──▶ AppSetting row
        │ Current, Changed
        ├──▶ ManuscriptViewModel ──▶ ChapterListItem.ApplySpeed / manuscript total
        ├──▶ EditorViewModel     ──▶ chapter estimate
        └──▶ SettingsViewModel   (writes via TrySetAsync / ResetAsync)

ChapterSummary.WordCount / AutoSaveResult.ChapterWordCount ─┐
                                                            ├─▶ ReadingTimeEstimator ─▶ ReadingTimeFormatter
ReadingSpeedSettings.Current ───────────────────────────────┘
```
