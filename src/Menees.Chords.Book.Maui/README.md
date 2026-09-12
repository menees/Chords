# ChordBook MAUI Windows

This is the production Windows-first ChordBook application. The delivered Windows features include:

- migrate an existing prototype book, or create `My ChordBook`, under `%LOCALAPPDATA%\Menees\ChordBook\Books`;
- reopen that local book on later launches;
- create additional local books and remember the most recently used book;
- rename a book and switch among up to ten recent valid book folders;
- open an existing native ChordBook folder;
- import multiple ordinary or extensionless files through the Windows **All Files** picker;
- persist imports through `FileSystemBookStore` without rewriting source bytes;
- list and search titles, artists, tags, and extracted source metadata, with optional archived-song visibility;
- show information-dense wrapping song summaries with compact metadata labels, grouped by their visible `#`/A-Z initials,
  with a filtered jump index;
- version and refresh extracted directive metadata once when an older book is opened, without rewriting managed song bytes;
- use a full-window management surface for library and book work;
- browse and filter an information-dense `#`/A-Z-grouped setlist catalog with a used-letter jump index, song counts,
  known total durations, dates, and searchable notes;
- open an ordered setlist, enter an explicit edit mode, rename it, add or remove songs, and move entries up or down;
- enter an explicit multi-select mode from the filtered song catalog and append all selected songs to a new or existing
  setlist with one database commit, while preserving the visible order;
- use the same top-left **Back** placement to leave performance and setlist-detail views, with the same navigation
  hierarchy exposed through Android's system Back button;
- switch to a separate full-window performance surface and initially focus its viewer when rendering a managed text chart or PDF;
- move to the previous or next song in either the filtered library or selected setlist context, then return to the unchanged management view;
- advance paginated text charts by exactly one rendered page with Page Up and Page Down; and
- open the current book folder in Windows File Explorer from the status-bar link.

Abandoned `.chordbook-stage-*` transaction folders are removed at startup after a short
age guard. A successfully migrated legacy book remains in its old location as a backup.

Build from a Windows machine with the .NET 10 MAUI workload installed:

```powershell
dotnet build src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj
```

The project intentionally targets Windows in the production build today. On non-Windows
hosts it compiles an empty placeholder so the repository's shared-library CI remains
cross-platform. Future MAUI targets can replace platform adapters without changing the
database or store contracts.

September 6 update: Select mode uses the native list selection indicator without a duplicate row checkbox. Viewer resize scripts wait for successful navigation and stop when performance closes or the viewer unloads; obsolete song loads cannot reopen the viewer after Back. Setlist Edit now offers Archive/Restore. Enable Show archived in the setlist overview to find archived lists; their ordered entries are retained. Archive/restore persistence is covered by the application tests. The Windows build passes; desktop interaction verification remains pending because Computer Use app approval timed out.

September 7 storage update: metadata changes now mutate the active database in place and save only JSON. Asset transactions stage only incoming content and move only affected files, with an interruption-recovery journal. Unchanged assets are never copied or hashed during ordinary book/setlist operations. Batch imports and backups process asset payloads incrementally. Computer Use was disabled at the user's request for that audit.
