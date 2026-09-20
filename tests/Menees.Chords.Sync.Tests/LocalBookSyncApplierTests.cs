#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class LocalBookSyncApplierTests
{
	#region Private Data

	private readonly string root = Path.Combine(Path.GetTempPath(), "ChordApplyTests-" + Guid.NewGuid().ToString("N"));
	private FileSystemBookStore store = null!;
	private BookLocation location = null!;
	private string original = string.Empty;
	private ChordDatabase target = null!;

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	public async Task InitializeAsync(CancellationToken token)
	{
		this.store = new(this.root);
		Guid device = Guid.NewGuid();
		this.location = await this.store.CreateBookAsync("Sync fixture", device, token);
		using MemoryStream source = new(Encoding.UTF8.GetBytes("{title: Original}\n[C]Old"));
		await BookImportService.ImportAsync(this.store, this.location, "song.cho", source, device, token);
		this.original = await this.store.ReadDatabaseJsonAsync(this.location, token);
		this.target = DatabaseJson.Deserialize(this.original);
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
	public async Task MetadataOnlyCommitDoesNotOpenLockedAsset()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		this.target.Songs[0].Title = "Cloud title";
		string path = this.AssetPath();
		using FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
		await this.ApplyAsync((_, _) => throw new InvalidOperationException("Unexpected asset read"));
		ChordDatabase result = DatabaseJson.Deserialize(await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken));
		result.Songs[0].Title.ShouldBe("Cloud title");
	}

	[TestMethod]
	public async Task VerifiedReplacementCommitsBytesAndCatalogTogether()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		byte[] bytes = Encoding.UTF8.GetBytes("{title: Changed}\n[G]New");
		this.target.SongFiles[0].ContentHash = SongFileAnalyzer.Hash(bytes);
		this.target.SongFiles[0].ObservedLength = bytes.Length;
		this.target.SongFiles[0].RelativePath = PortableManagedFileName.Create("Cloud version", this.target.SongFiles[0].Id, ".cho");
		await this.ApplyAsync((_, _) => Task.FromResult<Stream>(new MemoryStream(bytes)));
		(await File.ReadAllBytesAsync(this.AssetPath(), this.TestContext.CancellationToken)).ShouldBe(bytes);
		(await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken)).ShouldBe(DatabaseJson.Serialize(this.target));
	}

	[TestMethod]
	public async Task ExternalEditDuringStagingIsPreservedAndCommitRollsBack()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		byte[] incoming = Encoding.UTF8.GetBytes("{title: Incoming}\n[G]New");
		byte[] external = Encoding.UTF8.GetBytes("{title: External}\n[D]Keep me");
		this.target.SongFiles[0].ContentHash = SongFileAnalyzer.Hash(incoming);
		string path = this.AssetPath();
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => this.ApplyAsync(async (_, token) =>
		{
			await File.WriteAllBytesAsync(path, external, token);
			return new MemoryStream(incoming);
		}));
		(await File.ReadAllBytesAsync(path, this.TestContext.CancellationToken)).ShouldBe(external);
		(await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken)).ShouldBe(this.original);
	}

	[TestMethod]
	public async Task BadIncomingHashLeavesOriginalBookUntouched()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		this.target.SongFiles[0].ContentHash = SongFileAnalyzer.Hash(Encoding.UTF8.GetBytes("expected"));
		await Should.ThrowAsync<InvalidDataException>(() => this.ApplyAsync((_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]))));
		(await this.store.ReadDatabaseJsonAsync(this.location, this.TestContext.CancellationToken)).ShouldBe(this.original);
	}

	[TestMethod]
	public async Task PublicationAndLocalApplicationConvergeThroughRealStore()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		await this.InitializeAsync(token);
		InMemoryCloudReplica cloud = new(new("Test", "account", "folder"));
		await cloud.AuthenticateAsync(token);
		using MemoryStream catalog = new(Encoding.UTF8.GetBytes(this.original));
		await cloud.CreateAsync("database.json", catalog, token);
		SongFile originalFile = this.target.SongFiles.Single();
		await using (Stream old = await this.store.OpenManagedAssetAsync(this.location, originalFile.Id, token))
		{
			await cloud.CreateAsync(originalFile.RelativePath, old, token);
		}

		BookMergeBase baseline = BookSyncMerger.CreateBase(this.target);
		byte[] incoming = Encoding.UTF8.GetBytes("{title: New version}\n[G]Verified bytes");
		this.target.SongFiles[0].ContentHash = SongFileAnalyzer.Hash(incoming);
		this.target.SongFiles[0].ObservedLength = incoming.Length;
		CloudBookComparison comparison = await CloudBookComparisonService.CompareAsync(
			this.target, cloud, baseline, SyncDirection.TwoWay, Guid.NewGuid(), DateTimeOffset.UtcNow, token);
		CloudBookPublication published = await CloudBookPublisher.PublishAsync(
			cloud, comparison, (_, _) => Task.FromResult<Stream>(new MemoryStream(incoming)), Path.Combine(this.root, "stage"), token);
		await LocalBookSyncApplier.ApplyAsync(
			this.store,
			this.location,
			this.original,
			published.DatabaseJson,
			(file, cancellation) => cloud.DownloadAsync(published.Assets[file.Id].Id, cancellation),
			Path.Combine(this.root, "stage"),
			token);
		(await this.store.ReadDatabaseJsonAsync(this.location, token)).ShouldBe(published.DatabaseJson);
		await using Stream installed = await this.store.OpenManagedAssetAsync(this.location, originalFile.Id, token);
		using StreamReader reader = new(installed);
		(await reader.ReadToEndAsync(token)).ShouldBe(Encoding.UTF8.GetString(incoming));
	}

	#endregion

	#region Private Methods

	private string AssetPath() => Path.Combine(this.store.GetDirectory(this.location), this.target.SongFiles[0].RelativePath);

	private Task ApplyAsync(Func<SongFile, CancellationToken, Task<Stream>> openSource) => LocalBookSyncApplier.ApplyAsync(
		this.store,
		this.location,
		this.original,
		DatabaseJson.Serialize(this.target),
		openSource,
		Path.Combine(this.root, "stage"),
		this.TestContext.CancellationToken);

	#endregion
}
