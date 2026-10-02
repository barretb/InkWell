# Contract: Reading Time & Application Settings

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

InkWell exposes no network API. These are the in-process contracts between layers, written as the
C# surface each layer depends on. Signatures are normative; bodies are not shown.

---

## Domain: `InkWell.Domain`

```csharp
public readonly record struct ReadingSpeed
{
    public const int MinimumWordsPerMinute = 100;
    public const int MaximumWordsPerMinute = 600;
    public static ReadingSpeed Default { get; }            // 238
    public int WordsPerMinute { get; }
    public bool IsDefault { get; }
    public static DomainResult<ReadingSpeed> TryCreate(int wordsPerMinute);
    public static DomainResult<ReadingSpeed> TryParse(string? text);
}

public enum ReadingTimeKind { None, UnderOneMinute, Minutes }

public readonly record struct ReadingTimeEstimate(ReadingTimeKind Kind, int TotalMinutes)
{
    public int Hours { get; }
    public int RemainderMinutes { get; }
}

public static class ReadingTimeEstimator
{
    public static ReadingTimeEstimate Estimate(int wordCount, ReadingSpeed speed);
}

public static class ReadingTimeFormatter
{
    public static string Format(ReadingTimeEstimate estimate);    // "—", "< 1 min", "10 min", "1 hr 15 min"
    public static string Describe(ReadingTimeEstimate estimate);  // full words for screen readers
}
```

**Guarantees**
- `Estimate` is pure and total: any `int` word count (negatives clamp to 0) and any valid speed.
- `Estimate(w, s).Kind == None` ⇔ `w <= 0`.
- `Kind == UnderOneMinute` ⇔ `w > 0 && 2w < s.WordsPerMinute`.
- Otherwise `TotalMinutes == (2w + s) / (2s)` (integer division; round half up).
- `TryParse` error message names the allowed range: "Enter a whole number from 100 to 600."
- The manuscript total is `Estimate(sum of chapter words, s)`, **never** the sum of chapter estimates.

## Application: `InkWell.Application`

```csharp
public interface IAppSettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, DateTimeOffset timestamp, CancellationToken ct = default);
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<StoredSetting>> GetAllAsync(CancellationToken ct = default);
}

public static class AppSettingKeys
{
    public const string ReadingWordsPerMinute = "reading.wordsPerMinute";
}

/// Singleton. The single in-memory owner of the active reading speed.
public sealed class ReadingSpeedSettings
{
    public ReadingSpeed Current { get; }                         // Default until LoadAsync completes
    public event EventHandler<ReadingSpeed>? Changed;
    public Task LoadAsync(CancellationToken ct = default);       // idempotent; invalid/missing → Default
    public Task<DomainResult<ReadingSpeed>> TrySetAsync(string? input, CancellationToken ct = default);
    public Task ResetAsync(CancellationToken ct = default);
}
```

**Behaviour**
| Call | Persisted | `Current` | `Changed` raised |
|------|-----------|-----------|------------------|
| `LoadAsync`, no row | — | Default | no |
| `LoadAsync`, row `"200"` | — | 200 | yes, if different from previous |
| `LoadAsync`, row `"abc"` / `"2380"` | — (row left as is) | Default | no |
| `TrySetAsync("250")` | upsert `"250"` | 250 | yes |
| `TrySetAsync("250")` when already 250 | no write | 250 | no |
| `TrySetAsync("99")` / `("abc")` / `("")` | no write | unchanged | no; returns failure |
| `TrySetAsync("238")` when custom | **row removed** (equals default) | Default | yes |
| `ResetAsync` | row removed | Default | yes, if was custom |
| any call when the store throws | no change | unchanged | no; exception surfaces to caller for `IErrorPresenter` |

`Changed` is raised on the caller's synchronization context after persistence succeeds, never before:
a failed write must not leave the UI showing a speed that won't survive a restart.

**Inventory**: `IDataControlsRepository.GetInventoryAsync` returns `DataInventory.Settings` from
`IAppSettingsRepository.GetAllAsync` semantics (all rows, ordered by `Key`).

## Infrastructure: `InkWell.Infrastructure`

- `AppSettingsRepository : IAppSettingsRepository`, SQLCipher-backed through `ISqliteConnectionFactory`
  leases, same as `DailyGoalRepository`. Upsert via `INSERT … ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value, ModifiedAt=excluded.ModifiedAt`.
- `DatabaseMigrator`: `CurrentVersion = 2`; v2 step creates `AppSetting`; `TableNames` includes it.

## Presentation & UI: `InkWell.Presentation`, `InkWell.Maui`

| Surface | Binding | Accessible description |
|---------|---------|------------------------|
| Chapter list row (`ManuscriptPage`) | `ChapterListItem.ReadingTimeText`, beside the word count | `ChapterListItem.ReadingTimeDescription` |
| Manuscript header (`ManuscriptPage`) | `ManuscriptViewModel.ReadingTimeText` | `ManuscriptViewModel.ReadingTimeDescription` |
| Editor status bar (`EditorPage`) | `EditorViewModel.ChapterReadingTimeText`, own label below `CountsSummary` | `ChapterReadingTimeDescription`; **never** passed to `SemanticScreenReader.Announce` |
| Settings (`SettingsPage`, route `settings`) | `Entry` ↔ `ReadingSpeedText`; apply on `Completed` and `Unfocused`; "Restore default (238)" button | Label "Reading speed (words per minute)", hint "100 to 600"; error text announced once when shown |
| Entry points | Library: "Settings" button; Manuscript & Editor: "Settings" header button (editor header is hidden in distraction-free mode) | Hint "Opens application settings" |
| Data Controls | "Settings stored" section listing `StoredSetting`s by label; hidden when none | Each line readable as text |

`Routes.Settings = "settings"`; registered in `AppShell.xaml.cs`; `SettingsViewModel`/`SettingsPage`
transient; `ReadingSpeedSettings` and `IAppSettingsRepository` singletons in `MauiProgram`.

`ReadingSpeedSettings.LoadAsync` is awaited by each view model's existing `LoadAsync` before the first
estimate is computed (cheap after the first call), so no screen ever briefly shows estimates at
the default speed and then jumps.
