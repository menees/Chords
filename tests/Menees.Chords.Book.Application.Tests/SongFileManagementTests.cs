#region Using Directives

using System.IO;
using System.Text;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongFileManagementTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task AttachOrderArchiveAndRenamePreserveRelatedAndUnrelatedContent()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(SongFileManagementTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			using FileSystemBookStore store = new(Path.Combine(root, "Books"));
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Sheets", device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			Guid songId = await session.CreateSongAsync("Catalog", ["Original artist"], ["Tag"], "[C]Original", device, token);
			SongFile original = session.Database!.SongFiles.Single();
			string originalPath = Path.Combine(store.GetDirectory(location), original.RelativePath);
			string importedPath = Path.Combine(root, "Alternative.cho");
			byte[] alternative = Encoding.UTF8.GetBytes("{title: Different title}\r\n{artist: Different artist}\r\n[G]Alternative\n");
			await File.WriteAllBytesAsync(importedPath, alternative, token);
			Guid addedId;
			using (FileStream lockedOriginal = File.Open(originalPath, FileMode.Open, FileAccess.Read, FileShare.None))
			{
				(await session.ImportSongFilesAsync(songId, [importedPath, importedPath], device, token)).ShouldBe(1);
				session.Database!.Songs.Count.ShouldBe(1);
				session.Database.Songs.Single().Title.ShouldBe("Catalog");
				session.Database.Songs.Single().Artists.ShouldBe(["Original artist"]);
				session.GetSongFiles(songId).Single(file => file.IsDefault).Id.ShouldBe(original.Id);
				addedId = session.Database.SongFiles.Single(file => file.Id != original.Id).Id;
				ChordDatabase identity = session.Database;
				await session.SetSongFilePositionAsync(songId, addedId, 1, device, token);
				session.Database.ShouldBeSameAs(identity);
				session.GetSongFiles(songId).Single(file => file.IsDefault).Id.ShouldBe(addedId);
				await session.SetSongFileArchivedAsync(songId, addedId, true, device, token);
				session.GetSongFiles(songId).Single(file => file.IsDefault).Id.ShouldBe(original.Id);
				session.Search(string.Empty).Single().ActiveFileCount.ShouldBe(1);
				await session.SetSongFileArchivedAsync(songId, addedId, false, device, token);
				await session.RenameSongFileAsync(songId, addedId, "Alternate arrangement", device, token);
				(await session.ImportSongFilesAsync(songId, [importedPath], device, token)).ShouldBe(0);
				string beforeFailure = await store.ReadDatabaseJsonAsync(location, token);
				await File.WriteAllTextAsync(importedPath, "[Am]Changed input", token);
				await Should.ThrowAsync<FileNotFoundException>(() => session.ImportSongFilesAsync(
					songId, [importedPath, Path.Combine(root, "Missing.cho")], device, token));
				(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(beforeFailure);
				session.Database.SongFiles.Count.ShouldBe(2);
			}

			await session.ActivateAsync(store, location, token);
			SongFile renamed = session.Database!.SongFiles.Single(file => file.Id == addedId);
			renamed.RelativePath.ShouldStartWith("Alternate arrangement");
			Path.GetExtension(renamed.RelativePath).ShouldBe(".cho");
			(await File.ReadAllBytesAsync(Path.Combine(store.GetDirectory(location), renamed.RelativePath), token)).ShouldBe(alternative);
			(await File.ReadAllBytesAsync(originalPath, token)).ShouldBe(Encoding.UTF8.GetBytes("[C]Original"));
			(await session.GetPresentationAsync(songId, cancellationToken: token)).SongFileId.ShouldBe(addedId);
			(await session.GetSongEditAsync(songId, token)).FileId.ShouldBe(addedId);
			SongEditDocument originalEdit = await session.GetSongFileEditAsync(songId, original.Id, token);
			originalEdit.Text.ShouldBe("[C]Original");
			await session.SaveSongEditAsync(originalEdit, originalEdit.Title, originalEdit.Artists, originalEdit.Tags, "[D]Edited original", device, token);
			(await session.GetPresentationAsync(songId, cancellationToken: token)).SongFileId.ShouldBe(addedId);
			(await File.ReadAllBytesAsync(Path.Combine(store.GetDirectory(location), renamed.RelativePath), token)).ShouldBe(alternative);
			(await session.GetSongFileEditAsync(songId, original.Id, token)).Text.ShouldBe("[D]Edited original");
			await Should.ThrowAsync<InvalidOperationException>(() => session.GetSongFileEditAsync(Guid.NewGuid(), original.Id, token));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task DeleteArchivedSheetRetainsSongAndClearsSetlistAndInstrumentPreferences()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Deletion", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid songId = await session.CreateSongAsync("Song", [], [], "[C]Song", device, token);
		Guid fileId = session.Database!.SongFiles.Single().Id;
		Guid listId = await session.CreateSetlistAsync("Set", device, token);
		await session.AddSongsToSetlistAsync(listId, [songId, songId], device, token);
		string expected = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(expected);
		Guid profileId = Guid.NewGuid();
		database.InstrumentProfiles.Add(new() { Id = profileId, Name = "Guitar", Revision = RevisionStamp.Initial(device) });
		database.SongInstrumentSettings.Add(new()
		{
			Id = Guid.NewGuid(), SongId = songId, InstrumentProfileId = profileId, PreferredSongFileId = fileId,
			Revision = RevisionStamp.Initial(device),
		});
		foreach (SetlistEntry entry in database.Setlists.Single().Entries)
		{
			entry.PreferredSongFileId = fileId;
		}

		await store.CommitMetadataAsync(location, expected, database, token);
		await session.ReloadAsync(token);
		await Should.ThrowAsync<InvalidOperationException>(() => session.DeleteArchivedSongFileAsync(songId, fileId, device, token));
		await session.SetSongFileArchivedAsync(songId, fileId, true, device, token);
		await session.DeleteArchivedSongFileAsync(songId, fileId, device, token);
		await session.ActivateAsync(store, location, token);
		session.Database!.Songs.Single().Id.ShouldBe(songId);
		session.Database.SongFiles.ShouldBeEmpty();
		session.Database.Setlists.Single().Entries.Count.ShouldBe(2);
		session.Database.Setlists.Single().Entries.ShouldAllBe(entry => entry.PreferredSongFileId == null);
		session.Database.SongInstrumentSettings.Single().PreferredSongFileId.ShouldBeNull();
		session.Database.Tombstones.Single().EntityId.ShouldBe(fileId);
		DatabaseValidation.Validate(session.Database).ShouldBeEmpty();
		(await session.GetPresentationAsync(songId, cancellationToken: token)).SongFileId.ShouldBeNull();
	}

	[TestMethod]
	public async Task InvalidSheetMutationsAndStaleRenameDoNotLoseData()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Concurrency", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid songId = await session.CreateSongAsync("Song", [], [], "[C]Song", device, token);
		Guid fileId = session.Database!.SongFiles.Single().Id;
		string expected = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<ArgumentOutOfRangeException>(() => session.SetSongFilePositionAsync(songId, fileId, 0, device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => session.SetSongFileArchivedAsync(Guid.NewGuid(), fileId, true, device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(expected);
		ChordDatabase external = DatabaseJson.Deserialize(expected);
		external.Name = "External";
		await store.CommitMetadataAsync(location, expected, external, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => session.RenameSongFileAsync(songId, fileId, "Stale", device, token));
		session.Database!.SongFiles.Single().RelativePath.ShouldBe(external.SongFiles.Single().RelativePath);
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token)).Name.ShouldBe("External");
	}

	#endregion
}
