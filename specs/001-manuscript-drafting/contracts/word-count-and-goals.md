# Contract: Word Count & Daily Goals

**Serves**: User Story 3 (P2) · FR-009, FR-010, FR-011, FR-012

Pure domain services (`ProseWordCounter`, `DailyProgressCalculator`, `GoalEvaluator`) plus
`IWritingHistoryRepository` and `IDailyGoalRepository`. `IClock` supplies the device local calendar
day (injected for testability).

## Word counting (FR-009, SC-005)

| Operation | Behavior |
|---|---|
| `ProseWordCounter.Count(markdown) → int` | Parses markdown to the Markdig AST and counts whitespace-delimited tokens **only** within literal prose inline text. Excludes markdown syntax tokens, link/image markup, and inline-image markers. Pure and deterministic. |
| `GetManuscriptWordCount(manuscriptId) → int` | Sum of chapter `WordCount`s (FR-009). |

**Contract tests** (unit, TDD): counts match an independent prose count for samples with headings,
emphasis, lists, links, and embedded images (SC-005) — syntax and image markers never counted.

## Daily goals (FR-010, FR-011, FR-012)

| Operation | Behavior |
|---|---|
| `SetDailyGoal(manuscriptId, targetWords) → DailyGoal` | Validates `target > 0`; sets/updates the single active goal. Tracking begins (US3 scenario 1). |
| `ClearDailyGoal(manuscriptId) → void` | Deactivates the goal; history retained (FR-010). |
| `GetTodayProgress(manuscriptId) → DailyProgress` | Returns `{ wordsWritten, target, remaining, status }` for the current local day, where `status ∈ { NoGoal, InProgress, Met, Exceeded }`. `remaining = max(0, target − wordsWritten)`. Met = `words == target`; Exceeded = `words > target` (edge case: boundary vs. exceed). |
| `RecordWordsForToday(manuscriptId, deltaWords) → void` | Called by autosave; upserts today's `DailyWritingRecord` via `IClock` local day. Words after midnight attribute to the new day (FR-012, US3 scenario 4). |
| `GetHistory(manuscriptId, range) → DailyWritingRecord[]` | Prior days' results (words, goal target snapshot, met flag) — the writing history (FR-012). |

## Rollover & display rules

- `DailyProgressCalculator` attributes words to the day they were typed using local calendar boundaries;
  a new day resets progress to zero while target and history persist (US3 scenario 4).
- Status is conveyed by **text/label**, never color alone (FR-019, SC-007); `status` enum drives an
  accessible indicator.

**Contract tests**: 200/500 → InProgress 40%, remaining 300; +300 → Met; exceeding → Exceeded; a
clock advanced past midnight resets today and preserves the prior record (SC-005).
