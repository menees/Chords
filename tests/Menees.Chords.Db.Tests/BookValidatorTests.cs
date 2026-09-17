#region Using Directives

using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookValidatorTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task FolderReportFindsMissingChangedRenamedDuplicateAndOrphanSheetsWithoutWriting()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookValidatorTests), Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Report fixture", device, token);
			foreach (string title in new[] { "Missing", "Changed", "Renamed" })
			{
				byte[] bytes = Encoding.UTF8.GetBytes($"{{title: {title}}}\r\n[C]Original\r\n");
				await BookImportService.ImportAsync(store, location, title + ".cho", new MemoryStream(bytes), device, token);
			}

			string baseline = await store.ReadDatabaseJsonAsync(location, token);
			ChordDatabase database = DatabaseJson.Deserialize(baseline);
			string directory = store.GetDirectory(location);
			SongFile missing = database.SongFiles.Single(file => file.RelativePath.StartsWith("Missing", StringComparison.Ordinal));
			SongFile changed = database.SongFiles.Single(file => file.RelativePath.StartsWith("Changed", StringComparison.Ordinal));
			SongFile renamed = database.SongFiles.Single(file => file.RelativePath.StartsWith("Renamed", StringComparison.Ordinal));
			File.Delete(Path.Combine(directory, missing.RelativePath));
			string changedPath = Path.Combine(directory, changed.RelativePath);
			DateTime timestamp = File.GetLastWriteTimeUtc(changedPath);
			string changedText = (await File.ReadAllTextAsync(changedPath, token)).Replace("[C]", "[D]");
			await File.WriteAllTextAsync(changedPath, changedText, token);
			File.SetLastWriteTimeUtc(changedPath, timestamp);
			string renamedPath = Path.Combine(directory, PortableManagedFileName.Create("Moved", renamed.Id, ".cho"));
			File.Move(Path.Combine(directory, renamed.RelativePath), renamedPath);
			string duplicate = Path.Combine(directory, PortableManagedFileName.Create("Duplicate", changed.Id, ".cho"));
			File.Copy(changedPath, duplicate);
			string orphan = Path.Combine(directory, PortableManagedFileName.Create("Orphan", Guid.NewGuid(), ".cho"));
			await File.WriteAllTextAsync(orphan, "Untracked contents", token);
			using FileStream orphanLock = new(orphan, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			string note = Path.Combine(directory, "README.md");
			await File.WriteAllTextAsync(note, "Unrelated", token);
			using FileStream noteLock = new(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

			BookValidationReport report = await BookValidator.ValidateFolderAsync(store, location, token);

			report.IsValid.ShouldBeFalse();
			report.Issues.ShouldContain(issue => issue.Kind == BookValidationIssueKind.MissingAsset && issue.SongFileId == missing.Id);
			report.Issues.ShouldContain(issue => issue.Kind == BookValidationIssueKind.HashMismatch && issue.SongFileId == changed.Id);
			report.Issues.ShouldContain(issue => issue.Kind == BookValidationIssueKind.PathMismatch && issue.SongFileId == renamed.Id);
			report.Issues.ShouldContain(issue => issue.Kind == BookValidationIssueKind.DuplicateAssetIdentity && issue.SongFileId == changed.Id);
			report.Issues.ShouldContain(issue => issue.Kind == BookValidationIssueKind.UnexpectedManagedAsset
				&& issue.RelativePath == Path.GetFileName(orphan));
			report.Issues.ShouldNotContain(issue => issue.RelativePath == "README.md");
			(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(baseline);
			(await File.ReadAllTextAsync(changedPath, token)).ShouldBe(changedText);
			File.Exists(renamedPath).ShouldBeTrue();
			File.Exists(duplicate).ShouldBeTrue();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task MissingDatabaseProducesReadableFailureAndCanceledChecksPropagateCancellation()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookValidatorTests), Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			BookLocation location = await store.CreateBookAsync("Missing database", Guid.NewGuid(), token);
			using CancellationTokenSource cancellation = new();
			cancellation.Cancel();
			await Should.ThrowAsync<OperationCanceledException>(() => BookValidator.ValidateFolderAsync(store, location, cancellation.Token));
			File.Delete(Path.Combine(store.GetDirectory(location), "database.json"));
			BookValidationReport report = await BookValidator.ValidateFolderAsync(store, location, token);
			report.IsValid.ShouldBeFalse();
			report.Issues.Single().Kind.ShouldBe(BookValidationIssueKind.InvalidDatabase);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidBookPassesIndependentValidation()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Validated", Guid.NewGuid(), cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		byte[] content = TestData.OpenSongBytes();
		Song song = new()
		{
			Id = Guid.CreateVersion7(TestData.Now),
			Title = "Blessed Assurance",
			Revision = RevisionStamp.Initial(Guid.NewGuid(), TestData.Now),
		};
		Guid fileId = Guid.CreateVersion7(TestData.Now.AddMilliseconds(1));
		database.Songs.Add(song);
		database.SongFiles.Add(new()
		{
			Id = fileId,
			SongId = song.Id,
			RelativePath = PortableManagedFileName.Create(song.Title, fileId, extension: null),
			MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.OpenSongXml,
			ContentHash = Hash(content),
			ObservedLength = content.LongLength,
			Revision = RevisionStamp.Initial(Guid.NewGuid(), TestData.Now),
		});

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteManagedAssetAsync(fileId, database.SongFiles[0].RelativePath, new MemoryStream(content), cancellationToken);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
			await write.CommitAsync(cancellationToken);
		}

		BookValidationReport report = await BookValidator.ValidateAsync(store, location, cancellationToken);

		report.IsValid.ShouldBeTrue();
		report.Database!.Id.ShouldBe(database.Id);
		report.Issues.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task MissingAssetIsReportedWithoutChangingDatabase()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		ChordDatabase database = TestData.CreateDatabase();
		BookLocation location = await CreateLocationAsync(cancellationToken);
		StubBookStore store = new(DatabaseJson.Serialize(database), [], new Dictionary<Guid, byte[]>());

		BookValidationReport report = await BookValidator.ValidateAsync(store, location, cancellationToken);

		report.IsValid.ShouldBeFalse();
		report.Issues.Single().Kind.ShouldBe(BookValidationIssueKind.MissingAsset);
		report.Database!.Id.ShouldBe(database.Id);
	}

	[TestMethod]
	public async Task DescriptorAndContentDisagreementsAreReported()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		ChordDatabase database = TestData.CreateDatabase();
		SongFile file = database.SongFiles.Single();
		byte[] wrongContent = Encoding.UTF8.GetBytes("different");
		Guid unexpectedId = Guid.NewGuid();
		ManagedAssetDescriptor[] descriptors =
		[
			new(file.Id, "Renamed.txt", wrongContent.LongLength, Hash(wrongContent)),
			new(unexpectedId, "Unexpected.txt", 0, Hash([])),
		];
		Dictionary<Guid, byte[]> contents = new()
		{
			[file.Id] = wrongContent,
			[unexpectedId] = [],
		};
		StubBookStore store = new(DatabaseJson.Serialize(database), descriptors, contents);

		BookValidationReport report = await BookValidator.ValidateAsync(
			store,
			await CreateLocationAsync(cancellationToken),
			cancellationToken);

		report.Issues.Select(issue => issue.Kind).ShouldBe(
			[
				BookValidationIssueKind.PathMismatch,
				BookValidationIssueKind.LengthMismatch,
				BookValidationIssueKind.HashMismatch,
				BookValidationIssueKind.UnexpectedManagedAsset,
			],
			ignoreOrder: true);
	}

	[TestMethod]
	public async Task InvalidDatabaseStopsBeforeAssetInspection()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		StubBookStore store = new("not json", [], new Dictionary<Guid, byte[]>());

		BookValidationReport report = await BookValidator.ValidateAsync(
			store,
			await CreateLocationAsync(cancellationToken),
			cancellationToken);

		report.Database.ShouldBeNull();
		report.Issues.Single().Kind.ShouldBe(BookValidationIssueKind.InvalidDatabase);
	}

	#endregion

	#region Private Methods

	private static async Task<BookLocation> CreateLocationAsync(CancellationToken cancellationToken)
	{
		InMemoryBookStore owner = new();
		return await owner.CreateBookAsync("Location", Guid.NewGuid(), cancellationToken);
	}

	private static string Hash(byte[] content)
		=> Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

	#endregion

	#region Private Types

	private sealed class StubBookStore : IBookStore
	{
		private readonly IReadOnlyList<ManagedAssetDescriptor> descriptors;
		private readonly IReadOnlyDictionary<Guid, byte[]> contents;
		private readonly string json;

		public StubBookStore(
			string json,
			IReadOnlyList<ManagedAssetDescriptor> descriptors,
			IReadOnlyDictionary<Guid, byte[]> contents)
		{
			this.json = json;
			this.descriptors = descriptors;
			this.contents = contents;
		}

		public BookStoreCapabilities Capabilities => BookStoreCapabilities.None;

		public Task<string> ReadDatabaseJsonAsync(BookLocation location, CancellationToken cancellationToken = default)
			=> Task.FromResult(this.json);

		public async IAsyncEnumerable<ManagedAssetDescriptor> EnumerateManagedAssetsAsync(
			BookLocation location,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			foreach (ManagedAssetDescriptor descriptor in this.descriptors)
			{
				cancellationToken.ThrowIfCancellationRequested();
				yield return descriptor;
				await Task.Yield();
			}
		}

		public Task<Stream> OpenManagedAssetAsync(
			BookLocation location,
			Guid songFileId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<Stream>(new MemoryStream(this.contents[songFileId], writable: false));

		public Task<BookLocation> CreateBookAsync(string name, Guid deviceId, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<bool> ExistsAsync(BookLocation location, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task DeleteBookAsync(BookLocation location, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<string> CommitMetadataAsync(
			BookLocation location, string expectedJson, ChordDatabase database, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task CommitMetadataAsync(BookLocation location, string expectedJson, string updatedJson, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<IStagedBookWrite> StageWriteAsync(BookLocation location, string expectedJson, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<IStagedBookWrite> StageWriteAsync(BookLocation location, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<long?> GetAvailableSpaceAsync(BookLocation location, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	#endregion
}
