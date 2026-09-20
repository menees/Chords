# Button icon review

The Windows app uses individual SVG assets, with Fluent System Icons as the default source. Keep this approach; specialized actions may use other appropriately licensed artwork. Ask Bill before choosing an ambiguous icon.

## Icons still to choose

These buttons retain their text captions until an icon is selected. All are in top toolbars.

| Screen | Button | Meaning to convey |
|---|---|---|
| Backup and Restore | Backup Current Book | Write a backup of the current book. |
| Backup and Restore | Restore as New Book | Restore a backup into a separate book. |
| Backup and Restore | Replace Current Book | Recover the current book from a backup, after creating a safety backup. |
| Backup and Restore | Backup Settings | Export settings without song assets. |
| Backup and Restore | Restore Settings | Import settings without replacing the book. |

Choose distinct artwork for the backup and restore variants; a generic arrow or save symbol does not explain those differences. The specialized icons listed below were subsequently supplied by Bill.

## Supplied specialized icons

- Metronome now uses Bill's Metronome.svg.
- Learn a Key / Pedal uses Learn.svg. Assign Learned Key uses Fluent AttachArrowRight, selected by Bill; the former Reassign.svg was removed.
- Acoustic Guitar, Banjo, Bass Guitar, Electric Guitar, Mandolin, Piano and Ukulele use the supplied SVGs in instrument selection.
- Source notes are in Resources/Icons/README.md in the MAUI project.

## Selected mappings to implement

- Custom instruments without dedicated artwork: use Fluent **MusicNote1**, as selected by Bill. Replace the current text fallback when this mapping is implemented.

## Applied mappings

| Action | Fluent icon |
|---|---|
| Assign Learned Key | AttachArrowRight |
| Make Default | Star (Bill's selection; ChordBook has no Favorites concept) |
| Add songs / New instrument | Add |
| Add sheets | DocumentOnePageAdd |
| Edit sheet / Edit setlist | EditLineHorizontal3 |
| Finish editing / Apply reviewed changes | Checkmark |
| Rename | Rename |
| Delete permanently / Delete instrument | Delete |
| Archive / Restore archived item | Archive / ArchiveArrowBack |
| Move up / down | ArrowUp / ArrowDown |
| Remove setlist entry / input binding | Subtract |
| Entry settings | Settings |
| Save report | Save |
| Cancel selection | Dismiss |
| Cancel running validation / backup operation | Stop |
| Refresh review / Validate Again | ArrowClockwise |
| Restore default bindings | ArrowReset |
| Preview metadata | PreviewLink |
| Import metadata as directives | ArrowImport |

Icon buttons retain descriptive tooltips and accessible names. Edit/Done and Archive/Restore update the icon and description together. Actions local to a setlist entry remain beside that entry.

## Intentional text controls

Alphabet jump buttons, transpose key/offset choices and +/- controls communicate their value directly and retain their labels. Standard operating-system confirmation/action-sheet buttons and CodeMirror's built-in Find/Replace panel retain their text; this sweep covers application-owned native action buttons, not vendor UI.

## Verification

The native icon smoke runner checks bounds and actual rendered pixels for all 48 bundled SVGs in a temporary WinUI window. Actual toolbar wrapping, tooltip placement, keyboard focus, and icon recognition still require an interactive Windows check.
