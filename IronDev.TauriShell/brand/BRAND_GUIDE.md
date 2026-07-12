# IronDev Brand Guide

## Identity

IronDev is the product name. The compact descriptor is "Governed engineering". Product-facing UI, installers, executable metadata, diagnostics, and support information must not use "TauriShell" or "Shell Spike".

The stable desktop bundle identifier is `com.irondeveloper.irondev`. Changing it creates a new application identity and requires an explicit migration plan.

## Mark

The mark is a vertical iron bar held between opposing code brackets. The small amber gap is the controlled passage: software may move through the gate, but the gate remains visible.

- Graphite: `#20252b`
- Forge amber: `#d28a2c`
- Minimum digital size: 16 by 16 pixels
- Clear space: at least one quarter of the mark width
- Do not add letters, shadows, gradients, robot imagery, or security badges.

Use `irondev-mark.svg` as the canonical source. Monochrome and high-contrast variants are provided for surfaces where the primary mark is not legible. Generated PNG, ICO, and ICNS files must be regenerated from the canonical source rather than edited by hand.

## Lockups

Use the horizontal lockup where the descriptor helps establish the product, such as sign-in or About. Use the mark and product name alone in the compact application header. The stacked lockup is for square product-information surfaces, not routine navigation.

## Version

The desktop application version is independent from product roadmap labels. Keep the application version synchronized across npm, Cargo, Tauri configuration, About, diagnostics, and packaged installers.

## Distribution Status

The current local Windows MSI and NSIS packages are unsigned. Code signing, publisher identity, upgrade-channel proof, uninstall behavior, user-data migration, and release notes belong to `DESKTOP-DISTRIBUTION-1`; unsigned installation is a known distribution limitation, not a hidden success claim.
