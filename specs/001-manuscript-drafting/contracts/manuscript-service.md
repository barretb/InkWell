# Contract: Manuscript & Chapter Lifecycle

**Serves**: User Story 1 (P1) · FR-001, FR-002, FR-004, FR-005, FR-006

Interfaces: `IManuscriptRepository`, `IChapterRepository` (Application layer) and the
`ManuscriptUseCases` orchestrating them. Implemented by SQLCipher-backed repositories.

## Manuscripts

| Operation | Behavior & rules |
|---|---|
| `ListManuscripts() → ManuscriptSummary[]` | Returns all manuscripts (Id, Title, ModifiedAt, chapter count) for the library, newest-modified first. Empty list → caller shows empty-state guidance (edge case). |
| `CreateManuscript(title) → Manuscript` | Trims/validates title (1–200). Persists with `CreatedAt=ModifiedAt=now`. Appears in library immediately (US1 scenario 1). |
| `RenameManuscript(id, title) → void` | Validates title; updates `ModifiedAt`. Errors: `NotFound`, `ValidationError`. |
| `DeleteManuscript(id) → void` | Cascade-deletes all chapters, images, characters, plot threads, goal, and records in one transaction (FR-018). Caller confirmed first (FR-005). Error: `NotFound`. |
| `GetManuscript(id) → ManuscriptDetail` | Loads manuscript with ordered chapter summaries (Title, OrderIndex, WordCount) — **not** full chapter content or image bytes (loaded on open). |

## Chapters

| Operation | Behavior & rules |
|---|---|
| `AddChapter(manuscriptId, title) → Chapter` | Appends at `OrderIndex = max+1`, empty content. Bumps manuscript `ModifiedAt`. |
| `RenameChapter(chapterId, title) → void` | Validates title. Errors: `NotFound`, `ValidationError`. |
| `ReorderChapters(manuscriptId, orderedChapterIds[]) → void` | Rewrites `OrderIndex` to the given order, contiguous from 0, in one transaction. Persisted order survives restart (US1 scenario 3). Error: `ValidationError` if the id set doesn't match the manuscript's chapters exactly. |
| `DeleteChapter(chapterId) → void` | Cascade-deletes the chapter's images; re-packs remaining `OrderIndex` values. Caller confirmed first (FR-005). |
| `GetChapterContent(chapterId) → ChapterContent` | Returns markdown + resolved inline-image references for editor load. |

## Guarantees

- All operations are transactional and fully offline (FR-006). A failure rolls back with no partial state.
- Content persistence and autosave are covered by the [editor bridge contract](./chapter-editor-bridge.md).
- **Contract tests**: create→list round-trip; rename persists; reorder persists across a simulated
  restart (reopen store); delete cascades leave no orphan rows (verifies SC-008 at the data layer).
