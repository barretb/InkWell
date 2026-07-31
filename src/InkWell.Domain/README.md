# InkWell.Domain

Entities and pure domain services. This layer depends on nothing but the BCL and Markdig, so every
rule in it is unit-testable without a device, a database, or a clock.

- `Entities/` — `Manuscript`, `Chapter`, `InlineImage`, `Character`, `PlotThread`, `DailyGoal`,
  `DailyWritingRecord` (see [data-model.md](../../specs/001-manuscript-drafting/data-model.md))
- `Services/` — `ProseWordCounter`, `ChapterOrdering`, `DailyProgressCalculator`, `GoalEvaluator`
- `Abstractions/` — value objects and domain result/error types

Nothing here may reference MAUI, SQLite, or any platform API.
