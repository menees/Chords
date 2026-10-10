# Compare and retain song sheets

Open **Song Sheets**, select one sheet, choose **Compare Sheets**, and choose another sheet from the same song. Archived sync recovery sheets are available here.

Text comparisons show aligned lines and highlight additions, removals and changes. Select text within either side to copy a range, or use **Copy Left** or **Copy Right** to copy that side's original decoded text, including its line endings. Source markup is displayed as text. The comparison is read-only; close it before editing a retained sheet.

Each side shows its filename, edit time, active/archived status and available sync recovery time. PDF sheets receive side-by-side previews rather than a PDF difference calculation. A mixed text/PDF pair receives separate previews.

- **Keep Left** or **Keep Right** confirms one atomic operation: make that sheet active and default, delete the other through a tombstone, and redirect song/instrument/setlist preferences to the retained sheet.
- **Keep Both** makes both active and preserves the previous default when it belonged to the pair.
- **Close Comparison** leaves both sheets untouched.

Retaining sheets never changes their authored bytes. The app refuses a stale catalog or changed source rather than discarding edits made after the comparison opened. Sync the confirmed resolution to carry its catalog changes and deletion tombstone to the other PC.

Text capture supports 4 MB per sheet. Line highlighting supports 20,000 lines per side and bounds divergent comparisons; larger line differences fall back to full read-only source without highlights. Oversized text sheets need an external viewer. PDF previews use the existing offline PDF viewer.

## Previous saved text

A changed-text editor save archives an exact copy of the preceding bytes in the same staged transaction. Its comparison label identifies the save that retained it and the preceding edit time. Use Song Sheets to compare it with the current sheet, restore it as a separate active sheet, or retain one/both through comparison.

The next changed-text save replaces archived automatic copies for that source sheet through tombstones. Copies explicitly restored or retained remain ordinary alternatives. Metadata-only saves create no history. Full backups include these copies; making a backup conservatively leaves the current automatic copy available until the next text save. Histories produced independently on two PCs may coexist until that next save.

Previous-save provenance is an optional catalog field. Current builds accept older books, but older strict readers cannot open a book after this field has been written. Use the same current app build on both PCs.

## Acceptance still needed

Automated disposable filesystem tests cover exact bytes, stale catalogs, externally changed originals, cancellation, tombstones, default/preference changes and unrelated locked assets. The native WebView smoke checks line alignment, highlights, encoded source and independent text selection. Full MAUI comparison layout, side-by-side PDF interaction, touch/DPI and two-PC propagation still require manual acceptance. Comparison actions currently use descriptive text pending Bill's icon choices.
