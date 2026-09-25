# Quickstart & Validation Guide: Manuscript Drafting

**Feature**: `001-manuscript-drafting` | **Date**: 2026-07-30

This guide proves the feature works end-to-end. It maps each user story and the cross-cutting
requirements to runnable validation scenarios. Details of entities and operations live in
[data-model.md](./data-model.md) and [contracts/](./contracts/); decisions in [research.md](./research.md).

## Prerequisites

- **.NET 10 SDK** with the MAUI workload: `dotnet workload install maui`
- Platform toolchains for the target(s) you validate: Windows (Windows App SDK), macOS/iOS (Xcode +
  Mac Catalyst), Android (Android SDK + emulator/device)
- Node/npm only if rebuilding the CodeMirror editor bundle (prebuilt assets ship in `Resources/Raw/wwwroot`)
- iOS/Mac Catalyst: `Entitlements.plist` with Keychain Sharing enabled (required for `SecureStorage`)

## Build & run

```bash
# from repo root
dotnet restore
dotnet build src/InkWell.Maui/InkWell.Maui.csproj

# run (pick a framework)
dotnet build -t:Run -f net10.0-windows10.0.19041.0 src/InkWell.Maui/InkWell.Maui.csproj
# or net10.0-maccatalyst / net10.0-ios / net10.0-android
```

## Run the tests

```bash
dotnet test tests/InkWell.Domain.Tests           # word count, daily rollover, goal met/exceeded, ordering
dotnet test tests/InkWell.Application.Tests       # use cases against faked repositories/clock/keystore
dotnet test tests/InkWell.Infrastructure.Tests    # real keyed SQLite round-trips; EPUB (EPUBCheck) + PDF export
dotnet test tests/InkWell.Maui.UiTests            # per-story + keyboard-only / accessibility checks
```

Expected: all green. Domain tests need no device; infrastructure tests create a temporary keyed
SQLite DB and delete it afterward.

---

## Validation scenarios

Each scenario is the spec's *Independent Test* for a story, plus the cross-cutting mandates. Run them
manually in the app (or via the referenced automated test) and confirm the **expected outcome**.

### US1 — Draft & organize a manuscript (P1) · [contract](./contracts/manuscript-service.md)

1. With no manuscripts, create one and name it → it appears in the library and opens.
2. Add three chapters; type prose in each → text is saved automatically (no save button; FR-004).
3. Reorder the chapters → new order shown.
4. Close and reopen the app → all three chapters, their text, and the new order are intact.
5. Delete a chapter → a confirmation is required; it is removed only after confirming (FR-005).
6. Disable networking entirely and repeat 1–5 → everything succeeds identically (FR-006, SC-002).

**Expected**: content and ordering persist across restart; autosave loses nothing; all offline.
*Automated by*: `InkWell.Infrastructure.Tests` persistence round-trip + `Maui.UiTests` US1.

### US2 — Distraction-free writing (P2) · [contract](./contracts/chapter-editor-bridge.md)

1. Open a chapter; activate distraction-free mode via the visible control → navigation/panels/toolbars
   hide; the editing area fills the space.
2. Type → editing behaves identically to normal mode and autosaves (US2 scenario 2).
3. Exit via keyboard shortcut → full interface returns; cursor and content unchanged, in <1 s (SC-006).
4. Repeat entering/exiting via keyboard only.

**Expected**: chrome toggles cleanly; cursor/content preserved; both control and shortcut work.

### US3 — Daily word-count goals (P2) · [contract](./contracts/word-count-and-goals.md)

1. Set a daily goal of 500 words → tracking begins.
2. Write 200 words → progress shows 200/500 (40%), remaining 300, status *In progress* (text label,
   not color alone; FR-019).
3. Write 300 more → status *Met* for the day.
4. Advance the calendar day (or wait past local midnight) and reopen → today resets to 0/500, the target
   persists, and yesterday's result is retained in history (FR-012, US3 scenario 4).
5. Confirm the live word count excludes markdown syntax and image markers (SC-005) — insert an image and
   some `**bold**`; the count reflects prose words only.

**Expected**: progress math and met/exceeded are correct; rollover attributes post-midnight words to the
new day. *Automated by*: `InkWell.Domain.Tests` (counting + rollover) and `Maui.UiTests` US3.

### US4 — Characters & plot threads (P3) · [contract](./contracts/reference-service.md)

1. Create a character with name + notes → listed for the manuscript.
2. Create a plot thread with title + notes → listed for the manuscript.
3. While drafting, open the character/plot-thread reference → view it without losing your place; close
   returns to the exact caret (FR-015).
4. Edit one and delete another (delete requires confirmation) → changes persist; deleting a referenced
   entry does not corrupt the manuscript (edge case).
5. Close and reopen → both are retained and viewable.

**Expected**: reference data persists and is isolated from manuscript integrity.

### Cross-cutting — Export, privacy, accessibility · [contract](./contracts/export-service.md)

1. **Export** a manuscript to EPUB and to PDF, then a single chapter to each → four files at the chosen
   location; every inline image appears embedded (SC-009). EPUB passes EPUBCheck.
2. **Privacy**: with a network monitor attached, perform all above operations → no outbound traffic
   except the user-chosen file writes (FR-017, SC-002). Inspect the raw DB file → no plaintext prose
   (encrypted at rest, FR-016).
3. **Data controls**: view all stored data; delete a manuscript (confirmed); delete all app data
   (confirmed) → no residual user content remains (SC-008).
4. **Accessibility**: complete US1–US4 end-to-end using **keyboard only**; run screen-reader
   (Narrator/VoiceOver/TalkBack) and contrast checks on all new UI → WCAG 2.1 AA passes; no status is
   conveyed by color alone (FR-019, SC-007).

### Performance (SC-004, SC-006)

- Load a seeded 150,000-word / 50+-chapter manuscript → opening a chapter and typing stay responsive,
  keystroke feedback perceived as instantaneous; distraction-free enter/exit <1 s. *Automated by*:
  `Maui.UiTests` performance scenario against a generated large manuscript.

---

---

## Status — 2026-09-02

**Automated suite: 458 tests, 0 failing** (`dotnet test InkWell.slnx`) — 90 Domain, 60 Application,
148 Infrastructure, 160 UI/story. Every scenario above that a machine can decide is covered by one
of them; the rest are listed as outstanding rather than assumed.

### Verified automatically

| Scenario | Covered by |
|---|---|
| US1 steps 1–5 | `Maui.UiTests/UserStory1Tests.cs`, `Infrastructure.Tests/Persistence/ManuscriptRepositoryTests.cs` |
| US1 keyboard-only completion, including reorder | `Maui.UiTests/Accessibility/UserStory1AccessibilityTests.cs` |
| US2 all steps | `Maui.UiTests/UserStory2Tests.cs`, `DistractionFreeBridgeTests.cs`, `Performance/DistractionFreePerformanceTests.cs` |
| US3 all steps, including day rollover past local midnight | `Maui.UiTests/UserStory3Tests.cs`, `Domain.Tests/Services/DailyProgressCalculatorTests.cs` |
| US4 all steps | `Maui.UiTests/UserStory4Tests.cs`, `Infrastructure.Tests/Persistence/ReferenceRepositoryTests.cs` |
| Export: EPUB + PDF, whole manuscript and single chapter, images embedded | `Infrastructure.Tests/Export/EpubExporterTests.cs`, `PdfExporterTests.cs` |
| **EPUBCheck clean** | `Infrastructure.Tests/Export/EpubCheckTests.cs` — verified against EPUBCheck 5.3.0 with `--failonwarnings`, zero errors (research.md §5.7) |
| No plaintext in the database, no network permission on any platform | `Infrastructure.Tests/Privacy/DraftingPrivacyTests.cs`, `PlatformPrivacyManifestTests.cs`, `ExportPrivacyTests.cs` |
| Data controls: view all, delete one, delete everything with nothing recoverable | `Infrastructure.Tests/Persistence/DataControlsTests.cs`, `Maui.UiTests/ExportAndDataControlsTests.cs` |
| WCAG 2.1 AA contrast, computed over the shipping palette | `Maui.UiTests/Accessibility/UserStory1AccessibilityTests.cs` |
| No status conveyed by colour alone | The accessibility tests for US1–US4 |
| SC-004 at 150,104 words across 52 chapters | `Maui.UiTests/Performance/LargeManuscriptPerformanceTests.cs` |
| No blocking I/O on the UI thread | `Maui.UiTests/Performance/UiThreadDisciplineTests.cs` |

Measured on the desktop target: 0.009 ms per keystroke against a 16 ms frame budget, a chapter
opening from a 150,000-word manuscript in under a millisecond, and an autosave commit in under a
millisecond.

### Outstanding — needs a device or a person

These cannot be settled on a Windows build host and are **not** claimed as passing:

- **US1 step 6 and the privacy network monitor** — running the app with networking disabled and
  watching for egress. The manifest-level assertion (the app holds no network permission on any
  platform) is automated and is the stronger guarantee, but the observed run has not been done.
- **Screen-reader verification** — Narrator, VoiceOver, and TalkBack against the editor's
  `contenteditable` surface and the native accessibility-mode fallback. This is the risk
  research.md §1 names as the highest, and it is the reason the fallback exists. Tasks T071, T082,
  T096, T106, T130.
- **The mobile half of the performance criterion** — SC-004 asks for at least one desktop and one
  mobile target; only desktop has been measured.
- **SQLCipher and `HybridWebView` on Apple and Android** — tasks T012 and T013, still open
  (research.md §5.1, §5.2).
- **PDFsharp font rendering on a real iOS and Android device** — task T107. The resolver is verified
  on Windows and the font program is genuinely embedded in the output (research.md §5.8); whether
  the device rasterisers accept it is untested.
- **Cross-platform WebView QA** — `contenteditable`, IME, and paste across WebView2, WKWebView, and
  Android WebView. Task T129.

## Definition of done for this feature

- [X] All four validation scenarios (US1–US4) pass via automated tests. *Manual passes on a device
  remain outstanding, as listed above.*
- [X] Cross-cutting export/privacy scenarios pass; **EPUBCheck clean**; no plaintext in DB.
- [ ] Accessibility: contrast and keyboard-only completion are verified automatically; screen-reader
  verification on a device is outstanding.
- [ ] Performance scenario meets SC-004/SC-006 on at least one desktop **and one mobile** target —
  desktop done, mobile outstanding.
- [X] Constitution gates in [plan.md](./plan.md) remain satisfied.
