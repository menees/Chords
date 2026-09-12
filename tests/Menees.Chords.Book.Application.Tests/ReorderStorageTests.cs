#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class ReorderStorageTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task LargeBookMetadataOperationsNeverOpenAssets()
	{
		const int SongCount = 531;
		const int ListCount = 28;
		const int EntryCount = 43;
		const int Repetitions = 20;
		string root = Path.Combine(Path.GetTempPath(), nameof(ReorderStorageTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			var token = this.TestContext.CancellationToken;
			BookLocation location = await store.CreateBookAsync("Benchmark", device, token);
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
			byte[] bytes = Encoding.UTF8.GetBytes("{title: Synthetic song}\n[C]Test\n");
			await using (IStagedBookWrite write = await store.StageWriteAsync(location, token))
			{
				for (int index = 0; index < SongCount; index++)
				{
					Song song = new() { Id = Guid.NewGuid(), Title = $"Song {index}", Revision = RevisionStamp.Initial(device) };
					Guid fileId = Guid.NewGuid();
					SongFile file = new()
					{
						Id = fileId,
						SongId = song.Id,
						RelativePath = PortableManagedFileName.Create(song.Title, fileId, ".cho"),
						ContentHash = SongFileAnalyzer.Hash(bytes),
						ContentRevision = 1,
						AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion,
						MediaKind = MediaKind.Text,
						SourceFormat = SourceFormat.ChordPro,
						Revision = RevisionStamp.Initial(device),
					};
					database.Songs.Add(song);
					database.SongFiles.Add(file);
					using MemoryStream content = new(bytes);
					await write.WriteManagedAssetAsync(file.Id, file.RelativePath, content, token);
				}

				for (int index = 0; index < ListCount; index++)
				{
					database.Setlists.Add(new Setlist
					{
						Id = Guid.NewGuid(),
						Name = $"List {index}",
						Revision = RevisionStamp.Initial(device),
						Entries = [.. Enumerable.Range(0, EntryCount).Select(position => new SetlistEntry
						{
							Id = Guid.NewGuid(), SongId = database.Songs[position].Id,
						})],
					});
				}

				await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
				await write.CommitAsync(token);
			}

			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			SongEditDocument edit = await session.GetSongEditAsync(database.Songs[0].Id, token);
			List<FileStream> locks = [];
			try
			{
				foreach (SongFile file in database.SongFiles)
				{
					locks.Add(File.Open(Path.Combine(store.GetDirectory(location), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None));
				}

				Guid list = database.Setlists[0].Id;
				Guid entry = database.Setlists[0].Entries[0].Id;
				List<double> elapsed = [];
				for (int index = 0; index < Repetitions; index++)
				{
					ChordDatabase identity = session.Database!;
					Setlist setlistIdentity = identity.Setlists[0];
					var entriesIdentity = setlistIdentity.Entries;
					long start = Stopwatch.GetTimestamp();
					await session.SetSetlistEntryPositionAsync(list, entry, index % 2 == 0 ? EntryCount : 1, device, token);
					elapsed.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
					session.Database.ShouldBeSameAs(identity);
					session.Database!.Setlists[0].ShouldBeSameAs(setlistIdentity);
					setlistIdentity.Entries.ShouldBeSameAs(entriesIdentity);
				}

				Guid[] reversed = [.. session.GetSetlistEntries(list).Select(item => item.EntryId).Reverse()];
				await session.SetSetlistOrderAsync(list, reversed, device, token);
				session.GetSetlistEntries(list).Select(item => item.EntryId).ShouldBe(reversed);
				await session.RenameAsync("Renamed", device, token);
				Guid customTab = await session.SaveCustomTabAsync(null, "Practice", "Song", "artist", device, token);
				await session.DeleteCustomTabAsync(customTab, device, token);
				await session.SaveSongEditAsync(edit, "Metadata edit", [], [], edit.Text, device, token);
				await VerifyDisplayAsync(session, edit.SongId, device, token);
				await session.SetSongsArchivedAsync([edit.SongId], true, device, token);
				ChordDatabase persisted = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
				persisted.Setlists[0].Entries.Select(item => item.Id).ShouldBe(reversed);
				persisted.Songs[0].IsArchived.ShouldBeTrue();
				persisted.Songs[0].Title.ShouldBe("Metadata edit");
				persisted.Name.ShouldBe("Renamed");
				Directory.GetDirectories(root).Length.ShouldBe(1);
				await this.VerifyIncrementalAssetOperationsAsync(store, location, device, token);
				elapsed.Sort();
				this.TestContext.WriteLine($"{SongCount} songs, {ListCount} setlists, {EntryCount} entries/list; {Repetitions} durable reorders:");
				this.TestContext.WriteLine($"Median {elapsed[Repetitions / 2]:F1} ms; maximum {elapsed[^1]:F1} ms. All song files exclusively locked.");
			}
			finally
			{
				foreach (FileStream stream in locks)
				{
					stream.Dispose();
				}
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	#endregion

	#region Private Methods

	private static async Task VerifyDisplayAsync(BookApplicationSession session, Guid songId, Guid device, CancellationToken token)
	{
		SongCatalogItem unrelated = session.Search(string.Empty).First(song => song.Id != songId);
		await session.SaveDisplaySettingsAsync(songId, new() { FontSize = 24 }, device, token);
		session.Search("display:true").Single().Id.ShouldBe(songId);
		session.Search(string.Empty).Single(song => song.Id == unrelated.Id).ShouldBeSameAs(unrelated);
	}

	private async Task VerifyIncrementalAssetOperationsAsync(FileSystemBookStore store, BookLocation location, Guid device, CancellationToken token)
	{
		long start = Stopwatch.GetTimestamp();
		using MemoryStream source = new(Encoding.UTF8.GetBytes("{title: Added song}\n[C]Added"));
		BookImportResult added = await BookImportService.ImportAsync(store, location, "Added.cho", source, device, token);
		this.TestContext.WriteLine($"Import with all prior assets locked: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms.");
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		SongEditDocument edit = await session.GetSongEditAsync(added.SongId, token);
		ChordDatabase identity = session.Database!;
		Song songIdentity = identity.Songs.Single(song => song.Id == added.SongId);
		start = Stopwatch.GetTimestamp();
		await session.SaveSongEditAsync(edit, edit.Title, [], [], edit.Text + "\n[G]Edited", device, token);
		session.Database.ShouldBeSameAs(identity);
		session.Database!.Songs.Single(song => song.Id == added.SongId).ShouldBeSameAs(songIdentity);
		this.TestContext.WriteLine($"Text edit with unrelated assets locked: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms.");
		string expected = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(expected);
		SongFile file = database.SongFiles.Single(item => item.Id == added.SongFileId);
		file.AnalysisVersion = 0;
		await store.CommitMetadataAsync(location, expected, database, token);
		BookMetadataRefreshResult refreshed = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		refreshed.AnalyzedFileCount.ShouldBe(1);
		(await BookMetadataRefresh.RefreshAsync(store, location, device, token)).AnalyzedFileCount.ShouldBe(0);
		expected = await store.ReadDatabaseJsonAsync(location, token);
		database = DatabaseJson.Deserialize(expected);
		file = database.SongFiles.Single(item => item.Id == added.SongFileId);
		string originalPath = file.RelativePath;
		file.RelativePath = PortableManagedFileName.Create("Renamed file", file.Id, ".cho");
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, expected, token))
		{
			await write.RenameManagedAssetAsync(file.Id, file.RelativePath, token);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
			await write.CommitAsync(token);
		}

		File.Exists(Path.Combine(store.GetDirectory(location), originalPath)).ShouldBeFalse();
		File.Exists(Path.Combine(store.GetDirectory(location), file.RelativePath)).ShouldBeTrue();
		int descriptors = 0;
		await foreach (ManagedAssetDescriptor descriptor in store.EnumerateManagedAssetsAsync(location, token))
		{
			descriptors++;
		}

		descriptors.ShouldBe(database.SongFiles.Count);
		await session.ReloadAsync(token);
		await session.SetSongsArchivedAsync([added.SongId], true, device, token);
		start = Stopwatch.GetTimestamp();
		await session.DeleteArchivedSongsAsync([added.SongId], device, token);
		this.TestContext.WriteLine($"Delete with unrelated assets locked: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms.");
		File.Exists(Path.Combine(store.GetDirectory(location), file.RelativePath)).ShouldBeFalse();
	}

	#endregion
}
