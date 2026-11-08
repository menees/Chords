#region Using Directives

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SheetDuplicationTests
{
	#region Private Data

	private readonly string root = Path.Combine(Path.GetTempPath(), "ChordDuplicateTests-" + Guid.NewGuid().ToString("N"));
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
	public async Task UnchangedSaveDoesNotReadCurrentSheetOrManualCopy(bool withDuplicate)
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		Guid activeId = edit.FileId!.Value;
		Guid? copyId = withDuplicate ? await this.session.DuplicateSongFileAsync(this.songId, activeId, this.device, token) : null;
		var before = this.session.Database!.SongFiles.Select(file => (file.Id, file.ContentHash, file.ContentRevision, file.Revision.Revision)).ToArray();
		SongFile active = this.session.Database!.SongFiles.Single(file => file.Id == activeId);
		SongFile? copy = this.session.Database.SongFiles.SingleOrDefault(file => file.Id == copyId);
		using FileStream lockedActive = File.Open(this.PathFor(active), FileMode.Open, FileAccess.Read, FileShare.None);
		using FileStream? lockedCopy = copy is null ? null : File.Open(this.PathFor(copy), FileMode.Open, FileAccess.Read, FileShare.None);

		await this.SaveAsync(edit, edit.Text!, token);

		this.session.Database!.SongFiles.Select(file => (file.Id, file.ContentHash, file.ContentRevision, file.Revision.Revision)).ShouldBe(before);
		this.session.Database.Tombstones.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task ManualDuplicateKeepsExactBytesAndDefaultAcrossEditsReloadAndBackup()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongEditDocument original = await this.session.GetSongEditAsync(this.songId, token);
		Guid sourceId = original.FileId!.Value;
		Guid copyId = await this.session.DuplicateSongFileAsync(this.songId, sourceId, this.device, token);
		copyId.ShouldNotBe(sourceId);
		await this.SaveAsync(original, "Second\r\n[D]Changed\n", token);
		await this.SaveAsync(await this.session.GetSongEditAsync(this.songId, token), "Third\n[E]Changed\r\n", token);
		await this.session.ReloadAsync(token);

		SongFile copy = this.session.Database!.SongFiles.Single(file => file.Id == copyId);
		copy.IsArchived.ShouldBeFalse();
		copy.SavedVersion.ShouldBeNull();
		copy.RecoveryVersion.ShouldBeNull();
		copy.RelativePath.ShouldStartWith("Copy of First ");
		this.session.Database.SongFiles.Count.ShouldBe(2);
		this.session.Database.Tombstones.ShouldBeEmpty();
		this.session.GetSongFiles(this.songId).Single(file => file.IsDefault).Id.ShouldBe(sourceId);
		(await File.ReadAllBytesAsync(this.PathFor(copy), token)).ShouldBe(this.authored);

		SongVersionComparison captured = await this.session.CompareSongVersionsAsync(this.songId, sourceId, copyId, token);
		using (FileStream lockedCopy = File.Open(this.PathFor(copy), FileMode.Open, FileAccess.Read, FileShare.None))
		{
			SongVersionComparison ordered = captured.WithChronologicalOrder();
			ordered.Left.Id.ShouldBe(copyId);
			ordered.Left.Text.ShouldBe(original.Text);
			ordered.Right.Id.ShouldBe(sourceId);
			ordered.WithChronologicalOrder().ShouldBeSameAs(ordered);
		}

		using MemoryStream backup = new();
		await BookBackup.CreateAsync(this.store, this.location, backup, token);
		backup.Position = 0;
		using ZipArchive archive = new(backup, ZipArchiveMode.Read);
		using Stream retained = archive.GetEntry(copy.RelativePath)!.Open();
		using MemoryStream saved = new();
		await retained.CopyToAsync(saved, token);
		saved.ToArray().ShouldBe(this.authored);
	}

	[TestMethod]
	public async Task FailedDuplicateAndSaveLeaveExistingFilesAndCatalogUnchanged()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongEditDocument edit = await this.session.GetSongEditAsync(this.songId, token);
		SongFile file = this.session.Database!.SongFiles.Single();
		string before = await this.store.ReadDatabaseJsonAsync(this.location, token);
		await File.WriteAllTextAsync(this.PathFor(file), "External edit retained", token);

		await Should.ThrowAsync<BookStoreException>(
			() => this.session.DuplicateSongFileAsync(this.songId, file.Id, this.device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => this.SaveAsync(edit, "Edited", token));

		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(before);
		this.session.Database!.SongFiles.Single().Id.ShouldBe(file.Id);
		(await File.ReadAllTextAsync(this.PathFor(file), token)).ShouldBe("External edit retained");
	}

	[TestMethod]
	public async Task CatalogWithOffsetlessDatesIsRewrittenOnceWithoutReadingSheets()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		ChordDatabase database = this.session.Database!;
		string path = Path.Combine(this.store.GetDirectory(this.location), "database.json");
		var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path, token))!;
		node["schemaVersion"] = 1;
		node["songs"]![0]!["lastAccessedUtc"] = "2026-07-15T09:30:00";
		await File.WriteAllTextAsync(path, node.ToJsonString(), token);
		using FileStream lockedSource = File.Open(this.PathFor(database.SongFiles.Single()), FileMode.Open, FileAccess.Read, FileShare.None);

		BookMetadataRefreshResult first = await BookMetadataRefresh.RefreshAsync(this.store, this.location, this.device, token);
		first.AnalyzedFileCount.ShouldBe(0);
		string upgraded = await File.ReadAllTextAsync(path, token);
		DateTime wallTime = new(2026, 7, 15, 9, 30, 0, DateTimeKind.Unspecified);
		DateTimeOffset expected = new(wallTime, TimeZoneInfo.Local.GetUtcOffset(wallTime));
		upgraded.ShouldContain(JsonSerializer.Serialize(expected));
		await BookMetadataRefresh.RefreshAsync(this.store, this.location, this.device, token);
		(await File.ReadAllTextAsync(path, token)).ShouldBe(upgraded);
		DatabaseJson.Deserialize(upgraded).Revision.Revision.ShouldBe(database.Revision.Revision);
	}

	[TestMethod]
	public async Task OpeningLegacyBookRetiresOnlyAutomaticCopiesAndClearsTheirPreferencesOnce()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		SongFile source = this.session.Database!.SongFiles.Single();
		Guid legacyId = await this.session.DuplicateSongFileAsync(this.songId, source.Id, this.device, token);
		Guid manualId = await this.session.DuplicateSongFileAsync(this.songId, source.Id, this.device, token);
		Guid recoveryId = await this.session.DuplicateSongFileAsync(this.songId, source.Id, this.device, token);
		Guid listId = await this.session.CreateSetlistAsync("Practice", this.device, token);
		await this.session.AddSongToSetlistAsync(listId, this.songId, this.device, token);
		string expected = await this.store.ReadDatabaseJsonAsync(this.location, token);
		ChordDatabase database = this.session.Database;
		SongFile legacy = database.SongFiles.Single(file => file.Id == legacyId);
		legacy.IsArchived = true;
		legacy.SavedVersion = new() { SourceFileId = source.Id };
		database.SongFiles.Single(file => file.Id == manualId).IsArchived = true;
		SongFile recovery = database.SongFiles.Single(file => file.Id == recoveryId);
		recovery.IsArchived = true;
		recovery.RecoveryVersion = new() { OriginalConflictingFileId = source.Id, WinningFileId = source.Id };
		database.Setlists.Single().Entries.Single().PreferredSongFileId = legacy.Id;
		await this.store.CommitMetadataAsync(this.location, expected, database, token);
		using FileStream lockedSource = File.Open(this.PathFor(source), FileMode.Open, FileAccess.Read, FileShare.None);

		await BookMetadataRefresh.RefreshAsync(this.store, this.location, this.device, token);
		string migrated = await this.store.ReadDatabaseJsonAsync(this.location, token);
		await BookMetadataRefresh.RefreshAsync(this.store, this.location, this.device, token);
		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(migrated);
		database = DatabaseJson.Deserialize(migrated);
		database.SongFiles.Select(file => file.Id).ShouldBe([source.Id, manualId, recoveryId]);
		database.Setlists.Single().Entries.Single().PreferredSongFileId.ShouldBeNull();
		database.Tombstones.Single().EntityId.ShouldBe(legacyId);
		File.Exists(this.PathFor(legacy)).ShouldBeFalse();
		File.Exists(this.PathFor(recovery)).ShouldBeTrue();
	}

	#endregion

	#region Private Methods

	private async Task InitializeAsync(CancellationToken token)
	{
		this.store = new(this.root);
		this.location = await this.store.CreateBookAsync("Duplicate fixture", this.device, token);
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
