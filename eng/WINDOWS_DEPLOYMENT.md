# Portable Windows development builds

Publish from the repository root using the SDK in global.json and the MAUI Windows workload:

```powershell
.\eng\Publish-ChordBook.ps1
# ARM64 Windows, when needed:
.\eng\Publish-ChordBook.ps1 -RuntimeIdentifier win-arm64
```

The script creates a fresh timestamped folder under artifacts. It publishes Release, unpackaged and self-contained, with .NET and Windows App SDK dependencies plus the offline editor and PDF viewer. It refuses an existing output directory to prevent stale binaries. Only x64 output has been built and checked in this milestone; ARM64 still needs hardware acceptance.

## Published resource languages

English is the initial supported UI language. The Windows project declares `DefaultLanguage` and managed `NeutralLanguage` as `en-US`; `SupportedResourceLanguages` currently contains `en-US;en-GB`. The same list filters managed satellite assemblies and native Windows App SDK MUI payloads before files are copied. This applies to direct project publishes as well as Publish-ChordBook.ps1. Regional number/date formatting and Unicode song content are unchanged.

For a future translated release, update the project's supported list and add/validate the application's translations; shipping WinUI resources alone does not translate ChordBook. Keep the default language in the list. The publisher validates that only listed satellite/MUI languages are present and that each selected language has both required WinUI MUI files before mirroring. Resource folders remain next to their native DLLs. The initial reduction removes 84 unsupported MUI folders (168 files); the first mirror therefore performs these one-time stale-file deletions, and later builds retain unchanged English resources.

Published releases omit RestartAgent.exe (Windows App SDK restart helper), NpuDetect/NPUDetect.dll (unused AI hardware detection), and createdump.exe (.NET dump-generation helper). The project applies these exclusions to direct Windows publishes too, and the distribution script refuses unexpected copies before mirroring. ChordBook has no dependency on the SDK restart or AI APIs; Windows Task Manager remains available for manually capturing a process dump.

The app distribution also excludes the SDK package logo WindowsAppRuntime.png, unused AI workloads*.json manifests, and generated API documentation XML. Documentation generation and XML in reusable library/NuGet outputs remain enabled; this is an app-publish exclusion. ONEDRIVE.md, RECOVERY.md and WINDOWS_DEPLOYMENT.md remain included as requested.

## Automatic OneDrive acceptance copy

Each successful publish validates the required assets, then incrementally mirrors its contents into Bill's fixed acceptance folder:

```text
C:\Users\Bill\OneDrive\Documents\Music\Creations\ChordBookPrototypes\Maui.Acceptance
```

OneDrive automatically distributes that folder to the other PC. Close ChordBook on both PCs before publishing/replacing binaries, then wait for OneDrive to finish syncing before launching the new build. Mark the complete acceptance folder **Always keep on this device** on the music-room PC. Run `Menees.Chords.Book.Maui.exe` there and create a desktop shortcut to that stable path if useful. WebView2 must be installed. A single copied EXE is insufficient.

The timestamped local artifact remains available. To copy an already validated published artifact again without rebuilding:

```powershell
.\eng\Copy-ChordBookAcceptance.ps1 -SourceDirectory .\artifacts\ChordBook-win-x64-<timestamp>
```

The copy script compares file sizes and SHA-256 content, retains byte-identical files without changing their timestamps, copies only added/changed files, and removes only stale files and directories (including hidden entries). This avoids rewriting unchanged dependencies when a publish changes their timestamps. Comparison reads local bytes; online-only OneDrive files may need hydration. It reports added/updated, removed and unchanged counts, then verifies file counts/lengths. Preflight refuses overlapping paths, symbolic links/junctions, file/directory conflicts, a running acceptance app or locked destination files before changing contents. A publish failure leaves the acceptance folder untouched. A filesystem error during mirroring can leave an incomplete folder; rerun the copy from the retained artifact before launching.

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

## Open chord files from File Explorer

In ChordBook, choose **Options > File Associations**. This registers the currently running executable for `.cho`, `.chopro`, `.chordpro` and `.crd` under your Windows user account and opens **Windows Default apps**. Choose **Menees ChordBook** for the desired extensions. You can also use Explorer's **Open with** after registration. Windows retains control of the default; ChordBook does not replace another app's default choice. The supported [Windows default-app settings link](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings) opens the app's registration where supported, or the general Default apps page on older Windows builds.

Run setup separately on each PC from the stable Maui.Acceptance folder. Registration stores that executable's full path; repeat setup after moving the distribution. The current unpackaged build uses per-user registration, without administrator access. Store/MSIX declarations will be handled during release packaging.

Double-clicking a file opens the current book's hash-identical sheet without importing it again. Multiple matching songs require a choice. Otherwise a new song imports automatically; related title/artist matches with different content open Review Import with **Skip** selected. **Compare Contents**, **Import as Separate Song** and an explicitly chosen **Add as Sheet** remain available. External source bytes are retained. After a reviewed import, the imported song opens.

A cold launch uses your last active book. Later opens reuse the already-running window at that executable location. Incoming files wait while an editor or another protected operation is open; the title bar indicates a waiting file. Finish/leave the edit or unlock performance to continue. Matching a previous-save/recovery/archived sheet displays that version temporarily and retains normal song/default-sheet settings.

## Find a problematic sheet

Choose **Options > Validate Book** to check the current book and its referenced sheets. **Validate Again** rereads current contents; it does not reuse startup warnings. Reports identify song/artist, sheet type (current, previous save, recovery or archived), filename, full path and reason. Character errors include their Unicode character and line/column. Text can be selected and copied.

A repaired current sheet can have a damaged previous-save copy. Review its sheet type and use **Manage Sheets** to repair or delete that historical copy as appropriate, then choose **Validate Again**. Deleted sheets no longer appear. For external text repairs, keep a backup outside the book folder, preserve the managed filename, correct the characters/encoding, save UTF-8 and use **Review Folder Changes** to review/apply the edit. Saving a different encoding alone does not reverse already-corrupted characters.

Normal book opening does not validate every sheet. A new analyzer version triggers one automatic metadata upgrade for outdated sheets. Both successful versions and failed checks (version/hash/reason) are retained in database.json. Unchanged sheets are skipped on later opens; editor Save or reviewed external changes update the analysis. Failed checks become eligible again when their recorded content hash or the analyzer version changes. Validation is an explicit full check and remains read-only.

Read/access failure checks are scoped to their device, so one PC's filesystem failure does not suppress metadata analysis on another device. Source-content failure checks apply to the same analyzer version and content hash across devices.
