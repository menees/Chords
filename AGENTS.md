# Repository working instructions

## Priority and continuity

- Deliver a feature-complete Windows ChordBook app first. Follow `eng/plans/PRODUCT_PLAN.md` for requirements and architecture and `eng/plans/IMPLEMENTATION_STATUS.md` for current progress and the next increment. Defer PWA and additional native targets until the Windows milestones are satisfied.
- At the start of implementation work, read the status page and relevant plan sections, inspect the working tree, and verify the affected code. Do not assume an earlier session completed a feature because a model, interface, or project exists.
- After each meaningful increment, update the status page with what shipped, what remains, validation performed, limitations, and the next concrete step. Keep requirements in the product plan; keep current implementation status in the status page.
- Complete small, useful increments. Prefer established code and dependencies over speculative frameworks or abstractions. This is a spare-time project maintained by one human; development time and recurring costs matter.

## Performance

- Write high-performance code. Every operation should run quickly and avoid wasted CPU, memory, disk I/O, network I/O, and battery use on low-power, low-resource tablets and phones.
- Make work proportional to the change. Ordinary metadata edits and setlist reorders must not read, hash, copy, or rewrite unrelated song/PDF assets, clone the whole database, or rebuild unrelated catalogs. Preserve the incremental storage guarantees documented in `eng/plans/REORDER_AUDIT.md`.
- Reuse indexes and computed results with explicit invalidation. Avoid repeated enumeration, nested full scans, redundant parsing/serialization, and unbounded caches. Stream large imports, backups, restores, and transfers; do not buffer an entire book's assets.
- Keep expensive work off the UI thread, propagate cancellation, and prevent stale asynchronous results from updating a newer view. Virtualize large lists and update only affected UI state where practical.
- Audio timing must be independent of rendering and UI queues. Drop expired clicks rather than replaying a backlog; bound queued work.
- For changes to hot paths, verify realistic scale and record relevant timings or allocation/I/O evidence. Prefer deterministic tests of work avoided over fragile timing assertions; preserve existing performance budgets. A desktop storage benchmark does not establish tablet UI performance.

## Maintainability and boundaries

- Avoid code duplication. Use well-factored code that a human can maintain and edit later. Put shared behavior in the owning library/service and keep UI handlers thin. Do not copy an existing implementation into another client or split a large class into partial files as a substitute for coherent responsibilities.
- Keep musical semantics in `Menees.Chords`, persistence/search in `Menees.Chords.Db`, UI-neutral use cases in `Menees.Chords.Book.Application`, and provider-neutral synchronization in `Menees.Chords.Sync`. Keep Windows APIs in platform adapters/`Platforms/Windows`.
- Use direct project references for this product family and add related projects to `Chords.slnx`. Preserve reusable library target frameworks and platform-neutral contracts.
- Preserve authored source syntax and bytes unless the user explicitly edits the source. Keep catalog metadata separate from source directives. OpenSong XML is read-only source in version 1.
- Retain staged writes, concurrency checks, interruption recovery, and failure-safe persistence. Do not trade data integrity for speed.
- Follow `.editorconfig`, existing local style, centralized package/build properties, and analyzers. Do not disable warnings or weaken tests to make a build pass.
- When developing on Windows, create and edit repository text files with Windows CRLF line endings, including Markdown and local planning documents. Do not introduce LF-only or mixed line endings through patching or file-writing tools. Preserve the existing encoding and verify line endings after edits; normalize touched files to CRLF before finishing. Respect explicit file-specific requirements (for example, scripts requiring LF) and byte-preserving source/test fixtures. Do not rely on Git to repair working-tree line endings.
- Use book/song/sheet terminology consistently in the UI. Keep technical storage details in diagnostics rather than normal user workflows.

## Verification and handoff

- Use the SDK selected by `global.json`; Windows app builds require the MAUI Windows workload. CI commands are in `.github/workflows/windows.yml` and `ubuntu.yml`.
- Build affected projects and run relevant existing tests. Add focused regression tests for behavioral fixes, persistence/recovery, and performance invariants. Documentation-only changes need link/content checks, not a full build.
- From the repository root, a targeted example is `dotnet test tests/Menees.Chords.Book.Application.Tests/Menees.Chords.Book.Application.Tests.csproj --settings tests/.runsettings`. Build the Windows app with `dotnet build src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj`. Use a solution build/test for changes spanning the solution.
- Test with synthetic or disposable books; do not use the personal library as a destructive test fixture. Separate automated test results from manual Windows/Surface/AirTurn verification, and record anything not exercised.
- In the handoff, report the outcome, checks actually run, material limitations, and the next plan item. Do not declare a phase complete until its exit criteria have evidence.
