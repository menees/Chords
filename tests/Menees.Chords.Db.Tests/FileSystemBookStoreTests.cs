#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class FileSystemBookStoreTests
{
	#region Private Data

	private string directory = null!;

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestInitialize]
	public void Initialize()
	{
		this.directory = Path.Combine(Path.GetTempPath(), nameof(FileSystemBookStoreTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this.directory);
	}

	[TestCleanup]
	public void Cleanup()
	{
		if (Directory.Exists(this.directory))
		{
			Directory.Delete(this.directory, recursive: true);
		}
	}

	[TestMethod]
	public async Task CommitPersistsAcrossStoreInstances()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		string bookDirectory;
		using (FileSystemBookStore store = new(this.directory))
		{
			BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
			AddOpenSong(database, deviceId);
			await CommitAsync(store, location, database, cancellationToken);
			bookDirectory = store.GetDirectory(location);
		}

		using FileSystemBookStore reopenedStore = new(this.directory);
		BookLocation reopened = await reopenedStore.OpenBookAsync(bookDirectory, cancellationToken);
		ChordDatabase committed = DatabaseJson.Deserialize(await reopenedStore.ReadDatabaseJsonAsync(reopened, cancellationToken));
		SongFile file = committed.SongFiles.Single();
		using Stream content = await reopenedStore.OpenManagedAssetAsync(reopened, file.Id, cancellationToken);
		using MemoryStream copy = new();
		await content.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
	}

	[TestMethod]
	public async Task StaleWriterCannotOverwriteCommittedDatabase()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		await using IStagedBookWrite first = await store.StageWriteAsync(location, cancellationToken);
		await using IStagedBookWrite stale = await store.StageWriteAsync(location, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		database.Name = "First";
		await first.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await first.CommitAsync(cancellationToken);

		database.Name = "Stale";
		await stale.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => stale.CommitAsync(cancellationToken));
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken)).Name.ShouldBe("First");
	}

	[TestMethod]
	[DataRow((int)FileSystemCommitStep.ManagedAssetReplaced)]
	[DataRow((int)FileSystemCommitStep.DatabaseReplaced)]
	public async Task InjectedCommitFailureRestoresDatabaseAndAsset(int failingStepValue)
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		bool fail = false;
		FileSystemCommitStep failingStep = (FileSystemCommitStep)failingStepValue;
		using FileSystemBookStore store = new(this.directory, step =>
		{
			if (fail && step == failingStep)
			{
				throw new IOException("Injected commit failure.");
			}
		});
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database, Guid.NewGuid());
		await CommitAsync(store, location, database, cancellationToken);
		string originalJson = await store.ReadDatabaseJsonAsync(location, cancellationToken);
		SongFile file = database.SongFiles.Single();
		byte[] replacement = Encoding.UTF8.GetBytes("{title:Replacement}");
		file.ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(replacement)).ToLowerInvariant();
		file.ObservedLength = replacement.Length;
		database.Name = "Replacement";
		fail = true;

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(replacement), cancellationToken);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
			await Should.ThrowAsync<IOException>(() => write.CommitAsync(cancellationToken));
		}

		(await store.ReadDatabaseJsonAsync(location, cancellationToken)).ShouldBe(originalJson);
		using Stream restored = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken);
		using MemoryStream copy = new();
		await restored.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
		Directory.EnumerateDirectories(this.directory, "*.chordbook-stage-*", SearchOption.TopDirectoryOnly).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task BatchImportCommitsOnceAndRetrySkipsExactDuplicates()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		string sourceDirectory = Path.Combine(this.directory, "Sources");
		Directory.CreateDirectory(sourceDirectory);
		string[] paths =
		[
			Path.Combine(sourceDirectory, "First.cho"),
			Path.Combine(sourceDirectory, "Second.cho"),
			Path.Combine(sourceDirectory, "Third.cho"),
		];
		for (int index = 0; index < paths.Length; index++)
		{
			await File.WriteAllTextAsync(paths[index], $"{{title:Song {index + 1}}}\n[C]Words", cancellationToken);
		}

		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Batch", Guid.NewGuid(), cancellationToken);

		IReadOnlyList<BookImportResult> first = await BookImportService.ImportFilesAsync(
			store,
			location,
			paths,
			Guid.NewGuid(),
			cancellationToken);
		IReadOnlyList<BookImportResult> retry = await BookImportService.ImportFilesAsync(
			store,
			location,
			paths,
			Guid.NewGuid(),
			cancellationToken);

		first.Count.ShouldBe(paths.Length);
		retry.ShouldBeEmpty();
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		database.Songs.Count.ShouldBe(paths.Length);
		database.SongFiles.Count.ShouldBe(paths.Length);
		database.Revision.Revision.ShouldBe(2);
	}

	[TestMethod]
	public async Task DeleteLeavesUnrelatedContentUntouched()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Test Book", Guid.NewGuid(), cancellationToken);
		string bookDirectory = store.GetDirectory(location);
		string notes = Path.Combine(bookDirectory, "notes.txt");
		string subdirectory = Path.Combine(bookDirectory, "Other");
		await File.WriteAllTextAsync(notes, "Keep me", Encoding.UTF8, cancellationToken);
		Directory.CreateDirectory(subdirectory);

		await store.DeleteBookAsync(location, cancellationToken);

		File.Exists(notes).ShouldBeTrue();
		Directory.Exists(subdirectory).ShouldBeTrue();
		File.Exists(Path.Combine(bookDirectory, "database.json")).ShouldBeFalse();
	}

	[TestMethod]
	public async Task InspectClassifiesExternalChangesWithoutDeletingAnything()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database, deviceId);
		await CommitAsync(store, location, database, cancellationToken);
		string bookDirectory = store.GetDirectory(location);
		SongFile file = database.SongFiles.Single();
		string renamed = PortableManagedFileName.Create("Renamed", file.Id, null);
		File.Move(Path.Combine(bookDirectory, file.RelativePath), Path.Combine(bookDirectory, renamed));
		Guid candidateId = Guid.CreateVersion7();
		string candidate = PortableManagedFileName.Create("Candidate", candidateId, ".cho");
		await File.WriteAllTextAsync(Path.Combine(bookDirectory, candidate), "{title:Candidate}", cancellationToken);

		IReadOnlyList<ExternalBookProblem> problems = await store.InspectAsync(location, cancellationToken);

		problems.ShouldContain(problem => problem.RelativePath == renamed && problem.Message.Contains("renamed", StringComparison.Ordinal));
		problems.ShouldContain(problem => problem.RelativePath == candidate && problem.Message.Contains("candidate", StringComparison.Ordinal));
		File.Exists(Path.Combine(bookDirectory, renamed)).ShouldBeTrue();
		File.Exists(Path.Combine(bookDirectory, candidate)).ShouldBeTrue();
	}

	[TestMethod]
	public async Task InspectReportsChangedAndMissingManagedFiles()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database, deviceId);
		await CommitAsync(store, location, database, cancellationToken);
		string bookDirectory = store.GetDirectory(location);
		SongFile file = database.SongFiles.Single();
		string filePath = Path.Combine(bookDirectory, file.RelativePath);
		await File.WriteAllTextAsync(filePath, "changed", cancellationToken);
		IReadOnlyList<ExternalBookProblem> changed = await store.InspectAsync(location, cancellationToken);
		changed.ShouldContain(problem => problem.Message.Contains("changed", StringComparison.Ordinal));

		File.Delete(filePath);
		IReadOnlyList<ExternalBookProblem> missing = await store.InspectAsync(location, cancellationToken);
		missing.ShouldContain(problem => problem.Message.Contains("missing", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task ReconcileAdoptsGuidPreservingRename()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database, deviceId);
		await CommitAsync(store, location, database, cancellationToken);
		SongFile file = database.SongFiles.Single();
		string bookDirectory = store.GetDirectory(location);
		string renamed = PortableManagedFileName.Create("Renamed", file.Id, null);
		File.Move(Path.Combine(bookDirectory, file.RelativePath), Path.Combine(bookDirectory, renamed));

		BookReconcileResult result = await store.ReconcileAsync(location, deviceId, cancellationToken);

		result.RenamedFileCount.ShouldBe(1);
		result.ChangedFileCount.ShouldBe(0);
		ChordDatabase reconciled = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		reconciled.SongFiles.Single().RelativePath.ShouldBe(renamed);
		File.Exists(Path.Combine(bookDirectory, renamed)).ShouldBeTrue();
	}

	[TestMethod]
	public async Task ReconcileAdoptsContentAndMetadataEdit()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		using FileSystemBookStore store = new(this.directory);
		BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database, deviceId);
		await CommitAsync(store, location, database, cancellationToken);
		SongFile file = database.SongFiles.Single();
		string filePath = Path.Combine(store.GetDirectory(location), file.RelativePath);
		string changedSource = "<song><title>Changed Title</title><author>New Artist</author><lyrics>V1&#10;.Changed</lyrics></song>";
		await File.WriteAllTextAsync(filePath, changedSource, cancellationToken);

		BookReconcileResult result = await store.ReconcileAsync(location, deviceId, cancellationToken);

		result.ChangedFileCount.ShouldBe(1);
		ChordDatabase reconciled = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		reconciled.Songs.Single().Title.ShouldBe("Changed Title");
		reconciled.SongFiles.Single().ContentRevision.ShouldBe(2);
	}

	#endregion

	#region Private Methods

	private static void AddOpenSong(ChordDatabase database, Guid deviceId)
	{
		byte[] bytes = TestData.OpenSongBytes();
		Song song = new()
		{
			Id = Guid.CreateVersion7(),
			Title = "Blessed Assurance",
			Revision = RevisionStamp.Initial(deviceId),
		};
		Guid fileId = Guid.CreateVersion7();
		database.Songs.Add(song);
		database.SongFiles.Add(new SongFile
		{
			Id = fileId,
			SongId = song.Id,
			RelativePath = PortableManagedFileName.Create(song.Title, fileId, extension: null),
			MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.OpenSongXml,
			ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
			ObservedLength = bytes.Length,
			ContentRevision = 1,
			Revision = RevisionStamp.Initial(deviceId),
		});
	}

	private static async Task CommitAsync(
		FileSystemBookStore store,
		BookLocation location,
		ChordDatabase database,
		CancellationToken cancellationToken)
	{
		await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken);
		SongFile file = database.SongFiles.Single();
		await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(TestData.OpenSongBytes()), cancellationToken);
		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await write.CommitAsync(cancellationToken);
	}

	#endregion
}
