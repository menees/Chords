#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongVersionComparisonTests
{
	#region Private Data

	private readonly string root = Path.Combine(Path.GetTempPath(), "ChordVersionTests-" + Guid.NewGuid().ToString("N"));
	private readonly Guid device = Guid.NewGuid();
	private FileSystemBookStore store = null!;
	private BookLocation location = null!;
	private BookApplicationSession session = null!;
	private Guid songId;
	private Guid leftId;
	private Guid rightId;

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	public async Task InitializeAsync(CancellationToken token)
	{
		this.store = new(this.root);
		this.location = await this.store.CreateBookAsync("Comparison fixture", this.device, token);
		this.session = new();
		await this.session.ActivateAsync(this.store, this.location, token);
		this.songId = await this.session.CreateSongAsync("Song", [], [], "Left\r\n[C]original\n", this.device, token);
		this.leftId = this.session.Database!.SongFiles.Single().Id;
		string source = Path.Combine(this.root, "alternative.cho");
		await File.WriteAllTextAsync(source, "Right\r\n[G]alternative\n", token);
		await this.session.ImportSongFilesAsync(this.songId, [source], this.device, token);
		this.rightId = this.session.Database!.SongFiles.Single(file => file.Id != this.leftId).Id;
		await this.session.SetSongFileArchivedAsync(this.songId, this.rightId, true, this.device, token);
	}

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
	[DataRow(SongVersionChoice.KeepLeft)]
	[DataRow(SongVersionChoice.KeepRight)]
	public async Task KeepOnePreservesBytesAndUpdatesPreferencesInOneCommit(SongVersionChoice choice)
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		var token = this.TestContext.CancellationToken;
		Guid keptId = choice == SongVersionChoice.KeepLeft ? this.leftId : this.rightId;
		Guid deletedId = keptId == this.leftId ? this.rightId : this.leftId;
		string original = await this.store.ReadDatabaseJsonAsync(this.location, token);
		ChordDatabase database = DatabaseJson.Deserialize(original);
		database.Setlists.Add(new()
		{
			Id = Guid.NewGuid(), Name = "Setlist", Revision = RevisionStamp.Initial(this.device),
			Entries = [new() { Id = Guid.NewGuid(), SongId = this.songId, PreferredSongFileId = deletedId }],
		});
		await this.store.CommitMetadataAsync(this.location, original, database, token);
		await this.session.ReloadAsync(token);
		SongVersionComparison comparison = await this.CompareAsync();
		byte[] kept = await File.ReadAllBytesAsync(this.PathFor(keptId), token);
		await this.session.ResolveSongVersionsAsync(comparison, choice, this.device, token);
		this.session.Database!.SongFiles.Single().Id.ShouldBe(keptId);
		this.session.Database.SongFiles.Single().IsArchived.ShouldBeFalse();
		this.session.GetSongFiles(this.songId).Single().IsDefault.ShouldBeTrue();
		this.session.Database.Setlists.Single().Entries.Single().PreferredSongFileId.ShouldBe(keptId);
		this.session.Database.Tombstones.Single().EntityId.ShouldBe(deletedId);
		(await File.ReadAllBytesAsync(this.PathFor(keptId), token)).ShouldBe(kept);
		(await this.session.GetPresentationAsync(this.songId, cancellationToken: token)).SongFileId.ShouldBe(keptId);
	}

	[TestMethod]
	public async Task KeepBothLeavesBytesAndPriorDefaultUntouched()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		var token = this.TestContext.CancellationToken;
		SongVersionComparison comparison = await this.CompareAsync();
		byte[] left = await File.ReadAllBytesAsync(this.PathFor(this.leftId), token);
		byte[] right = await File.ReadAllBytesAsync(this.PathFor(this.rightId), token);
		await this.session.ResolveSongVersionsAsync(comparison, SongVersionChoice.KeepBoth, this.device, token);
		this.session.Database!.SongFiles.Count.ShouldBe(2);
		this.session.Database.SongFiles.All(file => !file.IsArchived).ShouldBeTrue();
		this.session.GetSongFiles(this.songId).Single(file => file.IsDefault).Id.ShouldBe(this.leftId);
		this.session.Database.Tombstones.ShouldBeEmpty();
		(await File.ReadAllBytesAsync(this.PathFor(this.leftId), token)).ShouldBe(left);
		(await File.ReadAllBytesAsync(this.PathFor(this.rightId), token)).ShouldBe(right);
	}

	[TestMethod]
	public async Task StaleCatalogCannotDeleteEitherVersion()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		var token = this.TestContext.CancellationToken;
		SongVersionComparison comparison = await this.CompareAsync();
		await this.session.RenameAsync("Changed after compare", this.device, token);
		string current = await this.store.ReadDatabaseJsonAsync(this.location, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => this.session.ResolveSongVersionsAsync(
			comparison, SongVersionChoice.KeepRight, this.device, token));
		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(current);
		File.Exists(this.PathFor(this.leftId)).ShouldBeTrue();
		File.Exists(this.PathFor(this.rightId)).ShouldBeTrue();
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task ExternalSourceEditCannotBeDiscarded(bool editKept)
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		var token = this.TestContext.CancellationToken;
		SongVersionComparison comparison = await this.CompareAsync();
		Guid modified = editKept ? this.leftId : this.rightId;
		string path = this.PathFor(modified);
		await File.WriteAllTextAsync(path, "Outside edit retained", token);
		string current = await this.store.ReadDatabaseJsonAsync(this.location, token);
		await Should.ThrowAsync<BookStoreException>(() => this.session.ResolveSongVersionsAsync(
			comparison, SongVersionChoice.KeepLeft, this.device, token));
		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(current);
		(await File.ReadAllTextAsync(path, token)).ShouldBe("Outside edit retained");
		File.Exists(this.PathFor(editKept ? this.rightId : this.leftId)).ShouldBeTrue();
	}

	[TestMethod]
	public async Task ReadOnlyComparisonPreservesTextAndEncodesMarkup()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		SongVersionComparison comparison = await this.CompareAsync();
		comparison.Left.Text.ShouldBe("Left\r\n[C]original\n");
		comparison.Right.Text.ShouldBe("Right\r\n[G]alternative\n");
		string html = SongVersionComparisonHtml.Create(comparison, this.TestContext.CancellationToken);
		html.ShouldContain("default-src 'none'");
		html.ShouldContain("Changed");
		html.ShouldNotContain("<script");
		this.session.Database!.SongFiles.Count.ShouldBe(2);
	}

	[TestMethod]
	public async Task CanceledResolutionLeavesCatalogAndSourcesUntouched()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		SongVersionComparison comparison = await this.CompareAsync();
		string before = await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken);
		using CancellationTokenSource canceled = new();
		canceled.Cancel();
		await Should.ThrowAsync<OperationCanceledException>(() => this.session.ResolveSongVersionsAsync(
			comparison, SongVersionChoice.KeepRight, this.device, canceled.Token));
		(await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken)).ShouldBe(before);
		File.Exists(this.PathFor(this.leftId)).ShouldBeTrue();
		File.Exists(this.PathFor(this.rightId)).ShouldBeTrue();
	}

	[TestMethod]
	public async Task KeepingRecoveredSheetMakesItEditableWithoutReadingUnrelatedAssets()
	{
		var token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		Guid unrelatedSong = await this.session.CreateSongAsync("Unrelated", [], [], "[C]Locked", this.device, token);
		Guid unrelatedFile = this.session.Database!.SongFiles.Single(file => file.SongId == unrelatedSong).Id;
		string before = await this.store.ReadDatabaseJsonAsync(this.location, token);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		database.SongFiles.Single(file => file.Id == this.rightId).RecoveryVersion = new()
		{
			RecoveredFromSyncUtc = DateTimeOffset.UtcNow,
			OriginalConflictingFileId = this.leftId,
			WinningFileId = this.leftId,
		};
		await this.store.CommitMetadataAsync(this.location, before, database, token);
		await this.session.ReloadAsync(token);
		using FileStream locked = File.Open(this.PathFor(unrelatedFile), FileMode.Open, FileAccess.Read, FileShare.None);
		SongVersionComparison comparison = await this.CompareAsync();
		comparison.Right.RecoveredUtc.ShouldNotBeNull();
		await this.session.ResolveSongVersionsAsync(comparison, SongVersionChoice.KeepRight, this.device, token);
		this.session.Database!.SongFiles.Single(file => file.Id == this.rightId).RecoveryVersion.ShouldBeNull();
		SongEditDocument editable = await this.session.GetSongFileEditAsync(this.songId, this.rightId, token);
		editable.Text.ShouldBe("Right\r\n[G]alternative\n");
		this.session.Database.SongFiles.Single(file => file.Id == unrelatedFile).ContentRevision.ShouldBe(1);
	}

	#endregion

	#region Private Methods

	private Task<SongVersionComparison> CompareAsync()
		=> this.session.CompareSongVersionsAsync(this.songId, this.leftId, this.rightId, this.TestContext.CancellationToken);

	private string PathFor(Guid fileId)
		=> Path.Combine(this.store.GetDirectory(this.location), this.session.Database!.SongFiles.Single(file => file.Id == fileId).RelativePath);

	#endregion
}
