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
It also checks the production display preview at 12/18/28 px: chord symbols remain
visible with diagrams disabled, the preview includes minor/major-seventh chords,
and only a standalone dominant 7 has smaller, raised browser geometry; minor 7 and major-seventh triangles remain baseline text. Captures retain
each font-size/diagram combination. It also tests the production tablature stylesheet and proves the fixture catches
the original individual-line scrollbar regression. JavaScript assertions execute
inside WebView2; the test runner and PDF generator are .NET. The editor checks also verify that Ctrl+S consumes the browser shortcut and sends one Save request without modifying the buffer. The full-editor metadata fixture also verifies metadata Save/return and unchanged authored text/hash. Physical keyboard/touch and Surface acceptance remain separate.

The script prints the temporary fixture/capture directory. These checks do not
establish interactive MAUI, Surface, touch or physical pedal quality.

PDF.js is restored from the pinned app libman.json into a Git-ignored directory.
A first restore needs access to jsDelivr; the installed app remains fully offline.
Do not commit the restored dependency files.

## Native published resource languages

After publishing, run:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeMuiResources.ps1 -PublishedDirectory artifacts/ChordBook-win-x64-<timestamp>

This read-only Windows MUI lookup probe uses the actual published WinUI DLLs and sets English and French thread UI preferences in its own PowerShell process. Both DLLs must resolve existing English MUI files through Windows' native resource lookup. It opens no books or app windows. Publish-ChordBook.ps1 separately validates the full native/managed resource allowlist and required English files. This establishes native file lookup/fallback, not visual control rendering or non-English Windows installation acceptance.

## Native icons

For the executable/window branding assets after a Windows build:

    pwsh -NoProfile -File eng/Export-ChordBookIcons.ps1 -Check

    pwsh -File eng/tests/Test-AppIcons.ps1 -Executable src/Menees.Chords.Book.Maui/bin/Release/net10.0-windows10.0.19041.0/win-x64/Menees.Chords.Book.Maui.exe

This read-only Win32 probe checks all 15 embedded icon images against the deployed
ICO, then loads each requested size with Windows. It opens no windows or books.
The exporter's separate read-only -Check rerenders the SVGs using the restored
MAUI graphics libraries and verifies the checked-in PNG/ICO bytes.
See [application icon sources and regeneration](../../src/Menees.Chords.Book.Maui/Resources/AppIcon/README.md).

For the in-app Fluent/custom action icons:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeIcons.ps1

This opens a temporary WinUI window and checks actual rendered pixels and viewport
bounds for all bundled Fluent and custom icons through the production helper. No books
are opened.
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
and a foreign backup's Restore as New Book offer. Validate Book should report
intentionally missing/changed/duplicate fixture sheets, allow cancellation and
save a readable report. It must not alter files. Provider transport and end-to-end coordination have simulated-cloud tests, including two independent filesystem books and interrupted recovery.
Live sign-in, Graph conditional behavior and two physical PCs remain manual; follow [OneDrive acceptance](../ONEDRIVE.md).
The native viewer harness also checks the bundled CodeMirror 6 editor: exact unchanged text, mixed newlines, undo/redo, highlighting, search/replace, read-only state and virtualization of 10,000 lines. All editor modules load locally from app output; no Node or runtime CDN is used.

Editor checks also cover Ctrl+Y/Ctrl+Shift+Z redo, capitalized search captions, native UI font overrides, Find/Replace input focus, current-section selection (including nested/abbreviated ChordPro environments), musical context, paragraph fallback, range invalidation and preview-safe undo. The editor capture leaves Find/Replace open for visual inspection.

## OneDrive authentication without a live account

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-OneDriveAuthentication.ps1

This links the production Windows authentication adapter into a disposable console
fixture and checks protected-cache verification, signed-out silent-token refusal,
disconnect, cancellation and public client-ID validation. It opens no browser and
uses no live account or book. The MSAL package versions come from the central
package file; no Node installation is needed. See [OneDrive development](../ONEDRIVE.md)
for registration requirements, limitations and remaining live acceptance.

The native viewer smoke also checks both conversion targets as undoable buffer replacements restoring the exact original line endings, plus read-only sheet comparison: aligned highlighted lines, independent left-side text selection, encoded source markup and a restrictive content security policy. The isolated MAUI fixture below additionally exercises the production comparison modal and Windows document adapter.

Run `eng/tests/Test-NativeMetadata.ps1` for production comparison and saved-tab controls with disposable data: dark-theme text differences, PDF paging/painting, mixed previews, released source handles, saved-tab sorting and failed-save retention. Compact full song-editor metadata and value dialogs are exercised by `eng/tests/Test-NativeSetlist.ps1` as described below; the retired combobox/scalar fixture is removed. Neither fixture uses a personal book.

## Acceptance build mirroring

Run `powershell -ExecutionPolicy Bypass -File eng/tests/Test-AcceptanceMirror.ps1` to execute the production mirror against disposable build folders. It checks initial and zero-write repeats, fresh timestamps on identical content, same-size changed bytes, additions, hidden stale files/directories, empty directories and preflight refusal for locks, incomplete assets, overlap, file/directory conflicts and junctions. It never touches Maui.Acceptance, a book or OneDrive; fixture paths and no-op timings are reported.

## Saved-tab dialog

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-NativeMetadata.ps1

The metadata/comparison fixture also runs the production native New/Edit Tab
dialog with synthetic drafts. It verifies new defaults, name validation, all sort
inputs reaching Save, retained fields/error after a failed save, successful retry,
loading saved grouping/sorting independently of temporary search, and Cancel
without invoking Save. Dark and narrow light dialog captures are retained with
the fixture. This does not establish physical Surface touch or full browser
navigation acceptance; catalog persistence and locked-asset sorting are covered
by the application/database test suites.

## Setlist editor and named sync reports

Run `eng/tests/Test-NativeSetlist.ps1` to exercise the compiled production MainPage and native handlers with an isolated synthetic book. The fixture verifies stable Sort by Name with repeated occurrences, no writes before Save, return to the current setlist, the editor Back/Save controls, Keep Editing, staged Add Songs, Add Songs cancel, Discard Changes, unchanged Back and draft retention/retry after a deliberately locked-catalog save failure. Captures include the editor and narrow detail view. It uses a separate WebView profile, explicit synthetic device identity and no live account or personal book. This supplements application tests for draft validation/concurrency, locked assets, 10,000 entries and bidirectional named sheet/setlist sync reports; it does not establish Surface touch or physical two-PC acceptance.

The same native fixture exercises the production OutputEditor/Windows handler used by the sync report: long initial output, manual scrolling while idle, appended lines, complete report replacement, rapid updates, clearing, short reports and subsequent long output. It checks the native ScrollViewer offset against its measured bottom after layout. It also opens the shared cloud sync page with an in-memory provider, checks the provider labels and report, verifies the rendered native progress track and indicator above the report, and checks that the bar hides after completion and an already-up-to-date sync. The fixture uses no live cloud account.

## View date sorting

The native setlist fixture also exercises the production Sort View dialog and browser: date choices, ascending/descending draft, Apply/Cancel/Default, flat chronological Songs without letter jumps, retained search, per-view isolation, local settings across BookSession recreation, access dates after history reload and unchanged catalog bytes. It cleans up only synthetic book preference keys. Date choices in New/Edit Tab are covered by the native metadata fixture. Unit regressions cover UUID creation timestamps/unknown legacy IDs, UTC chronological comparison, missing-last and stable ties, live date overrides, date saved-tab round trips, locked assets, history timestamps beyond the last 100 Recent entries and deleted-ID pruning.

## Metadata directive import review

The native setlist fixture also opens the production MetadataImportPage with synthetic source/metadata. It checks `{tag: Christian}` insertion, omission of unchanged title/key, a single checked line reference without a Picker, checkbox preview updates, explicit duplicate-target selection, Back without applying and the no-changes state with Apply disabled. It retains single-match/no-change captures. Application tests cover idempotent import, existing direct/meta-tag syntax preservation, unchanged multi-value metadata, unsafe conditional matches and Save/reload source observations. Metadata refresh tests verify the current analyzer version recognizes tags from old analyses once without changing source bytes.

## Windows file activation and import-on-open

The native setlist/app fixture now starts with a spaced Unicode file path and uses the production WindowsFileActivation coordinator. It verifies cold argument delivery, launches a second copy to check activation redirection and clean secondary exit, and exercises registry registration only under a disposable HKCU subtree. It checks quoted commands/capabilities and preservation of an existing extension default; it never changes the user's actual associations or opens Default apps. The production MainPage is tested with a queued external file while a synthetic editor is open, import/display after return, repeated hash-match opening without catalog writes and possible-version review with Skip/Back unchanged. Temporary test-book preferences and registry keys are removed.

Application tests cover exact-hash matches while managed assets are exclusively locked, genuinely new import and byte equality, repeated opens, title/artist conflicts, reviewed snapshot retention after external changes, explicit Add Sheet, multiple song identities sharing content, temporary previous-save display without default changes, invalid/missing/canceled input and catalog concurrency. Actual Explorer default selection and cross-PC shell behavior still require user acceptance.

## Book startup, persisted analysis checks and fresh validation

The native startup fixture opens a synthetic old-analysis book containing a rejected sheet. It checks the persisted analyzer-version/hash failure, healthy refresh, header/recent books and unchanged source bytes. It then opens the book through a new BookSession with every sheet exclusively locked and asserts zero reanalysis and exact unchanged catalog bytes. This verifies repeated startup avoids sheet reads even after a failed check.

The production Validate Book page rereads current sources and identifies invalid content by song, path, character/location and sheet type. The fixture saves a repaired current sheet through the normal editor service, checks that Validate Again returns a clean report, and verifies that saving the repaired sheet did not create another sheet. The metadata fixture opens the read-only Song Details page from an unsaved draft and checks offsets and setlist membership. It also uses Manage Sheets > Duplicate Sheet and checks that the selected copy is editable and the existing default stays selected for viewing. It verifies read-only/copyable output and absence of Save Report, and retains invalid/clean captures. It restores temporary synthetic preferences.

Database regressions cover retained failures and source/catalog data, version/hash cache invalidation, no-op repeated checks, independent fresh parsing for current and archived sheets even with matching hash/current analysis version, cancellation, and a 32 MiB streamed PDF validation with one source open and a bounded-allocation assertion. Report tests cover live issue identity, sheet type, repair steps, invalid catalogs and no historical-warning output. Physical tablet timings remain manual acceptance.

## Compact song metadata and selection dialogs

The native setlist fixture opens production SongEditorPage with synthetic text/catalog data. It verifies 1/2/3/5 metadata rows at 1,908/1,100/750/480 window widths, entry bounds, nonwrapping labels and available source-editor height. Captures record all widths. It also measures the 2-DIP entry/Edit gap and checks exact native placeholders for Key, Tempo, Capo, Time signature, Year, Duration and Tags without leaving the metadata draft dirty. Empty hinted fields are italic, typing values restores regular type, clearing restores italics, and dedicated placeholder colors follow light/dark themes; both placeholder themes have captures. It checks current-value preselection/checked-first display, a 10,000-artist virtualized list, search retaining hidden checks, Cancel without changes, adding literal semicolon values, multi-select artists/tags and single-select album/time signature. Dialog Apply leaves the catalog unchanged; page Save is blocked while a field dialog is open. The fixture checks legacy multi-value preservation, hidden copyright retention, actual editor metadata Save/return and exact authored source text/hash preservation. Artist/light and time-signature/dark dialog captures are retained. Unit tests cover normalized deduplication, accent-insensitive filtering, detached selection, literal value round trips, single-selection replacement and a 10,000-choice search sample.

## Browser viewport through song editing

The native setlist fixture opens a disposable 721-song book, scrolls to Maybe, and checks both grouped title order and ungrouped descending title order. It retains the first visible song identity and its exact viewport Y (within one DIP), checks ordinary open/Back, saves actual source edits through the production editor and MainPage mutation flow, then checks Back and settled layout again. It verifies unchanged search/sort and that an immediate new search cancels old restoration. Metadata-only synthetic neighbors avoid hundreds of irrelevant source writes; the edited song uses a real managed text sheet. Browser-return captures and measured row offsets are retained. The same fixture checks the Key placeholder is Dm. Physical Surface/touch acceptance remains manual.

## Import review source failures

BookImportReview retains successful snapshots and a separate read-only Failures collection with selected path, filename and read/encoding/analysis error. Db regressions cover good sources before/after binary controls in the prefix and beyond 4 KiB, invalid UTF-16, missing/locked files, incoming matching after failures, unchanged catalog before Apply, valid-only Apply, scratch disposal, all-failed/no-op reviews, invented decisions and cancellation. File-opening tests verify invalid/missing sources return the failure review without import, while cancellation/stale-book protection remains in force. Existing snapshot-tamper rollback tests retain atomic Apply guarantees.

The native setlist fixture exercises actual mixed-file review: failed category selected first, category/summary counts (including All files), full path and control character line/column, read-only failure details, no failed import/compare action, Cancel without writes, all-failed Apply disabled, and real valid-only Apply with a separate failed count. It retains failure/all-file screenshots and uses disposable files/books only. A read-only audit of an externally supplied extract may use the production reviewer against an in-memory disposable book; it never applies those sources or edits the extract/personal library.

## Import comparison roles, defaults and legacy text

Import candidates mark the normal active default sheet using priority/media/identity ordering. Regressions cover multiple-sheet/default selection with an archived higher-priority sheet, ambiguous related songs requiring a choice, and Skip remaining unchanged. Comparison sides carry incoming-source provenance. Native reviewed import selects the default among current/previous-save sheets, enables Compare without another picker action, checks explicit existing/incoming labels, retains a manually chosen comparison sheet on reselection, and confirms no comparison/catalog writes. Captures record the selected default and side roles.

Analyzer version 4 accepts C1 characters in UTF-8/BOM and deterministic Latin-1 fallback text, retaining the original bytes and decoded characters. Genuine binary rejection/diagnostic tests use C0 characters. Tests verify root-library parsing, bounded prefix acceptance, original import bytes, explicit repair through editor Save preserving encoding/BOM, and a previously rejected older-analysis C1 sheet upgraded once with no repeated catalog write. Read-only extract review is repeated against a disposable in-memory book. No mojibake repair or locale-dependent encoding fallback is automatic.

Bulk tagging: Test-NativeSetlist.ps1 verifies compact setlist Edit/Select Songs actions, distinct checked members including archived songs in the main Songs view with prior filters cleared, Tg labels, and Edit Tags through normal Songs Select mode, union preselection across disjoint/overlapping tag sets, searchable add/new values, Cancel without writes, replacement/add/remove/clear-all behavior, source-record preservation, and tag:!LyricOnly in the visible browser. SongManagementTests cover persistence, no-op revisions/writes, unknown selections, cancellation and stale-catalog rollback; ReorderStorageTests edits and clears tags for 100 songs in a 531-song book with every asset exclusively locked and retains unrelated catalog identities. BookSearchIndexTests cover negated values, missing fields, phrases, accent/case normalization, incremental refresh, genre extracted from ChordPro meta independently of tags, simple * / ? patterns, field presence/absence, escaped !/*/?/quotes/backslashes/whitespace, quoted patterns, literal brackets/regex punctuation, repeated-star coalescing, per-value matches, and a 10,000-song/adversarial-pattern budget.

Select All / Unselect All: Test-NativeSetlist.ps1 exercises filtered, grouped and flat 721-song views, checks a single selection notification per batch, unrealized rows, disabled action states for full/empty selections, keeping Select mode active, no catalog writes, 480-DIP action bounds and selection while adding songs to a detached setlist draft.

Metadata picker typography: the native setlist fixture opens New Song with blank Tags, applies Christian through the real field picker and compares the rendered entry pixels with a freshly loaded regular-tag entry before any Save. The regression failed with the old whole-field font toggling despite native FontStyle reporting Normal. It also checks the actual italic placeholder template part, typed/cleared field values, theme styling and metadata dirty state.
