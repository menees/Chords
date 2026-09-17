# Offline document viewer

The authored viewer uses Mozilla PDF.js **6.3.289**, restored from the prebuilt
pdfjs-dist package through the app's pinned libman.json. The
Microsoft.Web.LibraryManager.Build NuGet package restores client assets during
Windows builds; Node.js and npm are not required.

The Git-ignored pdfjs directory contains build/pdf.mjs, build/pdf.worker.mjs,
cmaps, standard_fonts, wasm, iccs and their licenses. Only the manifest and
authored viewer files belong in source control. The build enumerates restored
assets after LibMan runs so the first build from a clean checkout copies them.
The installed app includes these files and never downloads PDF.js at runtime.

Keep upstream bytes and license files intact when updating the pinned version
and file list. Source maps, Mozilla's generic viewer, sample PDF, translations
and Node-specific packaging are not included.

WindowsDocumentViewer maps only this bundled asset directory on assets.chordbook.invalid. Generated HTML and PDFs use the separate chordbook.invalid host because WebView2 folder mappings bypass request interception on their host. Its request handler
serves the current generated HTML and selected PDF stream, accepting the current
generation only. There is no book-directory mapping or local web server. At most
two response streams remain open until the next load/clear. An old request gets
404. The viewer's content security policy confines fetches to these two local app origins.

The application contract uses zero-based pages, zoom relative to fit-page, and
normalized horizontal/vertical scroll progress. The PDF renderer holds one canvas
capped at 4,194,304 pixels (16 MiB RGBA), cleans completed PDF page render data,
cancels superseded renders, and ignores viewport key repeats while rendering.
At zoomed sizes Page Up/Down first moves within the page. Moving back to a
previous PDF page opens its final viewport. Page boundaries use the same stamped
bridge as text charts. The native browser PDF viewer is no longer used.

Local reading history retains at most 100 song/sheet/entry positions per book.
Position messages are coalesced for 100 ms, with dirty preferences flushed every
30 seconds and when leaving performance. A forced termination can lose the last
30 seconds; a change immediately before leaving can lose the last 100 ms.
Reopening the same sheet after relaunch restores its saved position. This does
not automatically reopen the last performance setlist after launch. Book > Resume Last Performance explicitly restores the saved context and exact entry with audio stopped. Explicit
boundary navigation starts at the appropriate document edge rather than a saved
position.

PDF.js currently downloads the selected PDF in full (the native host streams it;
there is no base64 copy or whole-book buffering). Range delivery for very large
PDFs remains future work. Encrypted PDFs show an error; a password prompt, text
selection/search, PDF annotations, printing, touch/pinch polish and live
MAUI/Surface/AirTurn acceptance remain outside this increment.

## .NET/WebView2 regression check

On Windows with the selected .NET SDK, MAUI Windows workload and WebView2:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeViewer.ps1

The harness builds the app and tests its output assets with a deterministic
100-page PDF generated in C#. It exercises PDF painting, bounded rendering,
rapid paging, zoom/resize, scroll restoration, both document boundaries,
last-page load, invalid PDF errors, streamed delivery and keyboard routing.
Held repeats and form-field keystrokes must not trigger performance commands.
The production formatter's tablature scrollbar regression is tested in the same
WebView2 runtime. No Node.js or external PDF generator is required.

The offscreen window produces a capture and result in the printed temporary
directory. This check does not establish interactive MAUI or Surface acceptance.