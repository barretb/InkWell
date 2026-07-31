# Application Contracts: Manuscript Drafting

**Feature**: `001-manuscript-drafting` | **Date**: 2026-07-30

InkWell is an offline desktop/mobile app with no network API. Its "contracts" are the
**application-layer interfaces (ports)** that the `InkWell.Maui` presentation layer depends on and
that `InkWell.Infrastructure` implements, plus the **JS↔C# editor bridge** across the WebView. These
are the stable seams verified by contract/integration tests (constitution §I, §II).

Each contract lists operations as `Method(inputs) → output` with the behavior, errors, and the
requirements/user stories it serves. Types reference [data-model.md](../data-model.md).

| Contract | Serves | File |
|---|---|---|
| Manuscript & chapter lifecycle | US1 (P1), FR-001..006 | [manuscript-service.md](./manuscript-service.md) |
| Editor ↔ host bridge (autosave, images, distraction-free) | US1/US2 (P1/P2), FR-003/003a/004/007/008 | [chapter-editor-bridge.md](./chapter-editor-bridge.md) |
| Word count & daily goals | US3 (P2), FR-009..012 | [word-count-and-goals.md](./word-count-and-goals.md) |
| Characters & plot threads | US4 (P3), FR-013..015 | [reference-service.md](./reference-service.md) |
| Export & data controls | Cross-cutting, FR-016..018 | [export-service.md](./export-service.md) |

**Conventions**
- All async operations are `Task`-returning and run off the UI thread (constitution §V).
- Destructive operations do **not** self-confirm; the caller (ViewModel) obtains confirmation first
  (FR-005). Contracts assume confirmation already happened.
- Every operation works offline with no network dependency (FR-006, SC-002).
- Repository writes are transactional; a failed write leaves the store unchanged.
