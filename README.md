# InkWell

An offline-first manuscript drafting app for novelists. Write chapters in a distraction-free
live-rendering markdown editor, organize them into a manuscript, track daily word-count goals, and
keep character and plot-thread notes — with everything stored locally, encrypted at rest, and
nothing leaving the device except through a user-initiated EPUB/PDF export.

Feature specification: [`specs/001-manuscript-drafting/`](specs/001-manuscript-drafting/)
Project principles: [`.specify/memory/constitution.md`](.specify/memory/constitution.md)

## Architecture

Clean architecture; dependencies point inward only.

| Project | Responsibility |
|---|---|
| [`src/InkWell.Domain`](src/InkWell.Domain) | Entities and pure domain services (word counting, daily progress, chapter ordering, goal evaluation). No device dependency. |
| [`src/InkWell.Application`](src/InkWell.Application) | Use cases and the ports (repository/service interfaces) they depend on. |
| [`src/InkWell.Infrastructure`](src/InkWell.Infrastructure) | Adapters: SQLCipher persistence, SecureStorage key handling, Markdig, EPUB/PDF export. |
| [`src/InkWell.Presentation`](src/InkWell.Presentation) | ViewModels, navigation/confirmation/error services, and the `HybridWebView` editor host. A MAUI class library, so tests can drive it. |
| [`src/InkWell.Maui`](src/InkWell.Maui) | The app host: Views, the Shell, and the composition root. |

## Requirements

- [.NET 10 SDK](https://dot.net) with the MAUI workload: `dotnet workload install maui`
- Node.js 20+ and npm — only to rebuild the editor bundle (the built bundle is committed)
- Platform toolchains for whichever targets you build: Windows App SDK, Xcode (iOS / Mac Catalyst),
  Android SDK

## Build and run

```bash
dotnet restore
dotnet build src/InkWell.Maui/InkWell.Maui.csproj -f net10.0-windows10.0.19041.0

# run
dotnet build -t:Run -f net10.0-windows10.0.19041.0 src/InkWell.Maui/InkWell.Maui.csproj
```

The `net10.0-ios` and `net10.0-maccatalyst` targets require a macOS build host, so they are excluded
on Windows and Linux. Opt in explicitly with `-p:EnableAppleTargets=true` when building on (or
paired to) a Mac.

## Test

```bash
dotnet test tests/InkWell.Domain.Tests           # word counting, daily rollover, goals, ordering
dotnet test tests/InkWell.Application.Tests      # use cases against faked ports
dotnet test tests/InkWell.Infrastructure.Tests   # real keyed SQLite, privacy, EPUB/PDF export
dotnet test tests/InkWell.Maui.UiTests           # story journeys, accessibility, performance
```

Tests are mandatory for every feature (constitution §II) and are written before the implementation
they cover.

## Editor bundle

The CodeMirror 6 editor is authored in `src/InkWell.Maui/Resources/Raw/wwwroot/` and bundled by the
workspace in [`src/InkWell.Maui/editor-src/`](src/InkWell.Maui/editor-src). See that folder's README.

## Privacy

InkWell requests no network permissions on any platform. All content lives in a single SQLCipher
(AES-256) database whose key is held in platform secure storage. The only outbound path is an export
the writer explicitly triggers to a location they choose.
