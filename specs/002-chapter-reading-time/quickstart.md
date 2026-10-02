# Quickstart: Validating Chapter Reading Time Estimates

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/reading-time-and-settings.md](./contracts/reading-time-and-settings.md)

How to prove the feature works end to end once it's implemented. Behaviour details live in the
contract and data model; this guide only says what to run and what you should see.

## Prerequisites

- .NET 10 SDK with the MAUI workload (same as feature 001; see `specs/001-manuscript-drafting/quickstart.md`).
- Branch `002-chapter-reading-time` checked out.

## 1. Automated suite

```bash
dotnet test tests/InkWell.Domain.Tests           # ReadingSpeed parse/range; estimator rounding; formatter text
dotnet test tests/InkWell.Application.Tests      # ReadingSpeedSettings load/set/reset/Changed against a fake repository
dotnet test tests/InkWell.Infrastructure.Tests   # v1→v2 migration; AppSetting round-trip; inventory; delete-all; encryption
dotnet test tests/InkWell.Maui.UiTests           # per-story flows, accessibility, performance
dotnet test InkWell.slnx                         # everything; expect 0 failing
```

Expected: all green, including the pre-existing 001 suite. Pay particular attention to:
- the `SchemaMigrationTests` version assertion, which now expects `user_version = 2`;
- the chapter-list tests, which now go through `ChapterListItem`.

## 2. Boundary values to see in the domain tests

| Words | Speed | Expected display |
|------:|------:|------------------|
| 0 | 238 | `—` |
| 100 | 238 | `< 1 min` |
| 119 | 238 | `1 min` (exactly 30 s, so it rounds up) |
| 2,380 | 238 | `10 min` |
| 2,000 | 238 | `8 min` |
| 2,000 | 200 | `10 min` |
| 11,900 | 238 | `50 min` |
| 17,850 | 238 | `1 hr 15 min` |
| 71,400 | 238 | `5 hr` |

## 3. Manual walk-through (Windows)

```bash
dotnet build -t:Run -f net10.0-windows10.0.19041.0 src/InkWell.Maui/InkWell.Maui.csproj
```

1. **US1, per-chapter estimates**: Create a manuscript with four chapters. Leave one empty, paste
   about 100 words into another, and about 2,400 words into a third. The chapter list shows `—`, `< 1 min`,
   and `10 min`. Open the long chapter: the editor status bar shows `10 min` under the word counts.
   Delete a paragraph and pause; the estimate drops within about a second, with no save action.
2. **US3, manuscript total**: The manuscript header shows the total estimate. Delete a chapter and
   the total decreases.
3. **US2, Settings**: From the Library, choose **Settings**. The field shows `238`. Type `200`, press
   Enter. Go back: the 2,400-word chapter now shows `12 min`. Open the editor: same value.
   - Type `99` and Tab away: an error names the 100–600 range and the field reverts.
   - Type `abc`: same error.
   - Choose **Restore default (238)**: estimates return to the original values.
   - Set `300`, close the app completely, reopen: Settings still shows `300`.
4. **Settings from every main screen**: The Settings entry is present on the Library, Manuscript, and
   Editor pages, and absent in distraction-free mode.
5. **Data Controls**: With a custom speed set, Data Controls lists "Reading speed: 300 words per
   minute". After **Restore default**, the line disappears. After **Delete all app data**, Settings
   shows `238`.

## 4. Accessibility checks (Narrator on Windows, VoiceOver on macOS)

- Tab from the Library to **Settings**, into the field, to **Restore default**, with no mouse.
- A chapter row reads "estimated reading time 10 minutes" (full words, no "min").
- Typing in the editor produces **no** reading-time announcements; focusing the estimate reads it.
- An invalid speed announces the error message once.
- At 200% text size, estimates in the chapter list and editor status bar aren't cut off.

## 5. Performance check

Run `tests/InkWell.Maui.UiTests/Performance/LargeManuscriptPerformanceTests` (150,000 words across 50+
chapters). It should still pass its existing budgets with estimates enabled, and changing the speed
updates all 50+ rows within 1 second (SC-005).

## 6. Privacy check

`tests/InkWell.Infrastructure.Tests/Privacy` asserts that reading the database file without the key
can't recover the `AppSetting` value and that the feature performs no network I/O.
