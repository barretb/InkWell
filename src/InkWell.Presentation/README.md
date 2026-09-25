# InkWell.Presentation

ViewModels, the services they depend on, and the chapter editor host. A MAUI **class library**, not
part of the app project.

- `ViewModels/` — one per screen (`Library`, `Manuscript`, `Editor`, `Goals`, `Characters`,
  `PlotThreads`, `Export`, `DataControls`) plus their shared base
- `Services/` — `INavigationService`, `IConfirmationService`, `IErrorPresenter`,
  `IFileDestinationPicker`, `IEditorPreferences`, the platform adapters that implement them, and
  `StoreFailure`, which turns an unreachable Keychain or an unreadable database into a message that
  says whether the writer's work survived
- `Controls/` — `IEditorHost` with its two implementations: `EditorHostView`, the `HybridWebView` +
  CodeMirror bridge, and `AccessibleEditorFallbackView`, a native `Editor` showing Markdown source
  for screen-reader users. `AccessibleEditorDocument` holds the fallback's text rules, separately
  from the control, so they can be tested without a running MAUI app.

## Why this is a separate project

A MAUI *application* project cannot be referenced from another MAUI-enabled project: its
single-project asset pipeline re-processes the app icon and splash screen in every consumer and
fails on duplicate output names. Keeping the ViewModels here — in a library with no such assets —
is what lets `tests/InkWell.Maui.UiTests` drive complete user-story journeys through the real
presentation code instead of stopping at the application layer.

## Rules

ViewModels depend only on Application use cases and the interfaces in `Services/` and `Controls/`.
None of them may reference a `Page`, a `Window`, or a `WebView` directly — that is what keeps them
runnable in a test without a device.
