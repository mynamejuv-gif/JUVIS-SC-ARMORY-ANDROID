# Test the inline vehicle upgrade selector

This is the **0.1.9-beta.2** test build. It replaces beta.1, which crashed at launch. The published 0.1.8 release remains the stable download.

1. In JUVIS, open **More → Export backup** and save a copy of your personal data.
2. On your Android device, open the [0.1.9-beta.2 prerelease](https://github.com/mynamejuv-gif/JUVIS-SC-ARMORY-ANDROID/releases/tag/v0.1.9-beta.2) and download the **lightweight beta APK** under Assets. Open it in Files/Downloads and choose **Update**. Do not uninstall the existing app first, including if beta.1 currently crashes.
3. Open **Vehicles**, choose a ship, and refresh its stock loadout if needed. Expand a loadout category.
4. Check that each editable slot shows **Stock / fitted** on the left and **Upgrade** on the right. Tap the upgrade control, search, and choose a compatible component.
5. Confirm that the stock-versus-selected comparison appears directly below that slot. Try another component; the comparison should update. Choose **Keep stock**; the comparison should disappear.
6. Open **Proposed build** to confirm the selected part is saved. Check that your My Gear and local inventory remain present. Applying a build is optional and changes local tracking only, not the game.

The selector lists only components whose fit can be confirmed from cached type, size, subtype, mount-tag, and patch data. If it has no choices, use **Detailed comparison / sync candidates**. Unknown values remain unknown. The lightweight APK downloads images as needed; it does not bundle the full offline image pack.

Please report the ship, slot, component, and what happened in a [GitHub issue](https://github.com/mynamejuv-gif/JUVIS-SC-ARMORY-ANDROID/issues) if something looks wrong. A screenshot helps, but avoid including private backup data.
