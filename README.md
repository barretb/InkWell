# InkWell

An offline-first manuscript drafting app for novelists. Write chapters in a distraction-free
live-rendering markdown editor, organize them into a manuscript, track daily word-count goals, and
keep character and plot-thread notes — with everything stored locally and encrypted at rest.
EPUB/PDF export and local data controls are implemented (see **Status** below).

Feature specification: [`specs/001-manuscript-drafting/`](specs/001-manuscript-drafting/)
Project principles: [`.specify/memory/constitution.md`](.specify/memory/constitution.md)

## Status

This repository is the running demo for a conference talk on spec-driven development with
GitHub Spec Kit, so it is deliberately a real project mid-flight rather than a finished one.

**122 of 132 tasks complete.** Drafting, goals, reference notes, EPUB/PDF export, data controls,
and the accessible editor fallback are implemented. Ten device-dependent verification and QA
tasks remain open, including real screen-reader and platform checks.

The September 25 review fixes bound autosave submission to three seconds during continuous
editing, await in-flight writes during flush/disposal, and make delete-all safe across reopening.
The full solution run passed 475 tests; four external EPUBCheck tests were skipped because the
validator was unavailable. Windows and Android targets built successfully.

[`specs/001-manuscript-drafting/tasks.md`](specs/001-manuscript-drafting/tasks.md) is the
authoritative record — it lists every task with its state and documents each known gap and why.
That file, not this README, is the source of truth for what is and is not built.

## Architecture

Clean architecture; dependencies point inward only.

| Project | Responsibility |
|---|---|
| [`src/InkWell.Domain`](src/InkWell.Domain) | Entities and pure domain services (word counting, daily progress, chapter ordering, goal evaluation). No device dependency. |
| [`src/InkWell.Application`](src/InkWell.Application) | Use cases and the ports (repository/service interfaces) they depend on. |
| [`src/InkWell.Infrastructure`](src/InkWell.Infrastructure) | Adapters: SQLCipher persistence, SecureStorage key handling, Markdig rendering, EPUB/PDF export. |
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
dotnet test tests/InkWell.Infrastructure.Tests   # real keyed SQLite, privacy, use cases end to end
dotnet test tests/InkWell.Maui.UiTests           # story journeys, accessibility, performance
```

```bash
dotnet test tests/InkWell.Application.Tests      # use-case orchestration over in-memory fakes
```

`InkWell.Application.Tests` also holds the fakes the other suites share — `FakeKeyStore`,
`FixedClock`, `InMemorySecureStore`, and the in-memory repositories.

Tests are mandatory for every feature (constitution §II) and are written before the implementation
they cover.

### EPUB validation

Exported books are validated against the W3C's [EPUBCheck](https://github.com/w3c/epubcheck), which
is a Java tool and is not vendored here. Point the tests at it with either:

```bash
export EPUBCHECK_JAR=/path/to/epubcheck.jar     # or drop the jar at tools/epubcheck/epubcheck.jar
export INKWELL_REQUIRE_EPUBCHECK=1              # require EPUBCheck: absence fails validation
```

Without it the EPUBCheck tests are reported as skipped. The structural EPUB assertions in `EpubExporterTests` run either way.

## API documentation

Every project generates an XML documentation file next to its assembly
(`src/<Project>/bin/<Config>/<tfm>/InkWell.<Project>.xml`); public APIs are fully documented and
`GenerateDocumentationFile` is on solution-wide. Point DocFX, Sandcastle, or your IDE at those files
to browse the API surface.

## Help

User-facing guides live in [`docs/help/`](docs/help): the editor, daily goals, export, and what the
app stores about you.

## Editor bundle

The CodeMirror 6 editor is authored in `src/InkWell.Maui/Resources/Raw/wwwroot/` and bundled by the
workspace in [`src/InkWell.Maui/editor-src/`](src/InkWell.Maui/editor-src). See that folder's README.

## Privacy

InkWell requests no network permissions on any platform. All content lives in a single SQLCipher
(AES-256) database whose key is held in platform secure storage. The only outbound path is an export
the writer explicitly triggers to a location they choose.
