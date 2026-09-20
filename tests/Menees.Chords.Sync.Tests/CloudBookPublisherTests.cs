#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class CloudBookPublisherTests
{
	#region Private Data

	private readonly string directory = Path.Combine(Path.GetTempPath(), "ChordPublishTests-" + Guid.NewGuid().ToString("N"));

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
	public async Task ChangedAssetIsCreatedBeforeCatalogWithoutOverwritingOldBytes()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		(InMemoryCloudReplica replica, CloudBookComparison comparison) = await CreateAsync(token);
		SongFile file = comparison.Merge.Database.SongFiles.Single();
		file.ContentHash = Hash("new sheet");
		string authoredPath = file.RelativePath;
		CloudBookPublication result = await CloudBookPublisher.PublishAsync(replica, comparison, OpenNewSourceAsync, this.directory, token);
		result.Assets[file.Id].Name.ShouldNotBe(authoredPath);
		DatabaseJson.Deserialize(result.DatabaseJson).SongFiles.Single().RelativePath.ShouldBe(result.Assets[file.Id].Name);
		comparison.Merge.Database.SongFiles.Single().RelativePath.ShouldBe(authoredPath);
		(await ReadAsync(replica, comparison.ManagedItems[file.Id].Id, token)).ShouldBe("old sheet");
		(await ReadAsync(replica, result.Assets[file.Id].Id, token)).ShouldBe("new sheet");
		replica.MutationLog[^2].ShouldStartWith("Create:");
		replica.MutationLog[^1].ShouldBe("Replace:database.json");
		replica.MutationLog.Any(item => item.StartsWith("Delete:", StringComparison.Ordinal)).ShouldBeFalse();
	}

	[TestMethod]
	public async Task StaleCatalogKeepsBothOldAssetAndConcurrentCatalog()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		(InMemoryCloudReplica replica, CloudBookComparison comparison) = await CreateAsync(token);
		using MemoryStream concurrent = new(Encoding.UTF8.GetBytes("concurrent catalog"));
		await replica.ReplaceAsync(comparison.DatabaseItem.Id, comparison.DatabaseItem.Version, concurrent, token);
		comparison.Merge.Database.SongFiles.Single().ContentHash = Hash("new sheet");
		await Should.ThrowAsync<InvalidOperationException>(() => CloudBookPublisher.PublishAsync(
			replica, comparison, OpenNewSourceAsync, this.directory, token));
		(await ReadAsync(replica, comparison.DatabaseItem.Id, token)).ShouldBe("concurrent catalog");
		(await ReadAsync(replica, comparison.ManagedItems.Values.Single().Id, token)).ShouldBe("old sheet");
		replica.MutationLog.Last().ShouldStartWith("Create:");
	}

	[TestMethod]
	public async Task MetadataOnlyPublicationDoesNotOpenOrUploadAnySheet()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		(InMemoryCloudReplica replica, CloudBookComparison comparison) = await CreateAsync(token);
		comparison.Merge.Database.Songs.Single().Title = "New title";
		int before = replica.MutationLog.Count;
		CloudBookPublication result = await CloudBookPublisher.PublishAsync(
			replica, comparison, (_, _) => throw new InvalidOperationException("Unrelated asset opened"), this.directory, token);
		replica.MutationLog.Count.ShouldBe(before + 1);
		DatabaseJson.Deserialize(result.DatabaseJson).Songs.Single().Title.ShouldBe("New title");
		Directory.Exists(this.directory).ShouldBeFalse();
	}

	[TestMethod]
	public async Task CorruptSourcePreventsAllCloudMutations()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		(InMemoryCloudReplica replica, CloudBookComparison comparison) = await CreateAsync(token);
		comparison.Merge.Database.SongFiles.Single().ContentHash = Hash("different expected bytes");
		int before = replica.MutationLog.Count;
		await Should.ThrowAsync<InvalidDataException>(() => CloudBookPublisher.PublishAsync(
			replica, comparison, OpenNewSourceAsync, this.directory, token));
		replica.MutationLog.Count.ShouldBe(before);
		Directory.GetFiles(this.directory).ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private static Task<Stream> OpenNewSourceAsync(SongFile file, CancellationToken token)
	{
		ArgumentNullException.ThrowIfNull(file);
		token.ThrowIfCancellationRequested();
		return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("new sheet")));
	}

	private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

	private static async Task<string> ReadAsync(InMemoryCloudReplica replica, ProviderItemId id, CancellationToken token)
	{
		await using Stream stream = await replica.DownloadAsync(id, token);
		using StreamReader reader = new(stream);
		return await reader.ReadToEndAsync(token);
	}

	private static async Task<(InMemoryCloudReplica Replica, CloudBookComparison Comparison)> CreateAsync(CancellationToken token)
	{
		Guid device = Guid.NewGuid();
		ChordDatabase cloud = ChordDatabase.Create("Test", device);
		cloud.Songs.Add(new() { Id = Guid.NewGuid(), Title = "Title" });
		Guid fileId = Guid.NewGuid();
		cloud.SongFiles.Add(new()
		{
			Id = fileId, SongId = cloud.Songs[0].Id,
			RelativePath = PortableManagedFileName.Create("Title", fileId, ".txt"), ContentHash = Hash("old sheet"),
		});
		InMemoryCloudReplica replica = new(new("Test", "account", "folder"));
		await replica.AuthenticateAsync(token);
		using MemoryStream json = new(Encoding.UTF8.GetBytes(DatabaseJson.Serialize(cloud)));
		CloudReplicaItem database = await replica.CreateAsync("database.json", json, token);
		using MemoryStream asset = new(Encoding.UTF8.GetBytes("old sheet"));
		CloudReplicaItem file = await replica.CreateAsync(cloud.SongFiles[0].RelativePath, asset, token);
		ChordDatabase local = DatabaseJson.Deserialize(DatabaseJson.Serialize(cloud));
		return (replica, new(replica.Identity, database, cloud, new(local, [], []), new Dictionary<Guid, CloudReplicaItem> { [fileId] = file }));
	}

	#endregion
}
