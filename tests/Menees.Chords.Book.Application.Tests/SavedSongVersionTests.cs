#region Using Directives

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SavedSongVersionTests
{
	#region Private Data

	private readonly string root = Path.Combine(Path.GetTempPath(), "ChordSavedTests-" + Guid.NewGuid().ToString("N"));
	private readonly Guid device = Guid.NewGuid();
	private FileSystemBookStore store = null!;
	private BookLocation location = null!;
	private BookApplicationSession session = null!;
	private Guid songId;
	private byte[] authored = [];

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestCleanup]
	public void Cleanup()
	{
		this.store?.Dispose();
		if (Directory.Exists(this.root))
		{
			Directory.Delete(this.root, recursive: true);
		}
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task UnchangedSourceSaveDoesNotReadRewriteOrRotateHistory(bool hasPreviousSave)
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		if (hasPreviousSave)
		{
			await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "[C]Actually edited", token);
		}

		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		SongFile[] before = [.. this.session.Database!.SongFiles];
		var stamps = before.Select(file => (file.Id, file.ContentHash, file.ContentRevision, file.Revision.Revision)).ToArray();
		SongFile active = before.Single(file => !file.IsArchived);
		SongFile? previous = before.SingleOrDefault(file => file.IsArchived);
		using FileStream lockedActive = File.Open(this.PathFor(active), FileMode.Open, FileAccess.Read, FileShare.None);
		using FileStream? lockedPrevious = previous is null ? null : File.Open(this.PathFor(previous), FileMode.Open, FileAccess.Read, FileShare.None);

		await this.SaveAsync(edit, edit.Text!, token);

		this.session.Database!.SongFiles.Select(file => (file.Id, file.ContentHash, file.ContentRevision, file.Revision.Revision)).ShouldBe(stamps);
		this.session.Database.Tombstones.ShouldBeEmpty();
		this.session.GetSongFiles(this.songId).Count(file => file.IsPreviousSave).ShouldBe(hasPreviousSave ? 1 : 0);
	}

	[TestMethod]
	public async Task AutomaticComparisonUsesOriginalEditTimeForPreviousSaveAndReusesCapturedSources()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongEditDocument original = await this.session.GetSongEditAsync(this.songId, token);
		await this.SaveAsync(original, "[D]New source", token);
		SongFile previous = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		SongVersionComparison captured = await this.session.CompareSongVersionsAsync(this.songId, original.FileId!.Value, previous.Id, token);
		SongFile active = this.session.Database.SongFiles.Single(file => !file.IsArchived);
		using FileStream lockedActive = File.Open(this.PathFor(active), FileMode.Open, FileAccess.Read, FileShare.None);
		using FileStream lockedPrevious = File.Open(this.PathFor(previous), FileMode.Open, FileAccess.Read, FileShare.None);

		SongVersionComparison ordered = captured.WithChronologicalOrder();

		ordered.Left.Id.ShouldBe(previous.Id);
		ordered.Left.ModifiedUtc.ShouldBe(previous.SavedVersion!.OriginalModifiedUtc);
		ordered.Left.Text.ShouldBe(original.Text);
		ordered.Right.Id.ShouldBe(original.FileId.Value);
		ordered.Right.Text.ShouldBe("[D]New source");
		ordered.Left.ModifiedUtc.ShouldBeLessThanOrEqualTo(ordered.Right.ModifiedUtc);
		ordered.BookId.ShouldBe(captured.BookId);
		ordered.SongId.ShouldBe(captured.SongId);
		ordered.CanResolve.ShouldBe(captured.CanResolve);
		ordered.WithChronologicalOrder().ShouldBeSameAs(ordered);
	}

	[TestMethod]
	public async Task RepeatedSavesRetainOnlyPreviousExactBytesAndBackupIncludesThem()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongEditDocument initial = await this.session.GetSongEditAsync(this.songId, token);
		await this.SaveAsync(initial, "Second\r\n[D]Café\n", token);
		SongFile firstCopy = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		(await File.ReadAllBytesAsync(this.PathFor(firstCopy), token)).ShouldBe(this.authored);
		firstCopy.SavedVersion!.SourceFileId.ShouldBe(initial.FileId!.Value);
		SongVersionComparison compare = await this.session.CompareSongVersionsAsync(this.songId, initial.FileId.Value, firstCopy.Id, token);
		compare.Right.SavedUtc.ShouldNotBeNull();
		compare.Right.Text.ShouldBe(initial.Text);
		SongEditDocument second = await this.session.GetSongEditAsync(this.songId, token);
		SongFile active = this.session.Database.SongFiles.Single(file => !file.IsArchived);
		byte[] secondBytes = await File.ReadAllBytesAsync(this.PathFor(active), token);
		await this.SaveAsync(second, "Third\n[E]Changed\r\n", token);
		SongFile latestCopy = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		latestCopy.Id.ShouldNotBe(firstCopy.Id);
		(await File.ReadAllBytesAsync(this.PathFor(latestCopy), token)).ShouldBe(secondBytes);
		File.Exists(this.PathFor(firstCopy)).ShouldBeFalse();
		this.session.Database.Tombstones.Single().EntityId.ShouldBe(firstCopy.Id);
		this.session.GetSongFiles(this.songId).Single(file => file.IsDefault).Id.ShouldBe(initial.FileId.Value);

		using MemoryStream backup = new();
		await BookBackup.CreateAsync(this.store, this.location, backup, token);
		backup.Position = 0;
		using ZipArchive archive = new(backup, ZipArchiveMode.Read);
		using Stream retained = archive.GetEntry(latestCopy.RelativePath)!.Open();
		using MemoryStream saved = new();
		await retained.CopyToAsync(saved, token);
		saved.ToArray().ShouldBe(secondBytes);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task FailedSaveRetainsPriorCatalogAndBothExistingVersions(bool editPrevious)
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "Second\n", token);
		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		SongFile modified = this.session.Database!.SongFiles.Single(file => file.IsArchived == editPrevious);
		string before = await this.store.ReadDatabaseJsonAsync(this.location, token);
		await File.WriteAllTextAsync(this.PathFor(modified), "External edit retained", token);
		if (editPrevious)
		{
			await Should.ThrowAsync<BookStoreException>(() => this.SaveAsync(edit, "Third", token));
		}
		else
		{
			await Should.ThrowAsync<InvalidOperationException>(() => this.SaveAsync(edit, "Third", token));
		}

		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(before);
		this.session.Database!.SongFiles.Count.ShouldBe(2);
		(await File.ReadAllTextAsync(this.PathFor(modified), token)).ShouldBe("External edit retained");
	}

	[TestMethod]
	public async Task PreviousSaveHasDistinctSummaryAndSheetKindAfterSaveReloadAndRestore()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "[C]Edited text", token);
		SongFileCatalogItem previous = this.session.GetSongFiles(this.songId).Single(file => file.IsArchived);
		previous.IsPreviousSave.ShouldBeTrue();
		previous.IsRecoveryVersion.ShouldBeFalse();
		SongCatalogItem row = this.session.Search(null).Single();
		row.PreviousSaveCount.ShouldBe(1);
		row.RecoveryFileCount.ShouldBe(0);
		row.ArchivedFileCount.ShouldBe(1);
		row.DisplayText.ShouldContain("1 previous save");
		row.DisplayText.ShouldNotContain("archived");
		row.DisplayText.ShouldNotContain("recovery");
		this.session.Search("previoussave:true recovery:false archivedfiles:true").Single().Id.ShouldBe(this.songId);
		this.session.Search("recovery:true").ShouldBeEmpty();
		await this.session.SaveSongMetronomeAsync(this.songId, new(), this.device, token);
		this.session.Search(null).Single().PreviousSaveCount.ShouldBe(1);
		this.session.Search(null).Single().DisplayText.ShouldContain("1 previous save");
		await this.session.ReloadAsync(token);
		this.session.Search("previoussave:true recovery:false").Single().PreviousSaveCount.ShouldBe(1);
		this.session.GetSongFiles(this.songId).Single(file => file.Id == previous.Id).IsPreviousSave.ShouldBeTrue();

		await this.session.SetSongFileArchivedAsync(this.songId, previous.Id, false, this.device, token);
		this.session.GetSongFiles(this.songId).Single(file => file.Id == previous.Id).IsPreviousSave.ShouldBeFalse();
		this.session.Search(null).Single().PreviousSaveCount.ShouldBe(0);
		this.session.Search("previoussave:true").ShouldBeEmpty();
	}

	[TestMethod]
	public async Task SyncRecoveryRemainsDistinctFromPreviousSaves()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "[C]Edited text", token);
		string expected = await this.store.ReadDatabaseJsonAsync(this.location, token);
		SongFile previous = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		previous.SavedVersion = null;
		previous.RecoveryVersion = new()
		{
			RecoveredFromSyncUtc = DateTimeOffset.UtcNow,
			OriginalConflictingFileId = previous.Id,
			WinningFileId = this.session.Database.SongFiles.Single(file => !file.IsArchived).Id,
		};
		await this.store.CommitMetadataAsync(this.location, expected, this.session.Database, token);
		await this.session.ReloadAsync(token);

		SongCatalogItem row = this.session.Search("recovery:true previoussave:false").Single();
		row.RecoveryFileCount.ShouldBe(1);
		row.PreviousSaveCount.ShouldBe(0);
		row.DisplayText.ShouldContain("1 recovery sheet");
		row.DisplayText.ShouldNotContain("previous save");
		SongFileCatalogItem recovery = this.session.GetSongFiles(this.songId).Single(file => file.Id == previous.Id);
		recovery.IsRecoveryVersion.ShouldBeTrue();
		recovery.IsPreviousSave.ShouldBeFalse();
	}

	[TestMethod]
	public async Task MetadataOnlySaveDoesNotReadPreviousOrCreateHistory()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "Second\n", token);
		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		SongFile previous = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		var sourceMetadata = this.session.Database.Songs.Single().SourceMetadata;
		List<SourceMetadataValue> observation = [new() { Value = "preserved", SourceName = "custom" }];
		sourceMetadata["custom"] = observation;
		using FileStream locked = File.Open(this.PathFor(previous), FileMode.Open, FileAccess.Read, FileShare.None);
		await this.session.SaveSongEditAsync(edit, "New catalog title", [], [], edit.Text, this.device, token);
		this.session.Database!.SongFiles.Count.ShouldBe(2);
		this.session.Database.SongFiles.Single(file => file.IsArchived).Id.ShouldBe(previous.Id);
		this.session.Database.Tombstones.ShouldBeEmpty();
		this.session.Database.Songs.Single().SourceMetadata.ShouldBeSameAs(sourceMetadata);
		sourceMetadata["custom"].ShouldBeSameAs(observation);
	}

	[TestMethod]
	public async Task ExplicitlyRestoredCopySurvivesNextSourceSave()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "Second\n", token);
		SongFile restored = this.session.Database!.SongFiles.Single(file => file.IsArchived);
		await this.session.SetSongFileArchivedAsync(this.songId, restored.Id, false, this.device, token);
		this.session.GetSongFiles(this.songId).Single(file => file.Id == restored.Id).IsRecoveryVersion.ShouldBeFalse();
		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		await this.SaveAsync(edit, "Third\n", token);
		this.session.Database!.SongFiles.Count.ShouldBe(3);
		this.session.Database.SongFiles.Single(file => file.Id == restored.Id).IsArchived.ShouldBeFalse();
		(await File.ReadAllBytesAsync(this.PathFor(restored), token)).ShouldBe(this.authored);
	}

	#endregion

	#region Private Methods

	private async Task InitializeAsync(CancellationToken token)
	{
		this.store = new(this.root);
		this.location = await this.store.CreateBookAsync("Saved fixture", this.device, token);
		this.authored = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("First\r\n[C]Café\n")];
		using MemoryStream source = new(this.authored);
		BookImportResult result = await BookImportService.ImportAsync(this.store, this.location, "Song.cho", source, this.device, token);
		this.songId = result.SongId;
		this.session = new();
		await this.session.ActivateAsync(this.store, this.location, token);
	}

	private Task SaveAsync(SongEditDocument edit, string text, CancellationToken token)
		=> this.session.SaveSongEditAsync(edit, edit.Title, edit.Artists, edit.Tags, text, this.device, token);

	private string PathFor(SongFile file) => Path.Combine(this.store.GetDirectory(this.location), file.RelativePath);

	#endregion
}
