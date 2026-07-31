# InkWell.Maui

The MAUI presentation host: Views, ViewModels (CommunityToolkit.Mvvm), and the CodeMirror 6 editor.

- `Views/` and `ViewModels/` — library, manuscript shell, editor, goals, characters, plot threads,
  data controls
- `Controls/` — the `HybridWebView` editor host, its JS↔C# bridge, and the native `Editor`
  accessibility-mode fallback
- `Services/` — navigation, confirmation dialogs, and error presentation (the abstractions that keep
  ViewModels testable headlessly)
- `Resources/Raw/wwwroot/` — the editor page and its bundle; built by [`editor-src/`](editor-src)
- `Resources/Styles/` — WCAG 2.1 AA contrast tokens, focus visuals, and text-bearing status styles

ViewModels depend only on interfaces, never on a `Page`, `Window`, or `WebView`, so
`tests/InkWell.Maui.UiTests` can drive complete story journeys headlessly.
