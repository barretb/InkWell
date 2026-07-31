# InkWell editor build workspace

The chapter editor is CodeMirror 6 running inside MAUI's `HybridWebView` (research.md §1). This
folder holds only the npm dependency graph and the bundler; the editor itself is authored in
`../Resources/Raw/wwwroot/`:

| File | Role |
|---|---|
| `index.html` | The page `HybridWebView` loads; hosts the editor root and the a11y live region |
| `editor.js` | Entry point — CodeMirror setup and the whole JS↔C# bridge |
| `live-preview.js` | The Obsidian-style live-preview decoration layer |
| `styles.css` | Editor theme, including the distraction-free layout |
| `editor.bundle.js` | **Generated** — the only script `index.html` loads. Do not edit. |

## Build

```bash
cd src/InkWell.Maui/editor-src
npm install
npm run build      # writes ../Resources/Raw/wwwroot/editor.bundle.js
npm run watch      # rebuild on change while working on the editor
```

`editor.bundle.js` is committed so that a plain `dotnet build` of the app never requires Node.
Re-run `npm run build` and commit the result whenever `editor.js`, `live-preview.js`, or a
CodeMirror dependency changes.
