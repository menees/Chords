using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class VerifiedAssetStageTests
{
	private readonly string directory = Path.Combine(Path.GetTempPath(), "ChordStageTests-" + Guid.NewGuid().ToString("N"));

	public TestContext TestContext { get; set; } = null!;

	[TestCleanup]
	public void Cleanup()
	{
		if (Directory.Exists(this.directory))
		{
			Directory.Delete(this.directory, recursive: true);
		}
	}

	[TestMethod]
	public async Task StageIsSeekableExactAndRemovedOnDispose()
	{
		byte[] bytes = new byte[1_000_000];
		Random.Shared.NextBytes(bytes);
		using MemoryStream input = new(bytes, writable: false);
		await using (Stream stage = await VerifiedAssetStage.CreateAsync(
			input, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, this.directory, this.TestContext.CancellationToken))
		{
			stage.CanSeek.ShouldBeTrue();
			stage.Position.ShouldBe(0);
			stage.Length.ShouldBe(bytes.Length);
			(await SHA256.HashDataAsync(stage, this.TestContext.CancellationToken)).ShouldBe(SHA256.HashData(bytes));
			stage.Position = 0;
		}

		Directory.GetFiles(this.directory).ShouldBeEmpty();
		input.CanRead.ShouldBeTrue();
	}

	[TestMethod]
	[DataRow(2, false)]
	[DataRow(4, false)]
	[DataRow(3, true)]
	public async Task LengthAndHashMismatchLeaveNoStage(int expectedLength, bool wrongHash)
	{
		byte[] bytes = [1, 2, 3];
		using MemoryStream input = new(bytes);
		string hash = wrongHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
		await Should.ThrowAsync<InvalidDataException>(() => VerifiedAssetStage.CreateAsync(
			input, hash, expectedLength, this.directory, this.TestContext.CancellationToken));
		Directory.GetFiles(this.directory).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task CancellationAndInvalidHashCreateNoFiles()
	{
		using MemoryStream input = new([1]);
		await Should.ThrowAsync<ArgumentException>(() => VerifiedAssetStage.CreateAsync(
			input, "not-a-hash", 1, this.directory, this.TestContext.CancellationToken));
		await Should.ThrowAsync<OperationCanceledException>(() => VerifiedAssetStage.CreateAsync(
			input, new string('0', 64), 1, this.directory, new(canceled: true)));
		Directory.Exists(this.directory).ShouldBeFalse();
	}
}
