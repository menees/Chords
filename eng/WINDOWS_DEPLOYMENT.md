# Portable Windows development builds

Publish from the repository root using the SDK in global.json and the MAUI Windows workload:

```powershell
.\eng\Publish-ChordBook.ps1
# ARM64 Windows, when needed:
.\eng\Publish-ChordBook.ps1 -RuntimeIdentifier win-arm64
```

The script creates a fresh timestamped folder under artifacts. It publishes Release, unpackaged and self-contained, with .NET and Windows App SDK dependencies plus the offline editor and PDF viewer. It refuses an existing output directory to prevent stale binaries. Only x64 output has been built and checked in this milestone; ARM64 still needs hardware acceptance.

## Automatic OneDrive acceptance copy

Each successful publish validates the required assets, then cleans and replaces the contents of Bill's fixed acceptance folder:

```text
C:\Users\Bill\OneDrive\Documents\Music\Creations\ChordBookPrototypes\Maui.Acceptance
```

OneDrive automatically distributes that folder to the other PC. Close ChordBook on both PCs before publishing/replacing binaries, then wait for OneDrive to finish syncing before launching the new build. Mark the complete acceptance folder **Always keep on this device** on the music-room PC. Run `Menees.Chords.Book.Maui.exe` there and create a desktop shortcut to that stable path if useful. WebView2 must be installed. A single copied EXE is insufficient.

The timestamped local artifact remains available. To copy an already validated published artifact again without rebuilding:

```powershell
.\eng\Copy-ChordBookAcceptance.ps1 -SourceDirectory .\artifacts\ChordBook-win-x64-<timestamp>
```

The copy script refuses overlapping source/destination paths and redirected target directories, removes old hidden/stale files, copies every published subdirectory, and verifies file counts/lengths. The copy preflight refuses a running acceptance app or locked destination files before cleanup. A publish failure leaves the acceptance folder untouched. A filesystem error during replacement can leave an incomplete folder; rerun the copy from the retained artifact before launching.

The project declares Windows 10 version 1809/build 17763 or later as its minimum; acceptance on older supported Windows builds is still pending. The destination needs Windows 10/11 and a matching x64/ARM64 build. It also needs the [Microsoft Edge WebView2 Evergreen runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). The .NET SDK, Visual Studio, MAUI workload and Store installation are not required on that PC. This unsigned development build may prompt Windows trust/SmartScreen handling.

Books live separately under `%LOCALAPPDATA%\Menees\ChordBook\Books` by default, or in a folder selected through Open Book. Credentials, per-device settings/checkpoints and WebView2 browser data are local. Browser data uses `%LOCALAPPDATA%\Menees\ChordBook\WebView2`; it is created before any app browser, outside the copied build. Updating the distribution folder does not update or copy those data. Use [OneDrive setup](ONEDRIVE.md) for book synchronization.

## Non-development PC acceptance

Use a disposable book before a personal library:

- Launch from a fully hydrated copied folder with no SDK/Visual Studio installed.
- Add/edit/save/reopen a text song; check undo/redo and preview.
- Import and view a small PDF, then restart the app offline and check both sheets.
- Verify options, icon tooltips, keyboard navigation and toolbar wrapping at the PC's normal DPI/window size.
- Complete the two-PC OneDrive acceptance in ONEDRIVE.md.
- Publish a newer build, wait for the acceptance folder to finish syncing, then launch it and verify the existing local book/settings remain accessible.

The folder publish follows Microsoft's [unpackaged MAUI Windows deployment](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0). Actual music-room launch, Windows trust prompts, touch/Surface layout and live OneDrive acceptance remain manual release evidence.
