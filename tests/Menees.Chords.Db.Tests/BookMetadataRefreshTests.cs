#region Using Directives

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookMetadataRefreshTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RefreshBackfillsOldImportsOnlyOnce()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Old Imports", deviceId, cancellationToken);
		const string Text = "{title: 9 To 5}\r\n{artist: Dolly Parton}\r\n{key: F#}\r\n{tempo: 102}\r\n{capo: 4}\r\n{tag: Practice}\r\n\r\n[D]Tumble outta bed";
		BookImportResult imported = await BookImportService.ImportAsync(
			store,
			location,
			"9 To 5.cho",
			new MemoryStream(Encoding.UTF8.GetBytes(Text), writable: false),
			deviceId,
			cancellationToken);
		imported.Analysis.Artists.ShouldBe(["Dolly Parton"]);
		ChordDatabase oldDatabase = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		oldDatabase.Songs.Single().Artists.Clear();
		oldDatabase.Songs.Single().SourceMetadata.Clear();
		oldDatabase.SongFiles.Single().AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(oldDatabase), cancellationToken);
			await write.CommitAsync(cancellationToken);
		}

		BookMetadataRefreshResult first = await BookMetadataRefresh.RefreshAsync(store, location, deviceId, cancellationToken);
		ChordDatabase refreshed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		Song song = refreshed.Songs.Single();
		first.AnalyzedFileCount.ShouldBe(1);
		first.UpdatedSongCount.ShouldBe(1);
		first.Problems.ShouldBeEmpty();
		song.Artists.ShouldBe(["Dolly Parton"]);
		song.SourceMetadata["key"].Single().Value.ShouldBe("F#");
		song.SourceMetadata["tempo"].Single().Value.ShouldBe("102");
		song.SourceMetadata["capo"].Single().Value.ShouldBe("4");
		song.SourceMetadata["tag"].Single().Value.ShouldBe("Practice");
		refreshed.SongFiles.Single().AnalysisVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
		using Stream source = await store.OpenManagedAssetAsync(location, imported.SongFileId, cancellationToken);
		using StreamReader reader = new(source, Encoding.UTF8);
		(await reader.ReadToEndAsync(cancellationToken)).ShouldBe(Text);

		long revision = refreshed.Revision.Revision;
		BookMetadataRefreshResult second = await BookMetadataRefresh.RefreshAsync(store, location, deviceId, cancellationToken);
		second.AnalyzedFileCount.ShouldBe(0);
		second.UpdatedSongCount.ShouldBe(0);
		second.Problems.ShouldBeEmpty();
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken))
			.Revision.Revision.ShouldBe(revision);
	}

	[TestMethod]
	[DataRow("legacy", false)]
	[DataRow("legacy", true)]
	[DataRow("binary", false)]
	[DataRow("binary", true)]
	[DataRow("encoding", false)]
	[DataRow("encoding", true)]
	public async Task RefreshRetainsRejectedSourcesAndContinues(string kind, bool includeHealthy)
	{
		CancellationToken token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Legacy metadata", device, token);
		BookImportResult rejected = await BookImportService.CreateSongAsync(
			store, location, "Retained title", ["Retained artist"], [], "{title: Retained title}\n[C]Original source", device, token);
		if (includeHealthy)
		{
			_ = await BookImportService.CreateSongAsync(
				store, location, "Healthy", [], [], "{title: Healthy}\n{tag: Practice}\n[G]Healthy source", device, token);
		}

		byte[] bytes = kind switch
		{
			"legacy" => Encoding.Latin1.GetBytes("{title: Retained title}\n[C]Older text \u0001\u0099"),
			"binary" => [0, 1, 2, 3],
			_ => [0xff, 0xfe, 0x00, 0xd8], // Invalid final UTF-16 surrogate.
		};
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		foreach (SongFile file in database.SongFiles)
		{
			file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		}

		SongFile bad = database.SongFiles.Single(file => file.Id == rejected.SongFileId);
		bad.ContentHash = SongFileAnalyzer.Hash(bytes);
		bad.ObservedLength = bytes.Length;
		bad.DisplayPriority = 1;
		byte[] alternativeBytes = Encoding.UTF8.GetBytes("{title: Do not substitute this title}\n{tag: Alternative}\n[G]Alternative sheet");
		SongFile? alternative = includeHealthy ? new()
		{
			Id = Guid.CreateVersion7(), SongId = bad.SongId, RelativePath = "Alternative.cho", MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.ChordPro, TextEncoding = "utf-8", ContentHash = SongFileAnalyzer.Hash(alternativeBytes),
			ObservedLength = alternativeBytes.Length, AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1,
			Revision = bad.Revision,
		}
		: null;
		if (alternative is not null)
		{
			database.SongFiles.Add(alternative);
		}

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, token))
		{
			await write.WriteManagedAssetAsync(bad.Id, bad.RelativePath, new MemoryStream(bytes, false), token);
			if (alternative is not null)
			{
				await write.WriteManagedAssetAsync(alternative.Id, alternative.RelativePath, new MemoryStream(alternativeBytes, false), token);
			}

			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
			await write.CommitAsync(token);
		}

		string before = await store.ReadDatabaseJsonAsync(location, token);
		string oldSong = JsonSerializer.Serialize(database.Songs.Single(song => song.Id == rejected.SongId));
		BookMetadataRefreshResult result = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		result.Problems.Count.ShouldBe(1);
		result.Problems[0].RelativePath.ShouldBe(bad.RelativePath);
		result.Problems[0].Message.ShouldNotBeNullOrWhiteSpace();
		result.AnalyzedFileCount.ShouldBe(includeHealthy ? 2 : 0);
		result.UpdatedSongCount.ShouldBe(includeHealthy ? 1 : 0);
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		JsonSerializer.Serialize(database.Songs.Single(song => song.Id == rejected.SongId)).ShouldBe(oldSong);
		SongFile checkedFile = database.SongFiles.Single(file => file.Id == rejected.SongFileId);
		checkedFile.AnalysisFailure.ShouldNotBeNull();
		checkedFile.AnalysisFailure.AnalyzerVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
		checkedFile.AnalysisFailure.ContentHash.ShouldBe(bad.ContentHash);
		checkedFile.AnalysisFailure.Message.ShouldBe(result.Problems[0].Message);
		bad.AnalysisFailure = checkedFile.AnalysisFailure;
		bad.Revision = checkedFile.Revision;
		JsonSerializer.Serialize(checkedFile).ShouldBe(JsonSerializer.Serialize(bad));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldNotBe(before);

		using Stream source = await store.OpenManagedAssetAsync(location, rejected.SongFileId, token);
		using MemoryStream copy = new();
		await source.CopyToAsync(copy, token);
		copy.ToArray().ShouldBe(bytes);
		string refreshed = await store.ReadDatabaseJsonAsync(location, token);
		BookMetadataRefreshResult repeated = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		repeated.AnalyzedFileCount.ShouldBe(0);
		repeated.Problems.Count.ShouldBe(1);
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(refreshed);
		using CancellationTokenSource canceled = new();
		canceled.Cancel();
		await Should.ThrowAsync<OperationCanceledException>(() => BookMetadataRefresh.RefreshAsync(store, location, device, canceled.Token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(refreshed);
	}

	[TestMethod]
	public async Task FailedCheckIsRetriedForNewAnalyzerVersionOrDifferentContentHash()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Check invalidation", device, token);
		_ = await BookImportService.CreateSongAsync(store, location, "Legacy", [], [], "[C]Original", device, token);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		SongFile file = database.SongFiles.Single();
		file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		byte[] bytes = Encoding.UTF8.GetBytes("[C]Rejected\u0001");
		file.ContentHash = SongFileAnalyzer.Hash(bytes);
		file.ObservedLength = bytes.Length;
		file.AnalysisFailure = new(SongFileAnalyzer.CurrentAnalysisVersion - 1, file.ContentHash, "Earlier analyzer failure");
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, token))
		{
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(bytes, false), token);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
			await write.CommitAsync(token);
		}

		BookMetadataRefreshResult checkedAgain = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		checkedAgain.Problems.Single().Message.ShouldContain("U+0001");
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		file = database.SongFiles.Single();
		file.AnalysisFailure!.AnalyzerVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
		string failedHash = file.AnalysisFailure.ContentHash;
		bytes = Encoding.UTF8.GetBytes("[C]Repaired source");
		file.ContentHash = SongFileAnalyzer.Hash(bytes);
		file.ObservedLength = bytes.Length;
		file.ContentHash.ShouldNotBe(failedHash);
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, token))
		{
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(bytes, false), token);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
			await write.CommitAsync(token);
		}

		BookMetadataRefreshResult repaired = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		repaired.AnalyzedFileCount.ShouldBe(1);
		repaired.Problems.ShouldBeEmpty();
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		database.SongFiles.Single().AnalysisFailure.ShouldBeNull();
		database.SongFiles.Single().AnalysisVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
	}

	[TestMethod]
	public async Task OlderRejectedC1CheckIsUpgradedOnceWithoutRewritingSource()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation book = await store.CreateBookAsync("Old C1 check", device, token);
		_ = await BookImportService.CreateSongAsync(store, book, "Legacy", [], [], "[C]couldn\u00e2\u0080\u0099t", device, token);
		string before = await store.ReadDatabaseJsonAsync(book, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		SongFile file = database.SongFiles.Single();
		string hash = file.ContentHash;
		file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		file.AnalysisFailure = new(SongFileAnalyzer.CurrentAnalysisVersion - 1, hash, "Old C1 rejection");
		await store.CommitMetadataAsync(book, before, database, token);
		BookMetadataRefreshResult result = await BookMetadataRefresh.RefreshAsync(store, book, device, token);
		result.Problems.ShouldBeEmpty();
		result.AnalyzedFileCount.ShouldBe(1);
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(book, token));
		database.SongFiles.Single().AnalysisFailure.ShouldBeNull();
		database.SongFiles.Single().ContentHash.ShouldBe(hash);
		database.SongFiles.Single().AnalysisVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
		before = await store.ReadDatabaseJsonAsync(book, token);
		(await BookMetadataRefresh.RefreshAsync(store, book, device, token)).AnalyzedFileCount.ShouldBe(0);
		(await store.ReadDatabaseJsonAsync(book, token)).ShouldBe(before);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task ReadFailureCheckIsScopedToItsDevice(bool sameDevice)
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Device-scoped check", device, token);
		_ = await BookImportService.CreateSongAsync(store, location, "Readable", [], [], "[C]Readable source", device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		SongFile file = database.SongFiles.Single();
		file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		file.AnalysisFailure = new(SongFileAnalyzer.CurrentAnalysisVersion, file.ContentHash, "Earlier read failure")
		{
			DeviceId = sameDevice ? device : Guid.NewGuid(),
		};
		await store.CommitMetadataAsync(location, before, DatabaseJson.Serialize(database), token);
		before = await store.ReadDatabaseJsonAsync(location, token);
		BookMetadataRefreshResult result = await BookMetadataRefresh.RefreshAsync(store, location, device, token);
		result.AnalyzedFileCount.ShouldBe(sameDevice ? 0 : 1);
		result.Problems.Count.ShouldBe(sameDevice ? 1 : 0);
		if (sameDevice)
		{
			(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		}
		else
		{
			database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
			database.SongFiles.Single().AnalysisFailure.ShouldBeNull();
		}
	}

	#endregion
}
