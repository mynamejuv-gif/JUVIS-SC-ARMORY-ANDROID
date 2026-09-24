# Android 0.1.8 — release and verification

This update extends the existing native C# Android app. The app identifier stays `app.juvis.scarmory`, versionCode advances from 8 to 9, and the supplied APK is signed with the same development certificate as the supplied 0.1.7 APK. Export a backup, then open the 0.1.8 APK to update in place without uninstalling.

The **lightweight update APK** provides the same app features and is about 43 MiB. It omits the 3,730 bundled offline images, so uncached images load online when available. The full-images APK and AAB include the offline image pack and are about 780 MiB and 776 MiB respectively. Use the lightweight APK if a large download gives Android a parsing error.

## What changed

- Local inventory with named locations, quantities, transfers, recent activity and catalog item entry.
- Proposed vehicle builds can be recorded as applied. Available replacement parts are consumed at the chosen location; removed fitted parts are stored there. A missing replacement part requires an explicit outside-supply acknowledgment. The fitted state and inventory are saved together, and repeat application cannot duplicate the removed parts.
- Six collapsible loadout groups. Normal view hides fixed, internal and uncertain ports; Advanced View shows all raw ports for troubleshooting.
- Ship gun details show source-backed performance. The upgrade screen compares current and candidate components in horizontally scrolling columns, with DPS and range charts for confirmed compatible weapons.
- Refreshed bundled Wiki ship gun performance records for version 4.10.0-LIVE.12519617. No missing combat values are estimated.
- Existing gear flags, search, ammunition details, source sync, Gemini buttons, craft data, images and schema-v1 backups remain supported. Backups now also carry inventory and fitted loadout state.

## Verification

- .NET 10 Android Release APK and AAB compiled with no warnings or errors.
- 82 core tests passed. They cover compatibility, grouped ports, performance parsing, source accuracy, inventory transfers, applied builds, repeated imports and persistence after app relaunch.
- Android 16 x64 emulator accepted the update APK over v0.1.7 without uninstall. A saved Owned/Favorite item and its new location inventory remained available after the update.
- The signed AAB passed signature verification. A universal APK generated from it also installed over the saved emulator app, launched successfully and retained items at both Area18 and Orison.
- The 44,763,488-byte lightweight APK passed APK signature verification, installed over that same saved app and launched with the catalog, My Gear entry and both location inventories intact.
- Native phone UI exercised Catalog, My Gear, Inventory, grouped loadouts, confirmed candidates and applying a proposed build. The missing-part confirmation was enforced, and the replaced STOP shield appeared at Area18 while the fitted slot showed 6MA 'Kozane'. Inventory also remained visible at tablet size without clipping. Phone and tablet screenshots are included in the user guide.

## Known limits

- Inventory and fitted equipment are manual records in JUVIS. The app cannot read Star Citizen account or in-game storage state.
- Locations are user-defined labels. The app does not determine an item's physical location automatically.
- The tracked fitted state is per vehicle model ID. It does not distinguish two owned copies of the same model.
- Replacing a mount with nested equipment stores the removed child components, but the new mount's child slot layout remains unavailable until verified from source data.
- The Wiki does not publish every performance field. Effective range is often unknown even when maximum projectile range is present. Missing values display as unavailable.
- An existing installation retains its catalog cache. Use Sync all sources or item refresh for newer Wiki performance records.
- The emulator test does not prove performance or fit on every physical phone/tablet. This is a development-signed build, not a store submission.

Build commands and image-pack instructions are in the root README. The [0.1.8 update guide](UPDATE-0.1.8.md) explains installation and daily use.
