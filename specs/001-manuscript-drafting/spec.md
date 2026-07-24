# Feature Specification: Manuscript Drafting

**Feature Branch**: `001-manuscript-drafting`

**Created**: 2026-07-24

**Status**: Draft

**Input**: User description: "Build an app for novelists to draft chapters and organize them into a manuscript. Writers track characters and plot threads, set daily word-count goals, and write in a distraction-free editor. Everything works offline and saves locally."

## Clarifications

### Session 2026-07-24

- Q: How should inline images be stored (given local-first + encrypted-at-rest + nothing-leaves-device)? → A: Embed the image bytes in the app's local encrypted store; the manuscript is self-contained and survives if the original file moves or is deleted.
- Q: How should the markdown editor present content, especially in distraction-free mode? → A: Live inline rendering—the writer sees formatted prose and inline images as they type; markdown is the underlying storage format.
- Q: What should the word count (chapter, manuscript, daily goal) count? → A: Prose words only—exclude markdown syntax tokens and image markers; count only reader-facing prose.
- Q: What granularity should EPUB & PDF export support? → A: Both whole-manuscript and per-chapter export, with inline images embedded.

## User Scenarios & Testing *(mandatory)*

<!--
  User stories are prioritized as independently testable journeys. Each P-level
  story is a standalone slice that delivers value on its own.
-->

### User Story 1 - Draft and Organize a Manuscript (Priority: P1)

A novelist creates a new manuscript, adds chapters, writes prose in each chapter, and reorders chapters as the story evolves. The manuscript and all its chapters persist locally so the writer can close the app and return later with everything intact.

**Why this priority**: This is the core of the product. Without the ability to draft chapters and assemble them into a manuscript, none of the other features have anything to attach to. This story alone is a viable MVP—a writer could use the app purely as a chapter-organized drafting tool.

**Independent Test**: Create a manuscript, add three chapters, write text in each, reorder them, close and reopen the app, and confirm all content and ordering are preserved.

**Acceptance Scenarios**:

1. **Given** no manuscripts exist, **When** the writer creates a new manuscript and names it, **Then** the manuscript appears in their library and can be opened.
2. **Given** an open manuscript, **When** the writer adds a chapter and types prose into it, **Then** the chapter and its text are saved automatically without an explicit save action.
3. **Given** a manuscript with multiple chapters, **When** the writer reorders chapters, **Then** the new order is preserved after closing and reopening the app.
4. **Given** the writer is offline (no network connectivity), **When** they create, edit, and organize chapters, **Then** all operations succeed exactly as they would online.
5. **Given** a chapter with content, **When** the writer deletes it, **Then** they are asked to confirm and the chapter is removed only after confirmation.

---

### User Story 2 - Distraction-Free Writing (Priority: P2)

A novelist enters a focused writing mode that removes surrounding interface chrome (navigation, panels, toolbars) so only the text and minimal essential controls remain, letting them concentrate on drafting.

**Why this priority**: A distraction-free editor is a primary differentiator for writing tools and directly supports the core drafting activity, but the app is still usable for drafting (P1) without it. It elevates the writing experience rather than enabling it.

**Independent Test**: Open a chapter, activate distraction-free mode, confirm that non-essential UI is hidden and the text remains fully editable, then exit and confirm the full interface returns with content intact.

**Acceptance Scenarios**:

1. **Given** an open chapter, **When** the writer activates distraction-free mode, **Then** navigation, panels, and non-essential toolbars are hidden and the editing area fills the available space.
2. **Given** distraction-free mode is active, **When** the writer types, **Then** editing behaves identically to normal mode and content is saved automatically.
3. **Given** distraction-free mode is active, **When** the writer exits it (via keyboard shortcut or an unobtrusive control), **Then** the full interface returns with the cursor and content unchanged.

---

### User Story 3 - Track Daily Word-Count Goals (Priority: P2)

A novelist sets a daily word-count goal and sees live progress toward that goal as they write, along with the running word count of the current chapter and the whole manuscript.

**Why this priority**: Goal-tracking is a strong motivator for novelists and a recognizable feature of writing tools, but it is an enhancement on top of drafting. Writers can draft (P1) without it.

**Independent Test**: Set a daily goal of 500 words, write 200 words, confirm progress shows 200/500 (40%), write 300 more, and confirm the goal is marked met for the day.

**Acceptance Scenarios**:

1. **Given** no goal is set, **When** the writer sets a daily word-count target, **Then** the target is saved and progress tracking begins.
2. **Given** a daily goal is set, **When** the writer adds words to any chapter, **Then** the day's progress updates to reflect words written that day and shows remaining words to reach the goal.
3. **Given** the writer reaches their daily goal, **When** the goal is met, **Then** the app indicates the goal has been achieved for that day.
4. **Given** a new calendar day begins, **When** the writer opens the app, **Then** daily progress resets to zero while the target remains, and the prior day's result is retained in history.

---

### User Story 4 - Track Characters and Plot Threads (Priority: P3)

A novelist maintains a set of character profiles and plot threads associated with a manuscript, recording notes about each, so they can keep continuity across a long work.

**Why this priority**: Continuity tracking is valuable for serious novelists but is reference material alongside the manuscript rather than part of the drafting loop itself. The app delivers value without it, so it ranks below drafting, focus mode, and goals.

**Independent Test**: Create a character with notes and a plot thread with notes, associate them with a manuscript, close and reopen the app, and confirm both are retained and viewable alongside the manuscript.

**Acceptance Scenarios**:

1. **Given** an open manuscript, **When** the writer creates a character with a name and notes, **Then** the character is saved and listed for that manuscript.
2. **Given** an open manuscript, **When** the writer creates a plot thread with a title and notes, **Then** the plot thread is saved and listed for that manuscript.
3. **Given** existing characters and plot threads, **When** the writer edits or deletes one, **Then** the change persists and deletion requires confirmation.
4. **Given** the writer is drafting a chapter, **When** they open the character or plot-thread reference, **Then** they can view it without losing their place in the editor.

---

### Edge Cases

- **Unsaved work on crash/close**: If the app closes unexpectedly, the most recent edits (auto-saved) MUST be recoverable when the writer reopens the manuscript.
- **Very large manuscripts**: A manuscript with many long chapters (e.g., 150,000+ words across 50+ chapters) MUST remain responsive to open, scroll, and edit.
- **Empty states**: Opening the app with no manuscripts, a manuscript with no chapters, or a chapter with no text MUST present clear guidance rather than a blank confusing screen.
- **Word-count boundary**: Progress at exactly the goal, and progress that exceeds the goal, MUST both be handled clearly (met vs. exceeded).
- **Day rollover mid-session**: If the calendar day changes while the app is open and the writer keeps typing, words written after midnight MUST count toward the new day.
- **Deleting an in-use reference**: Deleting a character or plot thread that the writer referenced in notes MUST not corrupt or block the manuscript; only the reference entry is removed.
- **Concurrent devices (no sync)**: With cloud sync off (default), editing the same manuscript on two devices MUST not silently overwrite; each device keeps its own local copy.
- **Large/many inline images**: A chapter with several large embedded images MUST remain responsive to edit and MUST export to EPUB/PDF without failing; the encrypted store must accommodate the added image bytes.
- **Image without alt text**: Inserting an image without providing alternative text MUST be permitted but MUST be surfaced as an accessibility gap (e.g., prompt or indicator), not silently accepted as compliant.

## Data Privacy & User Consent *(mandatory)*

### User Consent Strategy

- **Data collected/created**: Manuscripts, chapters and their markdown prose, inline images embedded in chapters, character profiles and notes, plot threads and notes, daily word-count goals, and daily writing history. All of it is content the writer authors or supplies; none is collected from third parties.
- **Consent required**: No user content leaves the device under any circumstance without explicit, informed opt-in. In this feature scope there is no cloud, telemetry, or analytics; if any such capability is added later it MUST be off by default and require a clear opt-in.
- **Understanding and control**: Writers can view, export, and delete all of their data at any time (see Data Controls). The app MUST make clear that everything is stored locally and private by default.

### Data Handling

- **Storage location**: All data is stored locally on the device and is the source of truth. Local device storage is PRIMARY; there is no cloud component in this feature.
- **Encryption**: Local data—including embedded inline image bytes—MUST be encrypted at rest using platform-standard encryption. No data is transmitted, so there is no in-transit surface in this scope.
- **Data leaving the device**: Nothing leaves the device automatically. The only outbound path is a user-initiated export (see Data Controls), which the writer explicitly triggers and directs to a location of their choosing.
- **Privacy guarantee**: Manuscripts and all user content remain private—nothing leaves the device without the writer's explicit action and consent.

### Data Controls

- Writers MUST be able to view all data the app has stored for them (manuscripts, chapters, characters, plot threads, goals, history).
- Writers MUST be able to export their manuscripts and associated data to a user-chosen location in a portable, readable format.
- Writers MUST be able to delete any manuscript and all of its associated data, and to delete all app data entirely, with confirmation for destructive actions.

## Storage & Offline Design *(mandatory)*

### Local-First Storage

- **Primary mechanism**: Local, encrypted, structured on-device storage serves as the single source of truth for manuscripts, chapters, characters, plot threads, goals, and daily history.
- **Offline-complete**: Every capability in this feature—creating and editing manuscripts and chapters, reordering, distraction-free writing, word-count goals and progress, and character/plot-thread tracking—MUST work fully offline. No feature may be blocked by lack of connectivity.
- **Data structure for querying**: Data MUST be organized so that a manuscript and its chapters, characters, and plot threads can be retrieved and updated efficiently, and so that per-day word counts can be computed quickly for goal progress. Auto-save MUST persist edits frequently enough that unexpected shutdown loses no more than the most recent moments of typing.

### Cloud Sync (if applicable)

- [x] Not applicable—feature is offline-only
- [ ] Optional cloud sync offered

This feature is offline-only. Cloud sync is explicitly out of scope for this feature and, if ever introduced, MUST be opt-in and never required for core functionality.

## Accessibility Requirements *(mandatory)*

### WCAG 2.1 AA Compliance

- All UI elements—library, editor, distraction-free mode, goal displays, and character/plot-thread views—MUST meet WCAG 2.1 Level AA.
- Color contrast MUST meet AA ratios in all provided themes; goal progress MUST NOT be conveyed by color alone (text/label indicators required).
- The full drafting workflow MUST be operable by keyboard, including entering and exiting distraction-free mode and reordering chapters.
- Screen readers MUST be able to announce structure (manuscript, chapters), editing content, word-count progress, and reference lists. Inline images MUST support alternative text so screen-reader users can perceive them.
- Accessibility verification MUST be part of the acceptance criteria for every user story above.

## Testing Requirements *(mandatory)*

### Testing Strategy

- **Unit tests**: Word-count calculation, daily-progress computation and day-rollover logic, chapter ordering, and goal met/exceeded evaluation.
- **Integration tests**: At least one per user story—draft-and-organize persistence round-trip (P1), enter/edit/exit distraction-free mode (P2), set-goal-and-track-progress (P2), and create/retain characters and plot threads (P3).
- **Accessibility tests**: Keyboard-only completion of each user story and screen-reader/contrast verification for all new UI.
- **Privacy tests**: Verify no data leaves the device without explicit user action, that local storage (including embedded image bytes) is encrypted at rest, and that export/delete controls behave as specified.
- **Export tests**: Verify EPUB and PDF export at both whole-manuscript and per-chapter granularity, and that inline images are embedded in the exported files.
- **Performance tests**: Editor responsiveness and app responsiveness with a large manuscript (see Success Criteria), validating interactions stay within the responsiveness target.

## Requirements *(mandatory)*

### Functional Requirements

**Manuscript & chapter drafting (P1)**

- **FR-001**: System MUST allow writers to create, rename, and delete manuscripts, each identified by a title.
- **FR-002**: System MUST allow writers to add, rename, reorder, and delete chapters within a manuscript.
- **FR-003**: System MUST provide a markdown-based editor for writing chapter prose and MUST preserve chapter content across sessions. Chapter content is stored as markdown, and the editor MUST present it with live inline rendering (formatted prose and inline images shown as the writer types) rather than exposing raw markdown syntax as the primary editing surface.
- **FR-003a**: Users MUST be able to insert images inline within chapter text. Inserted image bytes MUST be embedded in the app's local encrypted store so the manuscript is self-contained and the image remains available even if the original source file is moved or deleted.
- **FR-004**: System MUST automatically save edits without requiring an explicit save action, frequently enough that an unexpected shutdown loses no more than the most recent moments of typing.
- **FR-005**: System MUST require confirmation before destructive actions (deleting a manuscript, chapter, character, or plot thread).
- **FR-006**: System MUST function fully offline for every capability in this specification.

**Distraction-free writing (P2)**

- **FR-007**: System MUST provide a distraction-free writing mode that hides navigation, panels, and non-essential toolbars, leaving the editing area and minimal essential controls.
- **FR-008**: Users MUST be able to enter and exit distraction-free mode via both a visible control and a keyboard shortcut, with cursor position and content preserved across the transition.

**Word-count goals (P2)**

- **FR-009**: System MUST display a live word count for the current chapter and for the whole manuscript, counting reader-facing prose words only—markdown syntax tokens and inline image markers MUST be excluded from all counts (chapter, manuscript, and daily-goal progress).
- **FR-010**: Users MUST be able to set, change, and clear a daily word-count goal.
- **FR-011**: System MUST track words written per calendar day and display progress toward the daily goal, including words remaining and a met/exceeded indication.
- **FR-012**: System MUST reset daily progress at the start of each calendar day while retaining the goal and a history of prior days' results, correctly attributing words typed after midnight to the new day.

**Characters & plot threads (P3)**

- **FR-013**: Users MUST be able to create, edit, and delete character profiles (name plus freeform notes) associated with a manuscript.
- **FR-014**: Users MUST be able to create, edit, and delete plot threads (title plus freeform notes) associated with a manuscript.
- **FR-015**: Users MUST be able to view characters and plot threads for the current manuscript without losing their place in the editor.

**Privacy, data control & accessibility (cross-cutting)**

- **FR-016**: System MUST store all user data locally and encrypted at rest, as the single source of truth.
- **FR-017**: System MUST NOT transmit any user content off the device except through an explicit, user-initiated export.
- **FR-018**: Users MUST be able to view, export, and delete all of their data. Export MUST support both EPUB and PDF formats, at both whole-manuscript and single-chapter granularity, with inline images embedded in the exported file, written to a user-chosen location.
- **FR-019**: All user-facing functionality MUST meet WCAG 2.1 AA, including full keyboard operability and screen-reader support, and MUST NOT convey status by color alone.

### Key Entities *(include if feature involves data)*

- **Manuscript**: A novel project. Attributes: title, creation/modification time, ordered set of chapters; owns its characters and plot threads.
- **Chapter**: A unit of the manuscript. Attributes: title, prose content stored as markdown (may contain inline images), order position within the manuscript, prose word count.
- **Inline Image**: An image embedded within a chapter's content. Attributes: image bytes stored in the local encrypted store, position within the chapter's text, optional caption/alt text (see Accessibility). Belongs to a chapter.
- **Character**: A person in the story, scoped to a manuscript. Attributes: name, freeform notes.
- **Plot Thread**: A narrative thread tracked across the manuscript. Attributes: title, freeform notes.
- **Daily Goal**: The writer's word-count target. Attributes: target word count, active state.
- **Daily Writing Record**: Words written on a given calendar day. Attributes: date, words written that day, goal-met status—forms the writing history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new writer can create a manuscript, add a chapter, and begin writing within 1 minute of first opening the app.
- **SC-002**: Every capability in this specification works with networking fully disabled; 100% of user stories complete offline with no degraded behavior.
- **SC-003**: No user edit is lost on unexpected shutdown beyond the last few seconds of typing; on reopening, auto-saved content is restored in 100% of test cases.
- **SC-004**: In a manuscript of at least 150,000 words across 50+ chapters, opening a chapter and typing remain responsive, with on-screen feedback to keystrokes perceived as instantaneous.
- **SC-005**: Word-count progress shown to the writer matches an independent prose-word count of the same text (excluding markdown syntax and image markers) 100% of the time, and daily progress correctly rolls over across calendar-day boundaries.
- **SC-009**: A writer can export a manuscript—and any single chapter—to both EPUB and PDF, and every inline image present in the source appears embedded in the exported file, in 100% of test cases.
- **SC-006**: A writer can enter and exit distraction-free mode and return to their exact cursor position in under 1 second with no content change.
- **SC-007**: Every user story can be completed end-to-end using keyboard only and passes WCAG 2.1 AA contrast and screen-reader checks.
- **SC-008**: A writer can export a complete manuscript and delete all their data using in-app controls, with no residual user content remaining after deletion in 100% of test cases.

## Assumptions

- **Single local user per device**: The app serves one writer on their own device; multi-user accounts, collaboration, and shared editing are out of scope for this feature.
- **No cloud/backup service in scope**: There is no server, account, sync, or online backup; the only way content moves off-device is a user-initiated export. Sync may be a future opt-in feature but is excluded here.
- **Cross-platform via the project stack**: The app runs on the platforms supported by the project's chosen framework (per the constitution); this spec is platform-agnostic about behavior.
- **Word counting is whitespace-delimited prose**: A "word" is a whitespace-separated token of reader-facing prose; markdown syntax tokens and inline image markers are excluded. This convention is used consistently for chapter, manuscript, and daily counts (see Clarifications 2026-07-24).
- **"Daily" follows the device's local calendar day**: Day boundaries and rollover use the device's local time zone.
- **Chapter content is markdown; images are embedded**: Prose is authored and stored as markdown, edited via live inline rendering, and inline images are embedded (bytes copied) into the local encrypted store rather than referenced by external path (see Clarifications 2026-07-24). The specific markdown feature set (headings, emphasis, lists, etc.) beyond prose and inline images is a planning-phase detail.
- **Export supports EPUB and PDF**: Export produces EPUB and PDF files, at whole-manuscript and per-chapter granularity, with inline images embedded, written to a user-chosen location. Additional export formats are out of scope for this feature.
