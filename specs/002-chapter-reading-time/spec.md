# Feature Specification: Chapter Reading Time Estimates

**Feature Branch**: `002-chapter-reading-time`

**Created**: 2026-10-01

**Status**: Draft

**Input**: User description: "I want the application to calculate the estimated reading time for each chapter"

## Clarifications

### Session 2026-10-01

- Q: What goes in the new application Settings menu? → A: Reading speed only for this feature; the menu is built so more settings can be added later.
- Q: How do changes in Settings take effect? → A: Automatically once a valid value is entered (on leaving the field or pressing Enter); invalid values show an error and are not applied. There is no Save button.
- Q: Where should the reading speed be stored? → A: In the encrypted local database alongside manuscripts (not in plain device preference storage); listed on the Data Controls page and removed by "delete all data". The app has no settings export, so EPUB/PDF exports do not include it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a reading-time estimate for each chapter (Priority: P1)

A novelist looking at their manuscript wants to know roughly how long each chapter takes to read. Every chapter shows an estimated reading time next to it in the chapter list, and the chapter currently open in the editor shows its estimate alongside its live word count. The estimate updates as the writer adds or removes prose, so they can judge pacing ("this chapter is a 25-minute read, the others are about 10") without leaving the app or doing arithmetic.

**Why this priority**: This is the feature as requested. On its own it gives writers a pacing signal they don't have today, and every other story builds on it.

**Independent Test**: Create a manuscript with chapters of known prose length (e.g., 0, 100, 2,380, and 11,900 words), confirm each chapter shows the expected estimate under the default reading speed, then edit one chapter and confirm its estimate updates.

**Acceptance Scenarios**:

1. **Given** a manuscript with several chapters containing prose, **When** the writer views the chapter list, **Then** each chapter shows an estimated reading time derived from its prose word count.
2. **Given** a chapter with 2,380 prose words and the default reading speed of 238 words per minute, **When** the writer views that chapter, **Then** its estimated reading time is shown as "10 min".
3. **Given** a chapter open in the editor, **When** the writer types or deletes enough prose to change the estimate, **Then** the displayed estimate updates without any explicit save or refresh action.
4. **Given** a chapter that contains markdown formatting and inline images, **When** its reading time is calculated, **Then** only reader-facing prose words count toward the estimate—the same words counted by the chapter's word count.
5. **Given** a chapter with no prose, **When** the writer views it, **Then** the estimate is shown as "—" (announced as "no estimated reading time") rather than a time.

---

### User Story 2 - Adjust the reading speed used for estimates (Priority: P2)

Different audiences read at different speeds—a middle-grade reader is slower than an adult reader of commercial thrillers. The writer opens a new application Settings menu and changes the reading speed (in words per minute) used for estimates, and all chapter estimates recalculate to match. In this feature reading speed is the only entry in Settings, but the menu is laid out so more application settings can be added later.

**Why this priority**: The default suits typical adult fiction, but writers targeting other audiences need estimates they can trust. It's worth having but not required for the basic feature to work.

**Independent Test**: With a chapter of 2,000 prose words, open Settings, change the reading speed from the default to 200 words per minute and confirm the estimate changes from about 8 minutes to 10 minutes; close and reopen the app and confirm the custom speed is still in effect.

**Acceptance Scenarios**:

1. **Given** the writer is on any main screen of the app, **When** they open the Settings menu from the main navigation, **Then** Settings opens and shows the current reading speed.
2. **Given** the default reading speed, **When** the writer sets a custom reading speed in Settings, **Then** every chapter's estimate recalculates using the new speed.
3. **Given** a custom reading speed has been set, **When** the writer closes and reopens the app, **Then** the custom speed is still in effect.
4. **Given** the writer enters a reading speed outside the allowed range or a non-numeric value, **When** they leave the field or press Enter, **Then** the value is not applied, an error message states the allowed range, and the previous speed remains in effect.
5. **Given** a custom reading speed is in effect, **When** the writer chooses to restore the default, **Then** the speed returns to 238 words per minute and estimates recalculate.

---

### User Story 3 - See the whole manuscript's reading time (Priority: P3)

Alongside per-chapter estimates, the writer sees the total estimated reading time for the whole manuscript, so they can gauge the book's overall length in reader terms ("about a 6-hour read").

**Why this priority**: A natural extension once per-chapter estimates exist, but it goes beyond the literal request.

**Independent Test**: Create a manuscript with chapters totaling 71,400 prose words, confirm the manuscript total shows "5 hr" at the default speed, then delete a chapter and confirm the total decreases.

**Acceptance Scenarios**:

1. **Given** a manuscript with multiple chapters, **When** the writer views the manuscript, **Then** a total estimated reading time is shown, calculated from the manuscript's total prose word count.
2. **Given** a manuscript total is displayed, **When** a chapter is added, edited, or deleted, **Then** the total updates to reflect the change.

---

### Edge Cases

- **Empty chapter**: A chapter with zero prose words shows "—" (announced as "no estimated reading time"), not a time estimate.
- **Very short chapter**: A chapter with prose that would read in under 30 seconds shows "< 1 min" rather than "0 min"; 30–59 seconds rounds up to "1 min".
- **Very long chapter or manuscript**: Estimates of 60 minutes or more are shown in hours and minutes (e.g., "1 hr 15 min"); whole hours drop the minutes (e.g., "5 hr").
- **Rounding**: Estimates are rounded to the nearest whole minute (half-minutes round up), so a chapter never displays fractional minutes.
- **Manuscript total vs. sum of chapters**: The manuscript total is calculated from the total word count, not by adding up the rounded chapter estimates, so it may differ slightly from the sum of the displayed chapter times. This is expected.
- **Images and formatting only**: A chapter that contains only images or markdown syntax and no prose is treated as an empty chapter.
- **Rapid typing**: Estimate updates while typing MUST NOT make typing feel slower or cause visible flicker in the editor.
- **Missing or unreadable reading speed**: If no reading speed has been saved (first launch, or after "delete all data") or the saved value cannot be read or falls outside 100–600, estimates use the default of 238 words per minute and Settings shows the default.
- **Large manuscripts**: A manuscript of 150,000+ words across 50+ chapters shows all chapter estimates without a noticeable delay when opening the manuscript.

## Data Privacy & User Consent *(mandatory)*

### User Consent Strategy

- **Data collected/created**: The only new stored data is the writer's preferred reading speed. Reading-time estimates are derived on demand from existing chapter word counts; no new manuscript content is collected.
- **Consent**: No additional consent is required. The feature runs entirely on-device and transmits nothing.
- **User understanding and control**: The reading speed setting is visible and editable in the application Settings menu, and the writer can restore the default at any time.

### Data Handling

- **Storage location**: The reading speed preference is stored in the app's encrypted local database alongside manuscripts—not in plain device preference storage, where the existing accessible-editor toggle lives.
- **Encryption**: The preference is encrypted at rest with the same protections as manuscript data.
- **Data leaving the device**: None. Estimates are computed locally and are never sent anywhere.
- **Manuscript privacy**: Chapter content is read only to count words on-device; it is never transmitted or shared.

### Data Controls

- Writers can view their current reading speed in the Settings menu.
- The reading speed preference is listed in the Data Controls page's inventory of stored data. It is a setting, not manuscript content, so it is not included in EPUB/PDF exports, and the app has no separate settings export.
- Writers can reset the reading speed to the default; "delete all data" also removes the preference, after which the default speed applies.

## Storage & Offline Design *(mandatory)*

### Local-First Storage

- **Primary mechanism**: The reading speed preference is kept in the app's existing local encrypted storage. Reading-time estimates are derived from chapter word counts and do not need to be stored separately.
- **Offline-complete**: All functionality in this feature works fully offline. No part of it depends on connectivity.
- **Data structure**: Estimates are derived from the existing prose word count of each chapter and the manuscript, so no new content structures are required.

### Cloud Sync (if applicable)

- [x] Not applicable—feature is offline-only
- [ ] Optional cloud sync offered (describe architecture and opt-in mechanism)

## Accessibility Requirements *(mandatory)*

### WCAG 2.1 AA Compliance

- Reading-time estimates MUST meet WCAG 2.1 AA color contrast requirements wherever they appear (chapter list, editor, manuscript summary).
- Screen readers MUST announce estimates in full words (e.g., "estimated reading time 1 hour 15 minutes"), not abbreviations such as "hr" or "min".
- Live estimate updates while typing MUST NOT be announced on every keystroke. Screen-reader users can read the current estimate on demand without being interrupted while writing.
- The Settings menu MUST be reachable from the main navigation by keyboard, and it and the reading speed setting MUST be fully operable by keyboard, have a visible label, and announce validation errors to assistive technology.
- Estimates MUST remain readable and must not be cut off at 200% text size.

## Testing Requirements *(mandatory)*

### Testing Strategy

- **Unit tests**: Reading-time calculation from word count and speed; rounding (including half-minute boundaries); "< 1 min" and empty-chapter states; hour/minute formatting; reading-speed validation (range limits, non-numeric input); full-word phrasing for screen readers.
- **Integration tests**: At least one per user story—chapter list and editor show correct estimates and update on edit (US1); a changed speed persists across restart and recalculates all estimates (US2); the manuscript total updates when chapters are added, edited, or deleted (US3).
- **Consistency tests**: Reading-time estimates use exactly the same prose word count shown to the writer (markdown syntax and image markers excluded).
- **Accessibility tests**: Screen-reader labels, no per-keystroke announcements, keyboard operation of the speed setting, contrast, and 200% text scaling.
- **Privacy tests**: Verify the feature makes no network requests and that the reading speed preference is stored in local encrypted storage.
- **Performance tests**: Estimate updates during typing do not slow input in a 150,000-word, 50+ chapter manuscript; opening such a manuscript shows all estimates promptly.

## Requirements *(mandatory)*

### Functional Requirements

**Per-chapter estimates (P1)**

- **FR-001**: System MUST calculate an estimated reading time for every chapter by dividing the chapter's prose word count by the active reading speed (words per minute).
- **FR-002**: System MUST use the same prose word count the app already displays for the chapter—excluding markdown syntax tokens and inline image markers—so word count and reading time never disagree.
- **FR-003**: System MUST display each chapter's estimated reading time in the chapter list.
- **FR-004**: System MUST display the estimated reading time of the chapter open in the editor, next to its live word count.
- **FR-005**: System MUST update a chapter's displayed estimate automatically whenever its prose changes, with no manual refresh or save.
- **FR-006**: System MUST format estimates as follows: zero words → "—"; more than zero but under 30 seconds → "< 1 min"; under 60 minutes → whole minutes rounded to the nearest minute (e.g., "10 min"); 60 minutes or more → hours and minutes (e.g., "1 hr 15 min"), omitting minutes when zero (e.g., "5 hr").

**Settings menu and reading speed (P2)**

- **FR-006a**: System MUST provide an application Settings menu that the writer can open from the app's main navigation.
- **FR-006b**: In this feature the Settings menu MUST contain only the reading speed setting, laid out so more settings can be added later without restructuring the menu. Moving existing preferences (accessible-editor toggle, daily goal, data controls) into Settings is out of scope.

- **FR-007**: System MUST use a default reading speed of 238 words per minute.
- **FR-008**: Users MUST be able to set, in the Settings menu, a custom reading speed as a whole number of words per minute between 100 and 600 inclusive.
- **FR-008a**: System MUST apply and persist a valid reading speed automatically as soon as the writer leaves the field or presses Enter. The Settings menu MUST NOT require a Save button or an explicit confirmation step.
- **FR-009**: System MUST reject reading speeds outside the allowed range or non-numeric values, show a message stating the allowed range, and keep the previous speed.
- **FR-010**: System MUST persist the reading speed preference in the encrypted local database so it survives app restarts, and apply it to all manuscripts. It MUST be listed in the Data Controls inventory and removed by "delete all data".
- **FR-011**: Users MUST be able to restore the default reading speed.
- **FR-012**: System MUST recalculate all displayed estimates immediately when the reading speed changes.

**Manuscript total (P3)**

- **FR-013**: System MUST display a total estimated reading time for the manuscript, calculated from the manuscript's total prose word count using the same speed and formatting rules.
- **FR-014**: System MUST update the manuscript total when chapters are added, edited, reordered, or deleted.

**Cross-cutting**

- **FR-015**: System MUST perform all reading-time calculations on-device, with no network access.
- **FR-016**: System MUST expose each estimate to assistive technology in full words, and MUST NOT announce live estimate changes on every keystroke.

### Key Entities

- **Reading Time Estimate**: A derived, non-stored value for a chapter or a manuscript. Attributes: source word count, reading speed used, estimated duration, display text, and full-word accessible text. Recalculated whenever the word count or reading speed changes.
- **Reading Speed Preference**: The writer's chosen words-per-minute value. Attributes: value (100–600), whether it is the default. Applies to all manuscripts and is stored in the encrypted local database.
- **Chapter** *(existing)*: Supplies the prose word count used for its estimate.
- **Manuscript** *(existing)*: Supplies the total prose word count used for the manuscript estimate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of test cases, a chapter's displayed reading time equals its prose word count divided by the active reading speed, rounded and formatted per the rules above.
- **SC-002**: A writer can find the estimated reading time of any chapter in under 5 seconds from opening the manuscript, without opening the chapter.
- **SC-003**: After an edit changes a chapter's estimate, the new estimate appears within 1 second of the writer pausing typing.
- **SC-004**: In a manuscript of 150,000+ words across 50+ chapters, showing reading-time estimates adds no perceptible delay to typing or to opening the manuscript.
- **SC-005**: After changing the reading speed, every visible estimate reflects the new speed within 1 second, and the setting survives an app restart 100% of the time.
- **SC-006**: Screen-reader users can hear the estimate for any chapter in full words, and are not interrupted by estimate announcements while typing.

## Assumptions

- **Default reading speed**: 238 words per minute, the commonly cited average silent reading speed for adult readers of English fiction and non-fiction. Writers who target other audiences can adjust it (User Story 2).
- **Allowed speed range**: 100–600 words per minute covers slow and early readers through very fast skimmers while blocking typos (e.g., 2380).
- **Settings menu scope**: The new Settings menu holds only reading speed for now (see Clarifications). Existing preferences stay where they are today.
- **One global reading speed**: The speed applies to all manuscripts. Setting a speed per manuscript or per chapter is out of scope.
- **Prose only**: Inline images and markdown formatting add no time to the estimate. Extra time for images, dialogue density, or text complexity is out of scope.
- **Word count reuse**: The feature relies on the existing prose word count from feature 001 (Manuscript Drafting) and adds no new counting rules.
- **Display only**: Reading-time estimates appear in the app UI only. Including them in EPUB/PDF exports is out of scope.
- **Single language**: The default speed assumes English-language prose. Language-specific defaults are out of scope.
