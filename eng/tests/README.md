# Windows integration checks

These checks use generated fixtures and temporary output directories. They do
not use the personal library. Run from the repository root with the SDK selected
by global.json.

## PDF renderer and native host

On Windows with the selected .NET SDK, MAUI Windows workload and installed WebView2:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeViewer.ps1

No Node.js, npm, Playwright or global JavaScript tools are required. The script
builds the Windows app (including pinned LibMan asset restoration), generates a
100-page PDF in C#, and opens an offscreen WPF WebView2 window. It tests the actual
app-output viewer assets, including the worker, PDF painting, bounded canvas,
rapid paging, zoom/resize, normalized scroll restoration, both boundaries,
last-page loading, invalid PDF errors, streamed delivery and keyboard routing.
It also tests the production tablature stylesheet and proves the fixture catches
the original individual-line scrollbar regression. JavaScript assertions execute
inside WebView2; the test runner and PDF generator are .NET.

The script prints the temporary fixture/capture directory. These checks do not
establish interactive MAUI, Surface, touch or physical pedal quality.

PDF.js is restored from the pinned app libman.json into a Git-ignored directory.
A first restore needs access to jsDelivr; the installed app remains fully offline.
Do not commit the restored dependency files.

## Native icons

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeIcons.ps1

This loads all bundled Fluent icon paths through the production WinUI helper.
It does not establish interactive layout, tooltip or theme quality.

## Native metronome

On Windows with an audio output available:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeMetronome.ps1

The harness links the production Windows adapter and application library. All
native audio output is muted. It checks six sound choices, native buffer delivery,
visual-only mode without an audio graph, audio-only beat state, graph reuse for live sound/tempo edits,
audio graph release and stopped frame submission when audio is disabled, beat continuity when audio is re-enabled, pre-cancellation, Stop during initialization and superseded initialization.
It also reports processing time and managed allocations for five minutes of
synthetic 300 BPM playback with four clicks per beat. This throughput measurement
is not physical playback latency/jitter or tablet battery evidence.

Samples are bundled in the application library. The optional
New-MetronomeSamples.ps1 script regenerates the five original PCM files; do not run
it for ordinary verification. See the Sounds/README.md in that library for format,
normalization and licensing notes.

## Remaining manual acceptance

Use a disposable book to exercise Windows pickers, canceled backups, restoring
as a new book, settings review/cancellation, and relaunch followed by Options >
Resume Last Performance. Check repeated setlist entries and unavailable songs.
On Surface, listen for first-beat bursts, long-session drift, sound-level changes
and audio-route failures while turning text/PDF pages. Test real pedal modifiers,
held-key suppression and focus changes. Window deactivation stops the metronome;
returning requires an explicit restart. Never use the personal library for
replacement or destructive recovery experiments.
For replacement recovery, use a disposable book and a matching earlier backup.
Verify the count preview, Cancel, automatic safety-backup path, restored catalog,
and a foreign backup's Restore as New Book offer. Check Book should report
intentionally missing/changed/duplicate fixture sheets, allow cancellation and
save a readable report. It must not alter files. Provider sync has not yet been
implemented; its future comparison path must honor the persisted recovery epoch.
The native viewer harness also checks the bundled CodeMirror 6 editor: exact unchanged text, mixed newlines, undo/redo, highlighting, search/replace, read-only state and virtualization of 10,000 lines. All editor modules load locally from app output; no Node or runtime CDN is used.

Editor checks also cover native UI font overrides, Find/Replace input focus, current-section selection (including nested/abbreviated ChordPro environments), musical context, paragraph fallback, range invalidation and preview-safe undo. The editor capture leaves Find/Replace open for visual inspection.
