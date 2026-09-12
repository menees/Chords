#region Using Directives

using System.IO;
using System.Text;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookReconciliationTests
{
	#region Private Data

	private readonly string directory = Path.Combine(Path.GetTempPath(), nameof(BookReconciliationTests), Guid.NewGuid().ToString("N"));
	private readonly Guid device = Guid.NewGuid();

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestCleanup]
	public void Cleanup()
	{
		if (Directory.Exists(this.directory))
		{
			Directory.Delete(this.directory, recursive: true);
		}
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task PreviewIsReadOnlyAndConflictResolutionIsExplicit(bool useSourceTitle)
	{
		var token = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		var (location, file) = await this.CreateSongAsync(store);
		string baseline = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase edited = DatabaseJson.Deserialize(baseline);
		edited.Songs.Single().Title = "Catalog title";
		edited.Songs.Single().Artists = ["Catalog artist"];
		await store.CommitMetadataAsync(location, baseline, edited, token);
		baseline = await store.ReadDatabaseJsonAsync(location, token);
		string path = Path.Combine(store.GetDirectory(location), file.RelativePath);
		const string Changed = "{title: New source}\r\n{artist: New artist}\r\n[G]New words\n";
		await File.WriteAllTextAsync(path, Changed, token);
		BookReconcilePreview preview = await store.PreviewReconcileAsync(location, this.device, token);
		preview.Conflicts.Count.ShouldBe(2);
		preview.Changes.Single().ContentChanged.ShouldBeTrue();
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(baseline);
		string titleKey = preview.Conflicts.Single(conflict => conflict.Field == "Title").Key;
		await store.ApplyReconcileAsync(preview, useSourceTitle ? new HashSet<string> { titleKey } : null, token);
		ChordDatabase applied = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		applied.Songs.Single().Title.ShouldBe(useSourceTitle ? "New source" : "Catalog title");
		applied.Songs.Single().Artists.ShouldBe(["Catalog artist"]);
		applied.Songs.Single().SourceMetadata["title"].Single().Value.ShouldBe("New source");
		(await File.ReadAllTextAsync(path, token)).ShouldBe(Changed);
		(await store.PreviewReconcileAsync(location, this.device, token)).HasChanges.ShouldBeFalse();
	}

	[TestMethod]
	public async Task ApplyRejectsChangedDatabaseAndChangedSourceEvenWithPreservedTimestamp()
	{
		var token = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		var (location, file) = await this.CreateSongAsync(store);
		string path = Path.Combine(store.GetDirectory(location), file.RelativePath);
		await File.WriteAllTextAsync(path, "[C]First edit", token);
		BookReconcilePreview preview = await store.PreviewReconcileAsync(location, this.device, token);
		DateTime stamp = File.GetLastWriteTimeUtc(path);
		await File.WriteAllTextAsync(path, "[D]Other edit", token);
		File.SetLastWriteTimeUtc(path, stamp);
		string baseline = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<BookStoreException>(() => store.ApplyReconcileAsync(preview, cancellationToken: token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(baseline);
		preview = await store.PreviewReconcileAsync(location, this.device, token);
		ChordDatabase database = DatabaseJson.Deserialize(baseline);
		database.Name = "New catalog state";
		await store.CommitMetadataAsync(location, baseline, database, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => store.ApplyReconcileAsync(preview, cancellationToken: token));
	}

	[TestMethod]
	public async Task MissingAndDuplicateFilesRemainProblemsWithoutTombstones()
	{
		var token = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		var (location, file) = await this.CreateSongAsync(store);
		string path = Path.Combine(store.GetDirectory(location), file.RelativePath);
		File.Copy(path, Path.Combine(store.GetDirectory(location), PortableManagedFileName.Create("Duplicate", file.Id, ".cho")));
		BookReconcilePreview duplicate = await store.PreviewReconcileAsync(location, this.device, token);
		duplicate.Problems.Single().Message.ShouldContain("Multiple");
		duplicate.HasChanges.ShouldBeFalse();
		File.Delete(path);
		File.Delete(Path.Combine(store.GetDirectory(location), PortableManagedFileName.Create("Duplicate", file.Id, ".cho")));
		BookReconcilePreview missing = await store.PreviewReconcileAsync(location, this.device, token);
		missing.Problems.Single().Message.ShouldContain("missing");
		await store.ApplyReconcileAsync(missing, cancellationToken: token);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		database.SongFiles.Single().Id.ShouldBe(file.Id);
		database.Tombstones.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task UnchangedAssetsAreNotOpenedAndAnalysisRefreshPreservesCatalogOverrides()
	{
		var token = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		var (location, file) = await this.CreateSongAsync(store);
		string path = Path.Combine(store.GetDirectory(location), file.RelativePath);
		using (FileStream locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			BookReconcilePreview preview = await store.PreviewReconcileAsync(location, this.device, token);
			preview.HasChanges.ShouldBeFalse();
			await store.ApplyReconcileAsync(preview, cancellationToken: token);
		}

		string baseline = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(baseline);
		database.Songs.Single().Title = "Independent title";
		database.Songs.Single().Artists.Clear();
		database.SongFiles.Single().AnalysisVersion = 0;
		await store.CommitMetadataAsync(location, baseline, database, token);
		await BookMetadataRefresh.RefreshAsync(store, location, this.device, token);
		database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		database.Songs.Single().Title.ShouldBe("Independent title");
		database.Songs.Single().Artists.ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private async Task<(BookLocation Location, SongFile File)> CreateSongAsync(FileSystemBookStore store)
	{
		var token = this.TestContext.CancellationToken;
		BookLocation location = await store.CreateBookAsync("Reconciliation", this.device, token);
		using MemoryStream source = new(Encoding.UTF8.GetBytes("{title: Original}\n{artist: Original artist}\n[C]Words"));
		await BookImportService.ImportAsync(store, location, "Original.cho", source, this.device, token);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
		return (location, database.SongFiles.Single());
	}

	#endregion
}
