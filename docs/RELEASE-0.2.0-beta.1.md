# JUVIS SC ARMORY Android 0.2.0-beta.1

## ZERO → HERO

This beta adds a post-wipe companion without replacing the existing Armory workflows.

- Seven-stage progression roadmap from first essentials through fleet recovery and base preparation.
- Run-scoped resource inventory with KEEP, SELL and PRIORITY KEEP decisions.
- Exact, user-entered contract requirements and automatic missing-resource totals.
- Blueprint dependencies pulled from the existing craft plan and cached recipe database.
- Mining and salvage guidance plus a base-building preparation checklist.
- Ship rebuild queue linked to existing owned vehicles, proposed builds and tracked fitted components.
- Patch validity labels: LIVE 4.10.1, PTU 4.10.2, or OUTDATED / VERIFY.
- New Wipe Run resets only this module's run progress. It does not erase the knowledge database, catalog, normal inventory, gear, owned blueprints, ships or saved loadouts.

## Data and update safety

The application ID remains `app.juvis.scarmory` and the versionCode is 12. Backups remain schema v1 and older backups can still be imported. New exports include ZERO → HERO progress.

Install this as an update. Do not uninstall the existing app or clear its data. The APK must be signed with the same certificate as the installed release for Android to accept an in-place update.

## Verification target

Run the 87 core tests, force a clean Android Rebuild, install the exact signed candidate with `adb install -r --no-incremental`, wait for the activity to be displayed, exercise ZERO → HERO and the existing Catalog, Craft, Vehicles, My Gear and More sections, then inspect logcat for fatal exceptions and native bridge errors.
