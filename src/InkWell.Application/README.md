# InkWell.Application

Use cases and the ports they depend on. This layer orchestrates domain services and repositories
without knowing how anything is stored or rendered.

- `Abstractions/` — repository and service interfaces (`IManuscriptRepository`,
  `IChapterRepository`, `IInlineImageRepository`, `IReferenceRepository`, `IDailyGoalRepository`,
  `IWritingHistoryRepository`, `IKeyStore`, `IClock`, `IMarkdownService`, `IExportService`) plus the
  DTOs crossing the boundary
- `UseCases/` — manuscript, chapter, autosave, goal, reference, and export orchestration

Implementations of these ports live in `InkWell.Infrastructure`. See
[contracts/](../../specs/001-manuscript-drafting/contracts/) for the behavior each port must honor.
