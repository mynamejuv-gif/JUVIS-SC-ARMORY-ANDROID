# Release history

## 0.2.0-beta.1 · 1 October 2026

- Added ZERO → HERO as a first-class navigation section with a seven-stage post-wipe roadmap.
- Added a run-scoped resource ledger with KEEP, SELL and PRIORITY KEEP decisions, contract requirements, blueprint-derived totals, owned quantities and missing calculations.
- Added mining, salvage and base-building preparation checklists with explicit LIVE 4.10.1, PTU 4.10.2 and OUTDATED / VERIFY labels.
- Connected the module to existing blueprint craft plans, owned vehicles, proposed builds and tracked fitted loadouts rather than duplicating those systems.
- Added a New Wipe Run action that resets only ZERO → HERO progress. Catalog knowledge, normal inventory, gear, blueprints, vehicles and saved builds remain intact.
- Preserved schema-v1 backup compatibility while adding ZERO → HERO progress to new exports and merges.
- Preserved package ID `app.juvis.scarmory`; versionCode is 12. Release builds now force a clean Android `Rebuild` to avoid stale native bridge packaging.
- 87 core tests pass, including reset isolation, legacy-backup migration, backup round trips, patch validity and missing-resource calculations.

## 0.1.8 · 23 September 2026

- Added local inventory with named storage locations, quantities, transfers and recent activity, accessible from More and My Gear.
- Applying a proposed vehicle build now consumes available parts at the selected location, stores removed equipment there and remembers fitted components. Parts supplied outside tracked inventory require explicit confirmation. Planning alone changes no inventory.
- Added Weapons, Avionics, Liveries, Propulsion, Utility and Misc. loadout groups, collapsed by default. Fixed/internal/unknown ports are hidden unless Advanced View is enabled.
- Added ship weapon performance details, horizontally scrollable comparison columns and DPS/range charts. Missing statistics remain unknown; published maximum range is never presented as effective range.
- Refreshed the bundled ship weapon performance snapshot: 172 records with some performance data, using Wiki version 4.10.0-LIVE.12519617. Existing installations retain their cache and can refresh it normally.
- Preserved v0.1.7 ammunition search, Gemini, source sync, images, owned/need/favorite states and schema-v1 backup import. Backups now also include inventory locations, quantities and tracked loadouts; repeated imports do not add quantities twice.
- Same app ID, versionCode 9, development signing certificate compatible with the supplied v0.1.7 APK. 82 core tests pass.

## 0.1.7 · 15 September 2026

- Added centralized display-name validation and fallback resolution across catalog, gear, crafting, vehicles, stock loadouts, compatible upgrades and search. Unresolved records are hidden while saved IDs and backup data remain untouched.
- Added structured ammunition data for personal and vehicle weapons: ammo class/type, caliber where the source states it, magazines, capacity, compatible magazines and energy-capacitor data.
- Added ammunition-aware catalog search and Ballistic, Energy, Magazine-fed and unavailable-data filters.
- Added a dedicated Wiki weapon/magazine sync that preserves prior cache data if any required source segment fails.
- Refreshed the bundled starter snapshot to 1,211 cleanly named items, including 586 weapon-category entries. All 524 ammunition-applicable entries have reliable ammunition data; 62 melee, throwable, gadget or utility entries are marked not applicable.
- Preserved schema-v1 backups, app ID `app.juvis.scarmory`, ARM64/x64 support and the existing strict slot/tag/patch compatibility rules. Version is now 0.1.7 (`versionCode` 8).

## 0.1.6

- Replace six More-page sync buttons with one Sync all sources action.
- Add source progress, cancellation, retained cache on failure and a completion report.
- Keep individual item and loadout refresh controls; update the user guide.

## 0.1.5 — community source

- Citizen Starter Guide sync, offline mission reports, source dates and website links.
- Exact variant matching and clearer checked/unchecked Wiki mission status.
- 51 tests passed; live source sync and Android offline viewing verified.

## 0.1.4 — network refresh fix

- Fixed network response reading/disposal on Android's UI thread for API requests and downloaded images.
- Clarified missing Wiki records without misleading UEX-token advice.
- Reproduced the ADP Arms failure and verified a successful FR-76 refresh; 43 core tests passed.

## 0.1.3 — private category review

- Audited every populated item category, blueprints, commodities and vehicles against a fresh five-source sync.
- Readable incomplete blueprint labels with an optional source-record filter.
- Consistent component category labels and preserved save identities after Wiki detail refresh.
- Prevented editing of vehicle variants with conflicting source IDs; existing saves remain exportable.
- Forty passing core tests; debug APK and AAB builds. See [the audit report](docs/CATEGORY-AUDIT-0.1.3.md) for remaining data gaps.

## 0.1.2

- New JUVIS star-and-laurel adaptive Android launcher icon.
- Illustrated user guide, installation instructions and source build documentation.
- Lightweight GitHub source edition with an optional external image pack.
- GitHub Actions workflow for core tests and a development APK.
- Local core tests: 35 passed; local Android builds succeeded without warnings or errors. The new icon and app launch were checked on an Android emulator.

## 0.1.1

- Bundled 3,730 original images for offline use: 3,655 item images and 75 commodity images.
- Image-pack importer, UUID/commodity image mapping and cache behavior checks.
- Full-image APK, development AAB and source package.

## 0.1.0

- Initial native C# Android implementation of the described Windows workflows.
- Searchable equipment, commodities, blueprints, crafting plans and My Gear.
- Vehicle stock loadouts, compatibility checks and proposed builds.
- Gemini research prompts, UEX/Wiki synchronization and Android backup import/export.

These are development releases of an unofficial companion. Desktop feature parity and Windows backup migration remain incomplete.
