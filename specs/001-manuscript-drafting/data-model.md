# Phase 1 Data Model: Manuscript Drafting

**Feature**: `001-manuscript-drafting` | **Date**: 2026-07-30

Derived from the spec's Key Entities and Functional Requirements. This is the logical model
persisted in the single encrypted SQLite database (see [research.md](./research.md) §2). Domain
entities live in `InkWell.Domain/Entities`; persistence mapping lives in `InkWell.Infrastructure`.

## Entity overview

```text
Manuscript 1───* Chapter 1───* InlineImage
     │
     ├───* Character
     ├───* PlotThread
     ├───0..1 DailyGoal
     └───* DailyWritingRecord
```

All child entities are scoped to (owned by) a Manuscript. Deleting a Manuscript cascades to all
of its chapters, images, characters, plot threads, goal, and writing records (FR-018, SC-008).

---

## Manuscript

A novel project; the aggregate root.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | Generated on create |
| `Title` | string | Required, 1–200 chars, trimmed; non-unique |
| `CreatedAt` | DateTimeOffset | Set on create, immutable |
| `ModifiedAt` | DateTimeOffset | Updated on any change to the manuscript or its children |

- **Relationships**: owns ordered `Chapter[]`, `Character[]`, `PlotThread[]`, at most one `DailyGoal`,
  and a history of `DailyWritingRecord[]`.
- **Rules**: FR-001 (create/rename/delete). Rename updates `ModifiedAt`. Delete requires confirmation
  (FR-005) and cascades (FR-018).

## Chapter

A unit of the manuscript containing markdown prose.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | Generated on create |
| `ManuscriptId` | GUID (FK) | Required; indexed |
| `Title` | string | Required, 1–200 chars, trimmed |
| `ContentMarkdown` | string (TEXT) | May be empty; markdown source, may contain `![alt](inkwell-img://{imageId})` or data-URI image refs |
| `OrderIndex` | int | ≥ 0; unique within a manuscript; contiguous after reorder |
| `WordCount` | int | Cached prose word count (derived; recomputed on save) |
| `CreatedAt` / `ModifiedAt` | DateTimeOffset | `ModifiedAt` bumped on every autosave commit |

- **Relationships**: belongs to one Manuscript; owns `InlineImage[]`.
- **Rules**: FR-002 (add/rename/reorder/delete), FR-003 (markdown, persisted across sessions),
  FR-004 (autosave). `WordCount` is prose-only via `ProseWordCounter` (FR-009, SC-005) — never trusted
  from the client; recomputed server-side (domain) on save.
- **Ordering**: reorder is a transaction that rewrites affected `OrderIndex` values to stay contiguous;
  persisted order survives restart (US1 scenario 3). `ChapterOrdering` domain service owns the logic.
- **State**: none beyond existence; deletion requires confirmation (FR-005).

## InlineImage

An image embedded within a chapter's content — bytes stored in the encrypted DB (FR-003a).

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | Referenced from markdown as `inkwell-img://{Id}` (or resolved to data-URI for the editor) |
| `ChapterId` | GUID (FK) | Required; indexed; cascade-deletes with chapter |
| `Bytes` | BLOB | Required; the embedded image bytes (rowid-addressed; **never** in the chapter row) |
| `MimeType` | string | e.g. `image/png`, `image/jpeg`; required |
| `AltText` | string? | Optional; absence surfaced as an accessibility gap, not blocked (edge case, FR-019) |
| `ByteLength` | int | Denormalized size for quick manifest/export sizing |
| `CreatedAt` | DateTimeOffset | Set on insert |

- **Rules**: bytes are copied into the store on insert so the manuscript is self-contained even if the
  source file moves/deletes (FR-003a). Loaded lazily on demand, streamed on export. Missing `AltText` is
  permitted but flagged (accessibility edge case).
- **Storage note**: kept in a separate table from `Chapter` so frequent chapter autosaves never rewrite
  image pages (research.md §2).

## Character

A person in the story, scoped to a manuscript.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | |
| `ManuscriptId` | GUID (FK) | Required; indexed |
| `Name` | string | Required, 1–200 chars, trimmed |
| `Notes` | string | Freeform; may be empty |
| `CreatedAt` / `ModifiedAt` | DateTimeOffset | |

- **Rules**: FR-013 (create/edit/delete). Delete requires confirmation (FR-005). Deleting a character
  referenced in notes removes only the entry and never corrupts the manuscript (edge case).

## PlotThread

A narrative thread tracked across the manuscript.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | |
| `ManuscriptId` | GUID (FK) | Required; indexed |
| `Title` | string | Required, 1–200 chars, trimmed |
| `Notes` | string | Freeform; may be empty |
| `CreatedAt` / `ModifiedAt` | DateTimeOffset | |

- **Rules**: FR-014 (create/edit/delete). Delete requires confirmation (FR-005); same isolation guarantee
  as Character.

## DailyGoal

The writer's daily word-count target (at most one active per manuscript).

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | |
| `ManuscriptId` | GUID (FK) | Required; unique (one goal per manuscript) |
| `TargetWords` | int | > 0 when active |
| `IsActive` | bool | Set/clear the goal (FR-010) |
| `CreatedAt` / `ModifiedAt` | DateTimeOffset | |

- **Rules**: FR-010 (set/change/clear). Clearing sets `IsActive=false` while retaining history.

## DailyWritingRecord

Words written on a given local-calendar day — the writing history.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID (PK) | |
| `ManuscriptId` | GUID (FK) | Required; indexed |
| `Date` | Date (local calendar day) | Required; unique per (ManuscriptId, Date) |
| `WordsWritten` | int | ≥ 0; net prose words attributed to that day |
| `GoalTarget` | int? | Snapshot of the target that applied that day |
| `GoalMet` | bool | Derived: `WordsWritten >= GoalTarget` when a target applied |

- **Rules**: FR-011/FR-012, SC-005. Progress resets each new calendar day; the target persists and prior
  days are retained (US3 scenario 4). Words typed after midnight count to the new day. Day boundaries use
  the device local time zone via `IClock`. `DailyProgressCalculator` (domain) computes attribution and
  met/exceeded; `GoalEvaluator` distinguishes *met* (==) from *exceeded* (>) for no-color-alone display.

---

## Derived values (not stored, or cached-and-recomputed)

- **Chapter prose word count** — computed by `ProseWordCounter` over the Markdig AST, excluding markdown
  syntax tokens and inline-image markers (FR-009, SC-005). Cached in `Chapter.WordCount`, recomputed on save.
- **Manuscript word count** — sum of chapter `WordCount`s (FR-009).
- **Daily progress %** and **remaining words** — from `DailyWritingRecord.WordsWritten` vs. active
  `DailyGoal.TargetWords` (FR-011).

## Persistence & integrity notes

- Single SQLCipher-encrypted SQLite DB; WAL + `synchronous=NORMAL`; foreign keys ON with cascade delete.
- Indexes: `Chapter(ManuscriptId, OrderIndex)`, `InlineImage(ChapterId)`, `Character(ManuscriptId)`,
  `PlotThread(ManuscriptId)`, unique `DailyWritingRecord(ManuscriptId, Date)`, unique `DailyGoal(ManuscriptId)`.
- Auto-save writes a chapter's prose + its `DailyWritingRecord` upsert in one transaction so counts never
  diverge from content (research.md §2).
- "Delete all app data" (FR-018, SC-008) drops every table and the encryption key from `SecureStorage`,
  leaving no residual content.
