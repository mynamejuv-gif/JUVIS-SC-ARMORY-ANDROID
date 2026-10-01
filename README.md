# JUVIS SC ARMORY — Android

<p align="center"><img src="docs/screenshots/juvis-icon.png" alt="JUVIS SC ARMORY emblem" width="180"></p>

[User guide](docs/USER-GUIDE.md) · [Downloads](https://github.com/mynamejuv-gif/JUVIS-SC-ARMORY/releases) · [Build instructions](#build-on-windows) · [Release history](CHANGELOG.md) · [Contributing](CONTRIBUTING.md)

A native Android companion for Star Citizen, written in C# with .NET for Android. Minimum Android 8 (API 26); ARM64 phones and x64 emulators.

This is a starter implementation of the described Windows workflows. The original Windows source was unavailable, so complete desktop feature parity has not been established.

This source edition is **v0.2.0-beta.1**. It adds the ZERO → HERO post-wipe module while preserving the v0.1.8 catalog, inventory, crafting and vehicle systems. See the [beta release notes](docs/RELEASE-0.2.0-beta.1.md). These local packages are not automatically published to GitHub.

Validation: see the [0.1.3 category audit](docs/CATEGORY-AUDIT-0.1.3.md) for tested coverage, fixes and remaining data gaps.

## Features

- One **Sync all sources** button in More, with progress, cancellation and per-source failure reporting.
- Citizen Starter Guide mission reports, optional sync and offline viewing with separate source attribution.

- Searchable item catalog, item images and downloaded-image cache.
- Weapon ammunition details plus caliber, ammo-type, magazine, ballistic and energy search/filter support.
- Central display-name validation across catalogs, gear, crafting, vehicles and loadouts.
- Commodities, blueprints and crafting hub.
- My Gear states and backup import/export.
- Vehicle loadouts and proposed upgrades.
- Collapsible loadout categories with Advanced View for raw ports.
- Ship weapon performance columns and DPS/range charts from published Wiki data.
- Local equipment quantities by city, station or outpost; transfers between locations.
- Apply a proposed build at a selected location, consume available parts and store removed components.
- Gemini deep-link buttons for suggestions.
- UEX and Star Citizen Wiki synchronization.
- Mobile navigation and dark styling inspired by the Windows app.

<img src="docs/screenshots/bundled-fr76-offline.png" alt="Item details with a bundled offline image" width="320">

## Install on Android

Open [Releases](https://github.com/mynamejuv-gif/JUVIS-SC-ARMORY/releases) and choose a signed APK from the available release assets. For v0.1.8, use the **lightweight update APK** (about 43 MiB) if the large download fails to install; the full-images APK includes 3,730 bundled offline pictures. Transfer the APK to your phone, open it in Files, allow installation from that app if prompted, then tap Install. Export your JUVIS backup before updating. Do not uninstall first. AAB files and source ZIPs are not directly installable.

Read the [illustrated user guide](docs/USER-GUIDE.md) for the first-run walkthrough and everyday tasks.

## Images

The complete v0.1.8 source ZIP includes the partial starter dataset and **3,730 images** in `BundledData/Images`. They are included by the build script by default. Images can also download and cache when online. Keep the large image pack and installable packages as release downloads, outside ordinary Git history. A lightweight checkout may contain only an empty image index.

To include your original image ZIP, run from the repository folder:

```powershell
dotnet run --project tools/Juvis.ImagePack -- "C:\path\BundledData (2).zip" .local-image-pack
./Build-Android.ps1 -ImagePackDirectory .local-image-pack
```

Use an empty output folder for the importer. It preserves image bytes and creates the required index. Set `ANDROID_HOME` and `JAVA_HOME`, or pass the SDK/JDK arguments below. If you already have the full source edition, pass its `BundledData` folder directly with `-ImagePackDirectory`. Do not replace the repository's empty index with a populated index without supplying its images.

To build the lightweight update from the complete source ZIP, make a separate empty image pack and pass it to the build script. This keeps the full source image pack intact:

```powershell
New-Item -ItemType Directory -Force .local-empty-images | Out-Null
[IO.File]::WriteAllText((Join-Path (Get-Location) '.local-empty-images/image-index.json'), '{}')
./Build-Android.ps1 -Configuration Release -ImagePackDirectory .local-empty-images
```

## Build on Windows

Install PowerShell 7 and .NET SDK 10 (tested with 10.0.401). Open PowerShell in this folder. First build, including Android SDK/JDK installation:

```powershell
./Build-Android.ps1 -InstallDependencies `
  -AndroidSdkDirectory "$env:LOCALAPPDATA\Juvis\android-sdk" `
  -JavaSdkDirectory "$env:LOCALAPPDATA\Juvis\jdk"
```

The script accepts Android SDK licenses during dependency installation. Later builds can omit `-InstallDependencies` and use the same SDK/JDK arguments. The script runs the core tests before building. Install the `*-Signed.apk` from `artifacts` on an Android device. AAB bundles are for distribution tooling, not direct installation. Add `-Format aab` to build one.

Run tests alone:

```powershell
dotnet run --project tests/Juvis.Core.Tests.csproj -c Release
```

Packages are development builds. Configure a stable private signing key before distributing updates or publishing to a store. GitHub runners generate temporary debug keys: builds from different runners may not install as updates over one another. Export your in-app backup before changing builds that use different signing keys.

## GitHub

Follow [the upload guide](docs/GITHUB-SETUP.md). The included GitHub Actions workflow builds a lightweight debug APK on pushes to `main`, pull requests, or manual runs. Download it from the completed run's Artifacts section. No repository secrets are required, and the workflow does not publish releases.

## Source layout

| Folder | Contents |
| --- | --- |
| `src/Juvis.Core` | Models, parsers, storage, synchronization and planning |
| `src/Juvis.Android` | Native Android screens and resources |
| `tests` | 82 executable core tests and fixtures |
| `tools/Juvis.ImagePack` | Local image-pack importer |
| `BundledData` | Image index and offline images in the full source edition |
| `docs` | Architecture, verification, device checklist and screenshots |

Historical verification is recorded in [VERIFICATION.md](docs/VERIFICATION.md). The [0.1.8 release notes](docs/RELEASE-0.1.8.md) describe this update and its testing limits. Vehicle recommendations depend on available API data and are not a ship-performance simulator.

This is an unofficial fan companion. Star Citizen content and images belong to their respective owners. Data sources include UEX and Star Citizen Wiki. No third-party image ownership or license is granted by this repository.



## Thanks to our data sources

Thank you to the maintainers and community contributors behind these resources:

- [UEX](https://uexcorp.space/) — item, commodity and vehicle reference data.
- [Star Citizen Wiki](https://starcitizen.tools/) and its [API](https://api.star-citizen.wiki/) — item details, components, blueprints, recipes and available unlock information.
- [Citizen Starter Guide](https://citizen-starter-guide.com/) — community blueprint and mission reports through its [Blueprint Finder](https://citizen-starter-guide.com/star-citizen-blueprint-finder/).

Your work makes JUVIS possible. Source names and update times remain visible so users can check the original information. JUVIS is an independent fan project; these credits do not imply endorsement or ownership of third-party data or images.

## Feedback

Suggestions and bug reports are welcome through [GitHub issues](https://github.com/mynamejuv-gif/JUVIS-SC-ARMORY/issues) or mynamejuv@gmail.com. Include your device, Android version, app version and a screenshot when reporting a problem.

