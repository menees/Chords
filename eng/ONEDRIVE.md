# OneDrive development and acceptance

The September 18 increment supplies transport, comparison, conflict selection,
application-folder discovery and a Windows authentication adapter. It does **not**
yet provide usable end-to-end sync in the ChordBook UI. Do not migrate the only copy
of a book to cloud storage to test these components.

## Public application registration

Before interactive acceptance, register ChordBook as a public desktop application
in Microsoft Entra. The current Windows adapter expects:

- Supported accounts: organizational directories and personal Microsoft accounts.
- Mobile/desktop redirect URI: `http://localhost` for the system-browser flow.
- Delegated Microsoft Graph permission: `Files.ReadWrite.AppFolder`.
- A public application/client GUID; no client secret or certificate.

Pass that GUID, an app-local cache directory, and an optional previously selected
MSAL home-account ID to `WindowsOneDriveTokenProvider.CreateAsync`. The upcoming
Connect dialog must store the public client ID and replica identity as local
configuration. Tokens stay solely in the protected MSAL cache, outside books,
backups, portable settings and source control. Creating the adapter loads only a
selected cached account; it does not infer an account or launch sign-in.
`AuthenticateAsync` is for an explicit Connect/Reconnect action. Token retrieval
uses silent acquisition and reports when reconnection is needed.

The adapter uses Microsoft's [desktop token cache](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization)
and [interactive acquisition](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/acquiring-tokens-interactively)
APIs. Cache-protection failures stop connection; there is no plaintext fallback.
Organization consent policies may still require administrator approval.

## Application folders and transport

`OneDriveBookFolders` accesses `me/drive/special/approot`. First access can create
OneDrive's `Apps/<registered application name>` folder; invoke it only in a
user-directed connection flow. Each book uses its stable GUID in D format as a
child-folder name. `EnsureAsync` creates only that folder and handles a concurrent
creator without replacing anything. It does not initialize `database.json`.
`ListAsync` lists candidate GUID-named book folders; the selected database must
still be validated against its book identity before use.

The [Graph application-folder documentation](https://learn.microsoft.com/en-us/graph/onedrive-sharepoint-appfolder)
describes this restricted scope. No broad file permission is requested.

`OneDriveHttpTransport` operates within a configured account/book folder. Listings
are complete, shallow and paginated, with no advertised change-token support.
Downloads follow a preauthenticated HTTPS URL without forwarding a bearer token.
Uploads stream from a readable, seekable source. New files request fail-on-conflict;
replacement, rename and deletion require an explicit ETag. HTTP errors expose
status and retry delay rather than provider bodies or access-token URLs.

The current simple-upload limit is 250 MB. Resumable upload sessions and automatic
retry are not implemented. Actual Graph create/replace conditional behavior still
requires disposable-account acceptance before a transfer executor can ship.

## Comparison and safety boundaries

`CloudBookComparisonService` reads `database.json`, checks referenced filenames,
and returns a captured ETag plus a detached whole-entity merge. It neither opens
sheet assets nor verifies their bytes, and it is not an executable sync plan.
Missing databases/sheets, portable filename collisions and wrong book identities
stop comparison. Unrelated assets are never candidates for deletion.

`BookSyncMerger` combines changes to different entities against a provider-specific
fingerprint base. Simultaneous changes select a whole entity; a setlist's exact
ordered entries remain together. Plausible separated timestamps choose the winner;
ambiguous clocks fall back to a stable fingerprint comparison. Conflicting sheet
content receives a deterministic archived recovery identity and a source mapping.
This describes recovery bytes to copy; it does not copy them.

Deletion/edit conflicts, deletion without a known base, and directional sync that
would discard a new entity stop conservatively. Resolution remains future work.
The caller must invalidate provider merge state after a restore/recovery epoch.
A merge result alone must never be written to a live book or cloud replica.

## Remaining implementation order

1. Persist independent replica state and crash-safe operation journals outside books.
2. Build a confirmed transfer plan with target, direction, counts, conflicts and bytes.
3. Hash-verify changed assets into staging; preserve every losing sheet before mutation.
4. Upload new asset versions before conditional database replacement, retaining old
   assets until the database commit. Avoid in-place asset replacement that could
   destroy another device's bytes before a database ETag failure is discovered.
5. Commit local changes through the staged book store, retry bounded remote races,
   resume interrupted work, then clean up only explicitly managed obsolete assets.
6. Add Connect/Reconnect/Disconnect, Sync Now, reports and cloud-book download UI.
7. Exercise two Windows installations, offline/cancellation/quota/expired credentials,
   both directional modes, concurrent edits/deletions and interrupted recovery.

## Automated evidence

Run from the repository root:

    dotnet test tests/Menees.Chords.Sync.Tests/Menees.Chords.Sync.Tests.csproj --settings tests/.runsettings

Tests use synthetic books and scripted Graph responses; no account credentials
are needed.

On Windows, run:

    powershell -ExecutionPolicy Bypass -File eng/tests/Test-OneDriveAuthentication.ps1

This links the production adapter into a disposable fixture and checks Windows
protected-cache persistence, signed-out token refusal, disconnect, cancellation
and client-ID validation. It performs no sign-in and does not test token expiry,
consent, real Graph ETags or multi-computer convergence.
