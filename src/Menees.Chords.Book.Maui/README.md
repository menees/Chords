# ChordBook MAUI Windows

This is the production Windows-first ChordBook application. It currently implements the
first vertical slice of Phase 2:

- migrate an existing prototype book, or create `My ChordBook`, under `%LOCALAPPDATA%\Menees\ChordBook\Books`;
- reopen that local book on later launches;
- create additional local books and remember the most recently used book;
- open an existing native ChordBook folder;
- import multiple ordinary or extensionless files through the Windows **All Files** picker;
- persist imports through `FileSystemBookStore` without rewriting source bytes;
- list and search the real book catalog; and
- select and render managed text charts or PDFs in the WebView; and
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
