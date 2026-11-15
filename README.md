# Qur’an Reader for Windows

**EXPERIMENTAL PROJECT — UNDER ACTIVE DEVELOPMENT**

This project is an independently developed experimental Windows Qur’an reader. It is not yet a certified or production-ready authoritative Mushaf. Arabic scripture rendering, pagination, content provenance, accessibility and distribution rights remain under active development and review.

Developers, researchers and interested contributors are welcome to fork the project, investigate its architecture, propose improvements and independently advance it.

The project is not affiliated with or endorsed by any publisher or religious institution unless such affiliation is independently established.

## What is here

Native C#/.NET 10 and WinUI 3 source, separated domain logic and SQLite access, and unit/integration test source. The command below runs only the explicitly selected asset-independent test classes. Implemented capabilities include Mushaf-inspired fixed pages, an experimental adaptive continuous book, Tafsir study/popup/drawer presentations, RTL navigation, themes, explicit font scaling, bookmarks and reading anchors.

Historical Windows engineering checks exercised exact source/geometry round trips and native UI Automation. These results do not certify religious correctness, visual acceptance or release readiness. The public source snapshot has its own build/test results in [status](docs/architecture/STATUS.md).

**UI-1.2K remains INCOMPLETE.** Intermittent private-memory gates remain unresolved. An exact 47-pixel scripture discrepancy reproduces when native resources regenerate; stable source/line geometry does not establish identical rasterization. Header-vector reconstruction also changes pixels independently. No production correction, raster tolerance or blanket exception suppression is presented as a solution.

## Build the source without restricted assets

Use Windows 10 build 19041 or later, an x64 development environment, PowerShell and the .NET 10 SDK selected by `global.json`. NuGet restore requires connectivity; ordinary reading is designed to be offline.

```powershell
dotnet restore QuranReader.slnx --locked-mode
dotnet test tests/QuranReader.Tests/QuranReader.Tests.csproj --no-restore --filter "FullyQualifiedName~ReaderTests|FullyQualifiedName~DesktopInteractionTests|FullyQualifiedName~ReadingFitTests|FullyQualifiedName~SurahTransitionLayoutTests"
dotnet build src/QuranReader.App/QuranReader.App.csproj -c Release --no-restore -p:RequirePublisherFont=false
```

The filtered command is not a full-suite pass. The other content-dependent tests require the exact authorized local assets and remain part of the source; no assertions or private acceptance gates were removed. After authorized provisioning, run the same test project without the filter for the full suite.

This is a compile-only build. No publisher database, Tafsir archive, font, artwork, user state or application binaries are supplied. Without the exact locally verified assets, startup fails visibly; do not substitute another text or relax the pins. There is no claim that a clean clone can currently read scripture. See [content and asset rights](CONTENT-AND-ASSET-RIGHTS.md) for prerequisites and the unresolved acquisition/distribution gate.

## Contribute or build independently

Forking, modification, redistribution and commercial derivatives of eligible project-owned code are welcome under the [MIT License](LICENSE); derivatives need not remain open source. Preserve its notice. Third-party packages and sacred/publisher text, Tafsir, fonts and artwork are outside that grant and retain their own rights. See [contributing](CONTRIBUTING.md), [third-party notices](THIRD-PARTY-NOTICES.md), [architecture](docs/architecture/README.md), [security](SECURITY.md) and the [roadmap](docs/ROADMAP.md).

KH-1.0–KH-1.3, BM-1.0 and LAN-1.0 are **PLANNED**, require separate authorization, and are not implemented by this publication.
