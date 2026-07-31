# InkWell.Presentation

ViewModels, the services they depend on, and the chapter editor host. A MAUI **class library**, not
part of the app project.

- `ViewModels/` — `LibraryViewModel`, `ManuscriptViewModel`, `EditorViewModel`, and their shared base
- `Services/` — `INavigationService`, `IConfirmationService`, `IErrorPresenter`, and the platform
  storage adapters (`SecureStorage`, app data paths) behind their Application-layer ports
- `Controls/` — `IEditorHost` and `EditorHostView`, the `HybridWebView` + CodeMirror bridge

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
