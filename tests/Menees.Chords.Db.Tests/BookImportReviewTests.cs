#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookImportReviewTests
{
	#region Private Data

	private readonly string directory = Path.Combine(Path.GetTempPath(), "ChordImportReviewTests-" + Guid.NewGuid().ToString("N"));

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestInitialize]
	public void Initialize() => Directory.CreateDirectory(this.directory);

	[TestCleanup]
	public void Cleanup() => Directory.Delete(this.directory, recursive: true);

	[TestMethod]
	public async Task ReviewClassifiesContentVersionsMissingArtistsAndBatchDuplicatesWithoutWriting()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Review", device, token);
		string original = await this.WriteAsync("original.cho", "{title: Song}\r\n{artist: Artist}\r\n[C]Original");
		await BookImportService.ImportFileAsync(store, location, original, device, token);
		string oldPdf = await this.WriteAsync("Old.pdf", "%PDF-1.7\nsynthetic");
		await BookImportService.ImportFileAsync(store, location, oldPdf, device, token);
		string changed = await this.WriteAsync("changed.cho", "{title: song}\n{artist: artist}\n[D]Changed");
		string unknown = await this.WriteAsync("unknown.cho", "{title: Song}\n[E]Missing artist");
		string other = await this.WriteAsync("other.cho", "{title: Song}\n{artist: Other}\n[F]Different song");
		string renamed = await this.WriteAsync("Renamed.pdf", "%PDF-1.7\nsynthetic");
		string novel = await this.WriteAsync("new.cho", "{title: New}\n{artist: Artist}\n[C]New");
		string anotherVersion = await this.WriteAsync("new2.cho", "{title: New}\n{artist: Artist}\n[D]New version");
		string before = await store.ReadDatabaseJsonAsync(location, token);
		using BookImportReview review = await BookImportService.ReviewFilesAsync(
			store, location, [original, changed, other, unknown, renamed, novel, novel, anotherVersion], token);
		review.Items.Select(item => item.Conflict).ShouldBe([
			BookImportConflict.ExactDuplicate, BookImportConflict.PossibleVersion, BookImportConflict.NewSong,
			BookImportConflict.PossibleVersion, BookImportConflict.IdenticalContent, BookImportConflict.NewSong,
			BookImportConflict.ExactDuplicate, BookImportConflict.PossibleVersion]);
		review.Items.Where(item => item.Conflict != BookImportConflict.NewSong)
			.ShouldAllBe(item => item.DefaultDecision.Action == BookImportAction.Skip);
		review.Items[^1].Candidates.ShouldContain(candidate => candidate.IsIncoming && candidate.SongId == review.Items[5].Id);
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task AddingReviewedSheetPreservesBytesMetadataDefaultAndUnrelatedLockedAssets()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		using FileSystemBookStore store = new(Path.Combine(this.directory, "Books"));
		BookLocation location = await store.CreateBookAsync("Review", device, token);
		string original = await this.WriteAsync("original.cho", "{title: Song}\r\n{artist: Artist}\r\n{key: C}\r\n[C]Original");
		BookImportResult first = await BookImportService.ImportFileAsync(store, location, original, device, token);
		string changed = Path.Combine(this.directory, "changed.cho");
		byte[] incoming = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{title: Song}\r\n{artist: Artist}\r\n{key: D}\r\n[D]Changed\r\n")];
		await File.WriteAllBytesAsync(changed, incoming, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		database.Songs.Single().Tags = ["Keep"];
		database.Songs.Single().MetadataOverrides["key"] = ["G"];
		await store.CommitMetadataAsync(location, before, database, token);
		string asset = Path.Combine(store.GetDirectory(location), first.RelativePath);
		DateTime oldWrite = File.GetLastWriteTimeUtc(asset);
		using FileStream locked = new(asset, FileMode.Open, FileAccess.Read, FileShare.None);
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [changed], token);
		await File.WriteAllTextAsync(changed, "Original path changed after review", token);
		IReadOnlyList<BookImportResult> result = await BookImportService.ApplyReviewAsync(
			store, location, review, [new(review.Items[0].Id, BookImportAction.AddSheet, first.SongId)], device, token);
		result.Single().SongId.ShouldBe(first.SongId);
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		database.Songs.Count.ShouldBe(1);
		database.SongFiles.Count.ShouldBe(2);
		Song song = database.Songs.Single();
		song.Tags.ShouldBe(["Keep"]);
		SongMetadata.GetValues(song, "key").ShouldBe(["G"]);
		song.SourceMetadata["key"].Single().Value.ShouldBe("C");
		database.SongFiles.OrderByDescending(file => file.DisplayPriority).First().Id.ShouldBe(first.SongFileId);
		File.GetLastWriteTimeUtc(asset).ShouldBe(oldWrite);
		await using Stream saved = await store.OpenManagedAssetAsync(location, result[0].SongFileId, token);
		using MemoryStream copy = new();
		await saved.CopyToAsync(copy, token);
		copy.ToArray().ShouldBe(incoming);
		DatabaseValidation.Validate(database).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task IncomingVersionsCanAttachToAnEarlierReviewedSongAndRetrySkipsBoth()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Batch", device, token);
		string first = await this.WriteAsync("first.cho", "{title: Song}\n{artist: Artist}\n[C]First");
		string second = await this.WriteAsync("second.cho", "{title: Song}\n{artist: Artist}\n[D]Second");
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [first, second, first], token);
		IReadOnlyList<BookImportDecision> choices = [review.Items[0].DefaultDecision,
			new(review.Items[1].Id, BookImportAction.AddSheet, review.Items[0].Id), review.Items[2].DefaultDecision];
		IReadOnlyList<BookImportResult> results = await BookImportService.ApplyReviewAsync(store, location, review, choices, device, token);
		results.Count.ShouldBe(2);
		results.Select(item => item.SongId).Distinct().Count().ShouldBe(1);
		using BookImportReview retry = await BookImportService.ReviewFilesAsync(store, location, [first, second], token);
		retry.Items.ShouldAllBe(item => item.Conflict == BookImportConflict.ExactDuplicate);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		IReadOnlyList<BookImportDecision> retryChoices = [.. retry.Items.Select(item => item.DefaultDecision)];
		(await BookImportService.ApplyReviewAsync(store, location, retry, retryChoices, device, token)).ShouldBeEmpty();
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task InvalidTargetsStaleReviewsAndDisposedReviewsCannotMutateTheBook()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Review", device, token);
		string first = await this.WriteAsync("first.cho", "{title: Song}\n[C]First");
		string second = await this.WriteAsync("second.cho", "{title: Song}\n[D]Second");
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [first, second], token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		IReadOnlyList<BookImportDecision> choices = [new(review.Items[0].Id, BookImportAction.Skip),
			new(review.Items[1].Id, BookImportAction.AddSheet, review.Items[0].Id)];
		await Should.ThrowAsync<InvalidOperationException>(() => BookImportService.ApplyReviewAsync(store, location, review, choices, device, token));
		await Should.ThrowAsync<ArgumentException>(() => BookImportService.ApplyReviewAsync(store, location, review, [], device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		ChordDatabase changed = DatabaseJson.Deserialize(before);
		changed.Name = "Changed";
		await store.CommitMetadataAsync(location, before, changed, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => BookImportService.ApplyReviewAsync(
			store, location, review, [.. review.Items.Select(item => item.DefaultDecision)], device, token));
		string captured = review.GetPath(review.Items[0].Id);
		review.Dispose();
		File.Exists(captured).ShouldBeFalse();
		Should.Throw<ObjectDisposedException>(() => BookImportService.ApplyReviewAsync(store, location, review, [], device, token));
	}

	[TestMethod]
	public async Task DamagedLaterSnapshotRollsBackAnEarlierStagedAddition()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		using FileSystemBookStore store = new(Path.Combine(this.directory, "Books"));
		BookLocation location = await store.CreateBookAsync("Rollback", device, token);
		string first = await this.WriteAsync("first.cho", "{title: First}\n[C]First");
		string second = await this.WriteAsync("second.cho", "{title: Second}\n[D]Second");
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [first, second], token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		await File.WriteAllTextAsync(review.GetPath(review.Items[1].Id), "tampered", token);
		await Should.ThrowAsync<BookStoreValidationException>(() => BookImportService.ApplyReviewAsync(
			store, location, review, [.. review.Items.Select(item => item.DefaultDecision)], device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		Directory.EnumerateFiles(store.GetDirectory(location)).Select(Path.GetFileName).ShouldBe(["database.json"]);
	}

	[TestMethod]
	public async Task ArchivedTargetsAreReviewedButCannotReceiveSheetsAndExplicitSeparateImportWorks()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Archive", device, token);
		string source = await this.WriteAsync("first.cho", "{title: Song}\n[C]First");
		BookImportResult first = await BookImportService.ImportFileAsync(store, location, source, device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		database.Songs.Single().IsArchived = true;
		await store.CommitMetadataAsync(location, before, database, token);
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [source], token);
		review.Items[0].Candidates.Single().IsArchived.ShouldBeTrue();
		await Should.ThrowAsync<InvalidOperationException>(() => BookImportService.ApplyReviewAsync(
			store, location, review, [new(review.Items[0].Id, BookImportAction.AddSheet, first.SongId)], device, token));
		IReadOnlyList<BookImportDecision> choices = [new(review.Items[0].Id, BookImportAction.ImportNewSong)];
		(await BookImportService.ApplyReviewAsync(store, location, review, choices, device, token)).Count.ShouldBe(1);
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token)).Songs.Count.ShouldBe(2);
	}

	[TestMethod]
	public async Task RepresentativeRepeatImportSkipsFiveHundredWithoutOpeningRecordedAssets()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		using FileSystemBookStore store = new(Path.Combine(this.directory, "Books"));
		BookLocation location = await store.CreateBookAsync("Scale", device, token);
		const int Count = 500;
		List<string> paths = [];
		for (int index = 0; index < Count; index++)
		{
			paths.Add(await this.WriteAsync($"{index}.cho", $"{{title: Song {index}}}\n{{artist: Artist}}\n[C]Synthetic {index}"));
		}

		await BookImportService.ImportFilesAsync(store, location, paths, device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		SongFile file = DatabaseJson.Deserialize(before).SongFiles[0];
		using FileStream locked = new(Path.Combine(store.GetDirectory(location), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None);
		Stopwatch timer = Stopwatch.StartNew();
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, paths, token);
		review.Items.ShouldAllBe(item => item.Conflict == BookImportConflict.ExactDuplicate);
		IReadOnlyList<BookImportDecision> choices = [.. review.Items.Select(item => item.DefaultDecision)];
		(await BookImportService.ApplyReviewAsync(store, location, review, choices, device, token)).ShouldBeEmpty();
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		this.TestContext.WriteLine($"500-source repeat review and no-op apply: {timer.ElapsedMilliseconds} ms; recorded sheet remained exclusively locked.");
	}

	[TestMethod]
	public async Task EmptySongsRemainAmbiguousAndCanceledImportRetainsTheReviewedBook()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Empty songs", device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		Guid first = Guid.NewGuid();
		database.Songs.Add(new() { Id = first, Title = "Song", Artists = ["Artist"] });
		database.Songs.Add(new() { Id = Guid.NewGuid(), Title = "Song", Artists = ["Artist"] });
		await store.CommitMetadataAsync(location, before, database, token);
		string source = await this.WriteAsync("new.cho", "{title: Song}\n{artist: Artist}\n[C]New");
		using BookImportReview review = await BookImportService.ReviewFilesAsync(store, location, [source], token);
		BookImportItem item = review.Items.Single();
		item.Conflict.ShouldBe(BookImportConflict.PossibleVersion);
		item.DefaultDecision.Action.ShouldBe(BookImportAction.Skip);
		item.Candidates.Count.ShouldBe(2);
		item.Candidates.ShouldAllBe(candidate => !candidate.CanCompare);
		before = await store.ReadDatabaseJsonAsync(location, token);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		await Should.ThrowAsync<OperationCanceledException>(() => BookImportService.ApplyReviewAsync(
			store, location, review, [new(item.Id, BookImportAction.AddSheet, first)], device, cancellation.Token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		IReadOnlyList<BookImportDecision> choices = [new(item.Id, BookImportAction.AddSheet, first)];
		(await BookImportService.ApplyReviewAsync(store, location, review, choices, device, token)).Single().SongId.ShouldBe(first);
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token)).Songs.Count.ShouldBe(2);
	}
	#endregion

	#region Private Methods

	private async Task<string> WriteAsync(string name, string text)
	{
		string path = Path.Combine(this.directory, name);
		await File.WriteAllTextAsync(path, text, this.TestContext.CancellationToken);
		return path;
	}

	#endregion
}