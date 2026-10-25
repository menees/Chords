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
