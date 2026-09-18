# Offline song editor

CodeMirror 6 and its twelve small browser-module packages are pinned in the app's
`libman.json`. Visual Studio/LibMan restores only module entry points and MIT
licenses; the build copies them into `Editor/vendor`. An import map resolves bare
module names locally. No Node.js, package bundler, CDN access at runtime, or
checked-in vendor tree is required. All restored licenses ship with the app.

`editor.mjs` owns the undoable buffer, conservative highlighting, search/replace,
line numbers and virtualized editing. Musical parsing and live preview use
`Menees.Chords` through the application layer. The editor cannot write book files.
The native adapter implements `ISongTextEditor`; only Save commits text through
the existing application transaction.

Unchanged or fully undone buffers return their exact original text, including
mixed line endings. Edited buffers retain the original dominant newline style
and the user's final-newline choice. Encoding and BOM remain the responsibility
of the existing application save path. OpenSong XML remains source-read-only.

Run `eng/tests/Test-NativeViewer.ps1` from the repository root to exercise the
actual output assets in WebView2 without Node. It checks text preservation,
undo/redo, highlighting, search, read-only state and 10,000-line virtualization
alongside the existing PDF and tablature checks. Native MAUI modal/layout and
physical keyboard acceptance remain separate from this browser-asset test.

The native toolbar exposes Find, Replace, Undo, Revert Text and a Preview toggle.
Find/Replace widgets inherit the native entry's font; source remains monospace.
`preview-section.mjs` indexes editor source ranges only while preview is enabled.
It selects the outer ChordPro environment (including nested environments and
blank lines), or a blank-line-delimited paragraph outside environments. Whitespace
between sections stays with the preceding section. Header/metadata paragraphs
can be previewed by placing the caret there. Preceding key, capo and chord
definition directives accompany the slice; the .NET parser remains authoritative.
The index is cached for the current immutable document and caret lookups use
binary search. Moving within a section does not reload its preview. This is an
editing aid, not a complete document rendering: use song view for final layout.
