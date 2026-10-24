# ChordBook application icons

`Source/` contains the editable SVG artwork. `Generated/` contains checked-in
transparent square PNGs and one 15-image Windows ICO. Normal .NET builds consume
the checked-in images; regenerate only when artwork or the renderer changes.

| Pixel sizes | SVG source | Detail |
| --- | --- | --- |
| 48, 60, 64, 72, 80, 96, 128, 256 | `chordbook-full.svg` | Approved MCB lettering, lowered notes, three pages, four coils |
| 30, 32, 36, 40 | `chordbook-standard.svg` | Three-page notebook without lettering, enlarged centered paired notes |
| 16, 20, 24 | `chordbook-micro.svg` | One white page, blue backing, three tabs, centered single eighth note |

The full and standard notebook tiers retain the approved geometry from Bill's
`ChordBook-MCB.svg` and `ChordBook-Main.svg` designs. Their square viewBoxes add
transparent margins without distorting the notebook. The standard tier enlarges
the paired notes by 35% and centers their silhouette on the page. All three tiers use paths,
not external fonts, scripts, media queries, or embedded bitmaps. The MCB letters
are Segoe UI Bold outlines. These are original ChordBook assets, separate from
the Fluent action icons. Keep the white paper, blue backing, and dark notes at
every tier, including on dark desktops.

## Regenerate and verify

Use Windows and PowerShell 7.4 or later. The script uses the SVG/Skia renderer
already restored with the MAUI project. No additional graphics package or
JavaScript toolchain is needed. From the repository root:

```powershell
dotnet restore src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj
pwsh -NoProfile -File eng/Export-ChordBookIcons.ps1
pwsh -NoProfile -File eng/Export-ChordBookIcons.ps1 -Check
```

The script locates Microsoft.Maui.Resizetizer through the project's restored
obj/project.assets.json and loads its managed and architecture-specific native
renderer libraries. The MAUI version is selected in Directory.Packages.props;
after upgrading it, regenerate and review the images. The exporter rasterizes
each tier directly at the target resolution, writes only
changed PNG/ICO bytes, and creates `artifacts/branding/icons.png` with enlarged
vector previews and native-size light/dark previews. `-Check` is read-only and
fails for missing/stale outputs. Review the native-size preview after any edit.
Commit both the SVG changes and regenerated assets.

`Generated/chordbook.ico` stores PNG-compressed RGBA images at 16, 20, 24, 30,
32, 36, 40, 48, 60, 64, 72, 80, 96, 128 and 256 pixels. The project embeds it
in the executable with `ApplicationIcon` and copies it to `Assets/ChordBook.ico`.
`WindowsAppIcon` sets that same file on each created WinUI window. No icon
generation, decoding loop, size switching, or asset enumeration runs in app code.
The publisher validates that the copied ICO exists before acceptance mirroring.

After a Windows build, verify the executable and copied ICO (no app launch):

```powershell
pwsh -File eng/tests/Test-AppIcons.ps1 -Executable src/Menees.Chords.Book.Maui/bin/Release/net10.0-windows10.0.19041.0/win-x64/Menees.Chords.Book.Maui.exe
```

This checks embedded resource bytes against the copied ICO and asks Windows to
load every requested size. It does not establish the appearance of cached pinned
shortcuts or taskbar icons. Check Explorer, title bar, taskbar, Alt+Tab, and light
and dark themes at 100%, 150%, and 200% display scaling on Windows/Surface.
Windows can retain older shortcut icons in its shell cache.

Individual PNGs remain available for future packaging. MSIX manifest assets,
Store artwork, shortcut installation, a tray icon, and other platforms are not
implemented here. Do not add a `MauiIcon` item that automatically resizes one
master over these intentionally different Windows variants.
