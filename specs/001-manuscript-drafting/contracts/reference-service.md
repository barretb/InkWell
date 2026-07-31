# Contract: Characters & Plot Threads

**Serves**: User Story 4 (P3) · FR-013, FR-014, FR-015

Interface: `IReferenceRepository` (Application layer), implemented by a SQLCipher-backed repository.
Characters and plot threads are reference material scoped to a manuscript; viewing them must not
disturb the editor (FR-015).

## Characters (FR-013)

| Operation | Behavior & rules |
|---|---|
| `ListCharacters(manuscriptId) → Character[]` | All characters for the manuscript, name-sorted. |
| `CreateCharacter(manuscriptId, name, notes) → Character` | Validates name (1–200, trimmed); notes freeform/optional. Listed immediately (US4 scenario 1). |
| `UpdateCharacter(id, name, notes) → void` | Persists edits. Error: `NotFound`, `ValidationError`. |
| `DeleteCharacter(id) → void` | Removes only this entry; never corrupts the manuscript even if referenced in notes (edge case). Caller confirmed first (FR-005). |

## Plot threads (FR-014)

| Operation | Behavior & rules |
|---|---|
| `ListPlotThreads(manuscriptId) → PlotThread[]` | All plot threads for the manuscript. |
| `CreatePlotThread(manuscriptId, title, notes) → PlotThread` | Validates title (1–200); notes freeform. Listed immediately (US4 scenario 2). |
| `UpdatePlotThread(id, title, notes) → void` | Persists edits. |
| `DeletePlotThread(id) → void` | Same isolation guarantee as character delete; confirmed first (FR-005). |

## Viewing without losing place (FR-015)

- The reference panel/view opens alongside or over the editor and returns focus to the exact caret
  position on close (US4 scenario 4) — presentation guarantee verified in UI tests.

## Contract tests

- Create→list→reopen-store round-trip retains characters and plot threads (US4 independent test, SC-008
  persistence).
- Edit persists; delete removes only the target row and leaves the manuscript intact.
