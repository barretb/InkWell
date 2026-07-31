# Contract: Editor ↔ Host Bridge

**Serves**: User Story 1 & 2 (P1/P2) · FR-003, FR-003a, FR-004, FR-007, FR-008

The CodeMirror 6 editor runs in a `HybridWebView`; this contract defines the message bridge between
the JS editor and the C# host, plus the autosave and distraction-free behaviors. Markdown is the
document buffer (research.md §1).

## JS → C# (via `HybridWebView.InvokeDotNet` / `SendRawMessage`)

| Message | Payload | Host behavior |
|---|---|---|
| `contentChanged` | `{ chapterId, markdown, revision }` | Debounced (~0.5–2 s idle) autosave: recompute prose `WordCount` (`ProseWordCounter`), persist chapter markdown + upsert today's `DailyWritingRecord` in one transaction (FR-004). Returns updated counts for live display. |
| `flushNow` | `{ chapterId, markdown }` | Force-commit immediately — sent on focus-loss, chapter switch, distraction-free toggle, and app-lifecycle sleep/close, so unexpected shutdown loses ≤ last moments (SC-003). |
| `insertImageRequested` | `{ chapterId, bytes(base64), mimeType, altText? }` | Persist an `InlineImage` (bytes into encrypted store, FR-003a); return a stable reference the editor renders inline. Downscale/cap oversized images before store. |
| `imageMissingAltText` | `{ imageId }` | Host records the accessibility gap for the a11y indicator (edge case; not blocked). |

## C# → JS (via `InvokeJavaScriptAsync` / `EvaluateJavaScriptAsync`)

| Call | Payload | Editor behavior |
|---|---|---|
| `loadChapter` | `{ chapterId, markdown, images[] }` | Initialize a fresh CodeMirror state for the chapter (one instance per chapter). Resolves image references to data-URIs for inline rendering. |
| `setDistractionFree` | `{ enabled }` | Toggle chrome-hidden layout; preserve cursor/selection and content across the transition (FR-008, SC-006). |
| `focusEditor` | `{ selection? }` | Restore focus and caret (e.g. after exiting distraction-free) with content unchanged. |

## Distraction-free mode (FR-007, FR-008)

- Entered/exited via **both** a visible control and a keyboard shortcut; both routes preserve cursor
  position and content (US2 scenarios). Enter/exit completes in <1 s (SC-006).
- Hides navigation, panels, and non-essential toolbars; editing area fills available space; autosave
  behaves identically to normal mode (US2 scenario 2).

## Accessibility

- Editor DOM exposes `role="textbox"`, semantic HTML, and ARIA live regions for word-count/goal updates;
  live-preview decorations keep underlying text in the accessibility tree (FR-019).
- A native-MAUI `Editor` **accessibility-mode fallback** (plain markdown source) is available for
  screen-reader users and satisfies keyboard-only completion (SC-007).

## Contract tests

- Debounced autosave persists within the durability window; `flushNow` commits synchronously.
- Round-trip: `loadChapter` → edit → `contentChanged` → reopen store yields identical markdown.
- Image insert embeds bytes and survives deletion of the (simulated) source (FR-003a).
- Distraction-free enter/exit preserves cursor and content; both toggle routes work by keyboard.
