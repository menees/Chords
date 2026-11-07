#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SetlistEditTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task SortAndSaveRetainOccurrencesPreferencesAndUnrelatedIdentityWithAssetsLocked()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), "ChordSetlistDraft-" + Guid.NewGuid().ToString("N"));
		try
		{
			Guid device = Guid.NewGuid();
			using FileSystemBookStore store = new(root);
			BookLocation location = await store.CreateBookAsync("Draft", device, token);
			BookApplicationSession app = new();
			await app.ActivateAsync(store, location, token);
			Guid zulu = await app.CreateSongAsync("Zulu", ["Artist"], [], "[C]Zulu", device, token);
			Guid alpha = await app.CreateSongAsync("Alpha", [], [], "[G]Alpha", device, token);
			Guid list = await app.CreateSetlistAsync("Practice", device, token);
			Guid other = await app.CreateSetlistAsync("Untouched", device, token);
			IReadOnlyList<Guid> ids = await app.AddSongsToSetlistAsync(list, [zulu, alpha, zulu], device, token);
			SetlistEntrySettings original = app.GetSetlistEntrySettings(list, ids[0]);
			Guid fileId = app.Database!.SongFiles.Single(file => file.SongId == zulu).Id;
			await app.SaveSetlistEntrySettingsAsync(original, fileId, 3, device, token);
			ChordDatabase identity = app.Database;
			Setlist unchanged = identity.Setlists.Single(item => item.Id == other);
			Song untouchedSong = identity.Songs.Single(item => item.Id == alpha);
			long revision = identity.Setlists.Single(item => item.Id == list).Revision.Revision;
			List<FileStream> locks = [];
			try
			{
				foreach (SongFile file in identity.SongFiles)
				{
					locks.Add(File.Open(Path.Combine(store.GetDirectory(location), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None));
				}

				string before = await store.ReadDatabaseJsonAsync(location, token);
				SetlistEdit draft = app.GetSetlistEdit(list);
				draft.SetOrder([ids[2], ids[1], ids[0]]);
				draft.SortByName(new Dictionary<Guid, string> { [zulu] = "Zulu", [alpha] = "Alpha" });
				draft.Value.Entries.Select(entry => entry.Id).ShouldBe([ids[1], ids[2], ids[0]]);
				draft.Value.Name = "Sorted practice";
				draft.HasChanges.ShouldBeTrue();
				(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
				app.GetSetlistEntries(list).Select(entry => entry.EntryId).ShouldBe(ids);
				await app.SaveSetlistEditAsync(draft, device, token);
				app.Database.ShouldBeSameAs(identity);
				identity.Setlists.Single(item => item.Id == other).ShouldBeSameAs(unchanged);
				identity.Songs.Single(item => item.Id == alpha).ShouldBeSameAs(untouchedSong);
				Setlist saved = identity.Setlists.Single(item => item.Id == list);
				saved.Name.ShouldBe("Sorted practice");
				saved.Revision.Revision.ShouldBe(revision + 1);
				saved.Entries.Select(entry => entry.Id).ShouldBe([ids[1], ids[2], ids[0]]);
				saved.Entries[2].PreferredSongFileId.ShouldBe(fileId);
				saved.Entries[2].TransposeSemitones.ShouldBe(3);
				DatabaseValidation.Validate(identity).ShouldBeEmpty();
			}
			finally
			{
				foreach (FileStream file in locks)
				{
					file.Dispose();
				}
			}
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	[TestMethod]
	public async Task DiscardDropsAllDraftChangesAndUnchangedSaveDoesNotRewriteCatalog()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Discard", device, token);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location, token);
		Guid song = await app.CreateSongAsync("Song", [], [], "[C]Song", device, token);
		Guid list = await app.CreateSetlistAsync("Original", device, token);
		await app.AddSongsToSetlistAsync(list, [song, song], device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		SetlistEdit draft = app.GetSetlistEdit(list);
		draft.Value.Name = "Changed";
		draft.Value.IsArchived = true;
		draft.Value.Entries.RemoveAt(0);
		draft.AddSongs([song]);
		draft.SetEntrySettings(app.Database!, draft.Value.Entries[0].Id, null, -4, null);
		draft.RequestDeletion();
		bool left = false;
		await EditorExit.RequestAsync(
			draft.HasChanges,
			() => Task.FromResult(EditorExitChoice.DiscardChanges),
			() => app.SaveSetlistEditAsync(draft, device, token),
			() =>
			{
				left = true;
				return Task.CompletedTask;
			},
			token);
		left.ShouldBeTrue();
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		app.GetSetlistEdit(list).HasChanges.ShouldBeFalse();
		await app.SaveSetlistEditAsync(app.GetSetlistEdit(list), device, token);
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task StaleSaveRetainsDraftAndExternalCatalogAndCancellationDoesNotWrite()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Stale", device, token);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location, token);
		Guid list = await app.CreateSetlistAsync("Original", device, token);
		SetlistEdit draft = app.GetSetlistEdit(list);
		draft.Value.Name = "Keep this input";
		string before = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase external = DatabaseJson.Deserialize(before);
		external.Setlists.Single().Name = "External change";
		string changed = await store.CommitMetadataAsync(location, before, external, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => app.SaveSetlistEditAsync(draft, device, token));
		draft.Value.Name.ShouldBe("Keep this input");
		draft.HasChanges.ShouldBeTrue();
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(changed);
		await Should.ThrowAsync<OperationCanceledException>(() => app.SaveSetlistEditAsync(draft, device, new(true)));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(changed);
	}

	[TestMethod]
	public async Task ArchiveAndDeletionCommitTogetherOnlyOnSaveAndRecordTombstone()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Delete", device, token);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location, token);
		Guid list = await app.CreateSetlistAsync("Delete me", device, token);
		SetlistEdit draft = app.GetSetlistEdit(list);
		Should.Throw<InvalidOperationException>(draft.RequestDeletion);
		draft.Value.IsArchived = true;
		draft.RequestDeletion();
		app.Database!.Setlists.Count.ShouldBe(1);
		app.Database.Tombstones.ShouldBeEmpty();
		await app.SaveSetlistEditAsync(draft, device, token);
		app.Database.Setlists.ShouldBeEmpty();
		app.Database.Tombstones.Single().EntityId.ShouldBe(list);
	}

	[TestMethod]
	public async Task EntryValidationRejectsInvalidPreferencesAndStaleDrafts()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Validation", device, token);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location, token);
		Guid song = await app.CreateSongAsync("Song", [], [], "[C]Song", device, token);
		Guid list = await app.CreateSetlistAsync("Original", device, token);
		Guid entry = await app.AddSongToSetlistAsync(list, song, device, token);
		SetlistEdit draft = app.GetSetlistEdit(list);
		Should.Throw<ArgumentOutOfRangeException>(() => draft.SetEntrySettings(app.Database!, entry, null, 25, null));
		Should.Throw<ArgumentException>(() => draft.SetEntrySettings(app.Database!, entry, Guid.NewGuid(), null, null));
		Should.Throw<ArgumentException>(() => draft.SetEntrySettings(app.Database!, entry, null, null, Guid.NewGuid()));
		Should.Throw<ArgumentException>(() => draft.SetOrder([entry, entry]));
		draft.HasChanges.ShouldBeFalse();
		draft.Value.Name = "Draft";
		await app.RenameSetlistAsync(list, "Newer", device, token);
		await Should.ThrowAsync<InvalidOperationException>(() => app.SaveSetlistEditAsync(draft, device, token));
		app.Database!.Setlists.Single().Name.ShouldBe("Newer");
	}

	[TestMethod]
	public async Task TenThousandOccurrencesSortAndSaveWithoutLosingEntryIdentity()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Scale", device, token);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location, token);
		Guid zulu = await app.CreateSongAsync("Zulu", [], [], "[C]Zulu", device, token);
		Guid alpha = await app.CreateSongAsync("Alpha", [], [], "[G]Alpha", device, token);
		Guid list = await app.CreateSetlistAsync("Large setlist", device, token);
		const int Count = 10000;
		IReadOnlyList<Guid> ids = await app.AddSongsToSetlistAsync(
			list,
			[.. Enumerable.Range(0, Count).Select(index => index % 2 == 0 ? zulu : alpha)],
			device,
			token);
		SetlistEdit draft = app.GetSetlistEdit(list);
		Stopwatch timer = Stopwatch.StartNew();
		draft.SortByName(new Dictionary<Guid, string> { [zulu] = "Zulu", [alpha] = "Alpha" });
		long sorting = timer.ElapsedMilliseconds;
		await app.SaveSetlistEditAsync(draft, device, token);
		long total = timer.ElapsedMilliseconds;
		Setlist saved = app.Database!.Setlists.Single();
		saved.Entries.Select(entry => entry.Id).ToHashSet().SetEquals(ids).ShouldBeTrue();
		saved.Entries.Take(Count / 2).ShouldAllBe(entry => entry.SongId == alpha);
		this.TestContext.WriteLine($"10,000-entry setlist: sort {sorting} ms; sort/save {total} ms; all occurrence identities retained.");
	}

	#endregion
}
