# OneDrive setup and two-PC acceptance

ChordBook now has manual OneDrive sync under **Options > OneDrive**: choose a direction and press **Sync Now**. It signs in if needed, compares the books, and applies changes without another confirmation dialog. Connect/Reconnect, Disconnect and Download Book are separate optional actions. Automated tests exercise independent filesystem books and a simulated cloud. Bill verified live Microsoft sign-in and initial publication of 531 sheets on October 4, 2026. Real Graph concurrency/interruption behavior and two-physical-PC acceptance remain to be checked below.

During Sync Now, the bottom status shows preparation/upload counts (for example, **Uploading: 237 of 531 sheets completed**) with a progress bar. The bar measures completed sheets in the current phase, not estimated time or bytes. Sign-in, comparison and catalog saves use an activity indicator; the sync finishes only after both catalog/state work and the local reload complete. Retries and resumed verified uploads count once, and Cancel retains recoverable transfer progress.

Windows Sync Now transfers up to four independent sheets concurrently during upload or cloud-download preparation. Local staging and catalog commits remain serialized; the cloud catalog is published only after all required uploads and receipts succeed. A failed/canceled batch waits for active transfers to stop and retains its journal for automatic recovery. Provider-neutral callers retain a serial default and can select a limit from 1 to 16 when their provider supports concurrent transfers; future mobile clients can use a lower limit. Retries respect the provider Retry-After delay. This limit is currently a Windows policy, not a user setting or adaptive throttle. The separate Download Book operation remains serial.

## Connect as a user

The app includes ChordBook's public application ID. Users do not register their own application or enter a GUID.

1. Open **Options > OneDrive**, choose a direction (normally **Two-Way**), and press **Sync Now**.
2. Sign in with your Microsoft account and approve OneDrive app-folder access.
3. Use that same account on each PC. Sync a disposable book on the first PC, then use Download Book on the second PC.
4. Follow the two-PC acceptance steps below before adopting a personal book.

## App registration (maintainer only)

Bill registered the app on October 4, 2026 and named it **Menees ChordBook**. Its app folder is **Apps/Menees ChordBook**. The built-in public client ID is `1b9e6b04-2e72-4654-815d-e62bc68dcf3a`. It is an application identifier, not a credential. The steps below document that registration for maintenance; they are not user setup.

You need access to a Microsoft Entra directory and permission to register applications there. The registration identifies the desktop app; your personal Microsoft account can still be the account you connect for OneDrive. If App registrations is unavailable, establish directory access before continuing. See Microsoft\'s registration guide linked below.

1. In the Microsoft Entra admin center, open **App registrations > New registration** and name the app Menees ChordBook.
2. Choose **Any Entra ID Tenant + Personal Microsoft accounts** (also displayed as **Accounts in any organizational directory and personal Microsoft accounts**), then Register. This matches ChordBook\'s configured organizational/personal account audience.
3. Under Authentication, add the **Mobile and desktop applications** platform with redirect URI `http://localhost`.
4. Under API permissions, add Microsoft Graph's **delegated** `Files.ReadWrite.AppFolder` permission.
5. Save your authentication changes. The **Application (client) ID** GUID from Overview is embedded in the Windows authentication adapter. A replacement registration requires updating that constant and rebuilding. No client secret is used.

Microsoft's [registration guide](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app) and [desktop configuration](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration) describe the portal settings. Organizational policy may require administrator consent. The app uses a system browser and a Windows-protected MSAL cache. Tokens, device identities and sync checkpoints stay local, outside books, backups and copied builds.

OneDrive stores books beneath its application folder, usually `Apps/<registration name>/<book GUID>`. ChordBook requests only [application-folder access](https://learn.microsoft.com/en-us/graph/onedrive-sharepoint-appfolder). Both PCs use the built-in registration and need the same Microsoft account to find these books.

## Start with a disposable book

1. On PC A, create a test book and add a text song plus a small PDF. Open OneDrive, select Two-Way and press Sync Now. Complete Microsoft sign-in if prompted. Check the resulting book/account, counts and conflicts.
2. On PC B, run the complete published folder, Connect with the same Microsoft account, then Download Book. Select the uploaded test book. The independent local copy keeps the same book identity.
3. Edit a song and add another on PC B. Press Sync Now there, then on PC A. Check titles, authored text bytes and PDF viewing. Reverse the sequence with edits on PC A.
4. Edit the same text sheet independently on both PCs before syncing. Sync A, then B, then A. Inspect the archived recovery sheet in Song Sheets; verify both versions survive.
5. Cancel a sync after a sheet upload, close/relaunch and press Sync Now to resume. Repeat after editing locally before resuming: Sync Now automatically preserves captured sheets as archived recoveries and compares current books again.
6. Try offline operation, canceled browser sign-in, Disconnect/Reconnect and expired credentials. Local editing must continue. Sync failures must retain books and confirmed progress.
7. Test Update This Device and Update Cloud on the disposable book. Check the resulting counts and song removals. Conflicting text/PDF bytes must survive as recovery sheets; Update This Device must make no cloud writes.
8. Start a sync on A and publish a new edit from B during A's comparison/transfer. A must refuse a stale cloud catalog. Press Sync Now again; it automatically preserves and replaces a stale pending transfer.

Run the portable deployment checks in [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md) on PC B. Record OS/architecture, build folder, result and any failure before adopting a personal book.

## Review, interruption and current limits

- Sync Now automatically authenticates, compares and executes. The report shows target account, direction, sheet counts, bytes, song removals and recognizable conflict names. An unchanged comparison reports Already up to date; completion includes elapsed mm:ss for comparison, transfer and local reload (excluding sign-in). Metadata/setlist conflicts select whole entities. Text/PDF conflicts keep an archived losing version.
- Newly uploaded sheet versions use a 16-character lowercase hexadecimal version ID derived once from the transfer ID, followed by the permanent bracketed sheet GUID. This shortens the former 32-character transfer token. Existing paths and pending transfers remain valid; unchanged sheets are not renamed or re-uploaded. Uploads never overwrite an existing same-name sheet: matching bytes are verified before reuse, and a conflicting version stops safely.
- Confirmed transfers bind original/target catalogs and sources to the book/device/account/folder. Durable stages and per-item receipts reconcile lost responses. Changed bytes are length/hash verified before upload; referenced and uploaded versions are checked before conditional catalog publication. Local commits use the existing incremental transaction and original-byte checks.
- Sync Now resumes an interrupted transfer when its direction, recovery epoch and local/cloud catalogs remain valid. If those changed, it automatically retains captured sheet versions locally and retires that transfer before making a fresh comparison. There is no separate recovery-mode button. It never rolls back a newer cloud catalog. Retired diagnostics remain device-local and currently have no automatic cleanup.
- Deletion/edit conflicts, deletions without a known base, and directional removal of a newly introduced item stop conservatively. Resolve these in the disposable books and sync again. Text/PDF version comparison with confirmed keep-left/right/both is available; see [RECOVERY.md](RECOVERY.md). General deletion/edit ambiguity resolution remains follow-up work.
- Existing cloud assets are retained when superseded. Automatic obsolete-asset cleanup is deferred to avoid removing another device's pending sources. Cloud-folder growth is a known limitation.
- Each individual simple upload is limited to 250,000,000 bytes. Upload sessions are not implemented. Catalog comparison is bounded to 32 million characters. A few complete shallow listings are used per sync, rather than a listing for every upload.
- The coordinator retries transient errors at most twice, honoring delay up to 30 seconds. Cancellation stops waiting. Authentication does not silently launch a browser.
- Keep ordinary local books in local folders. Copying application binaries through OneDrive is independent of app-managed book synchronization. Do not put two live local book folders under desktop OneDrive synchronization.

## Automated checks

From the repository root:

```powershell
dotnet test tests/Menees.Chords.Sync.Tests/Menees.Chords.Sync.Tests.csproj --settings tests/.runsettings
powershell -ExecutionPolicy Bypass -File eng/tests/Test-OneDriveAuthentication.ps1
```

The authentication fixture checks protected-cache persistence, signed-out refusal, disconnect, cancellation and client-ID validation without a browser/account. These checks do not establish live consent, real Graph conditional writes or physical-PC convergence.
