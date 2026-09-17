#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookRecoveryTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task FailedSafetyBackupCannotPublishRecoveryOrLeaveAPartialArchive()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = await Fixture.CreateAsync(token);
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		byte[] changed = Encoding.UTF8.GetBytes("Unreconciled external contents");
		await File.WriteAllBytesAsync(fixture.Asset, changed, token);
		await Should.ThrowAsync<BookStoreValidationException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token));
		File.Exists(fixture.Safety).ShouldBeFalse();
		Directory.GetFiles(fixture.Root, "*.tmp").ShouldBeEmpty();
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token)).ShouldBe(current);
		(await File.ReadAllBytesAsync(fixture.Asset, token)).ShouldBe(changed);
		(await fixture.Store.ReadRecoveryEpochAsync(fixture.Location, token)).ShouldBe(Guid.Empty);
	}

	[TestMethod]
	public async Task ReviewLocksArchiveAndRecoveryEpochSurvivesReopenButIsExcludedFromBackups()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = await Fixture.CreateAsync(token);
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		if (OperatingSystem.IsWindows())
		{
			Should.Throw<IOException>(() =>
			{
				using FileStream conflicting = new(fixture.Archive, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
			});
		}

		await BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token);
		Guid epoch = await fixture.Store.ReadRecoveryEpochAsync(fixture.Location, token);
		using FileSystemBookStore reopened = new(fixture.Root);
		BookLocation same = await reopened.OpenBookAsync(fixture.Directory, token);
		(await reopened.ReadRecoveryEpochAsync(same, token)).ShouldBe(epoch);
		using MemoryStream archive = new();
		await BookBackup.CreateAsync(reopened, same, archive, token);
		archive.Position = 0;
		using System.IO.Compression.ZipArchive zip = new(archive);
		zip.Entries.ShouldNotContain(entry => entry.FullName == ".recovery-epoch");
	}

	[TestMethod]
	public async Task ReplacementPreservesIdentityAndExactBytesWithValidSafetyCopyAndLocalEpoch()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = await Fixture.CreateAsync(token);
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		string unrelated = Path.Combine(fixture.Directory, "personal-notes.txt");
		await File.WriteAllTextAsync(unrelated, "Unrelated", token);
		using FileStream locked = new(unrelated, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		review.SongCount.ShouldBe(1);
		await BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token);
		ChordDatabase restored = DatabaseJson.Deserialize(await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token));
		restored.Id.ShouldBe(review.BookId);
		restored.Songs.Count.ShouldBe(1);
		restored.Songs.Single().Title.ShouldBe("Before");
		restored.Revision.Revision.ShouldBeGreaterThan(DatabaseJson.Deserialize(current).Revision.Revision);
		(await File.ReadAllBytesAsync(fixture.Asset, token)).ShouldBe(Fixture.BeforeBytes);
		(await fixture.Store.ReadRecoveryEpochAsync(fixture.Location, token)).ShouldNotBe(Guid.Empty);
		using BookBackupReview safety = await BookBackup.ReviewFileAsync(fixture.Safety, token);
		safety.BookId.ShouldBe(review.BookId);
		safety.SongCount.ShouldBe(2);
		using Stream original = safety.OpenAsset(Path.GetFileName(fixture.Asset));
		using MemoryStream copy = new();
		await original.CopyToAsync(copy, token);
		copy.ToArray().ShouldBe(Fixture.AfterBytes);
		Directory.GetDirectories(fixture.Root, ".*.chordbook-stage-*").ShouldBeEmpty();
	}

	[TestMethod]
	public async Task ForeignBookAndStaleBaselineDoNotCreateSafetyBackupOrModifyContent()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = await Fixture.CreateAsync(token);
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		BookLocation foreign = await fixture.Store.CreateBookAsync("Different", fixture.Device, token);
		string foreignJson = await fixture.Store.ReadDatabaseJsonAsync(foreign, token);
		await Should.ThrowAsync<BookStoreValidationException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, foreign, review, foreignJson, fixture.Safety, fixture.Device, token));
		ChordDatabase changed = DatabaseJson.Deserialize(current);
		changed.Name = "Changed during review";
		await fixture.Store.CommitMetadataAsync(fixture.Location, current, changed, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token));
		File.Exists(fixture.Safety).ShouldBeFalse();
		(await File.ReadAllBytesAsync(fixture.Asset, token)).ShouldBe(Fixture.AfterBytes);
		(await fixture.Store.ReadRecoveryEpochAsync(fixture.Location, token)).ShouldBe(Guid.Empty);
	}

	[TestMethod]
	[DataRow((int)FileSystemCommitStep.RollbackSnapshotCreated)]
	[DataRow((int)FileSystemCommitStep.ManagedAssetReplaced)]
	[DataRow((int)FileSystemCommitStep.DatabaseReplaced)]
	public async Task RecoveryFailuresRollbackDatabaseAndAssetsAndRetainValidSafetyBackup(int stepValue)
	{
		var token = this.TestContext.CancellationToken;
		bool armed = false;
		using Fixture fixture = await Fixture.CreateAsync(token, step =>
		{
			if (armed && step == (FileSystemCommitStep)stepValue)
			{
				throw new IOException("Injected recovery failure");
			}
		});
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		armed = true;
		await Should.ThrowAsync<IOException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token));
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token)).ShouldBe(current);
		(await File.ReadAllBytesAsync(fixture.Asset, token)).ShouldBe(Fixture.AfterBytes);
		using BookBackupReview safety = await BookBackup.ReviewFileAsync(fixture.Safety, token);
		safety.SongCount.ShouldBe(2);
		Directory.GetDirectories(fixture.Root, ".*.chordbook-stage-*").ShouldBeEmpty();
	}

	[TestMethod]
	public async Task ExternalEditAfterSafetyBackupIsRetainedAndRecoveryIsRejected()
	{
		var token = this.TestContext.CancellationToken;
		string? asset = null;
		byte[] external = Encoding.UTF8.GetBytes("{title: Other!}\r\n[G]External edit\r\n");
		using Fixture fixture = await Fixture.CreateAsync(token, step =>
		{
			if (step == FileSystemCommitStep.RecoveryBackupCreated)
			{
				File.WriteAllBytes(asset!, external);
			}
		});
		asset = fixture.Asset;
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token));
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token)).ShouldBe(current);
		(await File.ReadAllBytesAsync(asset, token)).ShouldBe(external);
		using BookBackupReview safety = await BookBackup.ReviewFileAsync(fixture.Safety, token);
		safety.SongCount.ShouldBe(2);
	}

	[TestMethod]
	public async Task CancellationAndExistingSafetyFileLeaveBookAndEarlierBackupUntouched()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = await Fixture.CreateAsync(token);
		string current = await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token);
		using BookBackupReview review = await BookBackup.ReviewFileAsync(fixture.Archive, token);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		await Should.ThrowAsync<OperationCanceledException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, cancellation.Token));
		File.Exists(fixture.Safety).ShouldBeFalse();
		await File.WriteAllTextAsync(fixture.Safety, "Keep prior backup", token);
		await Should.ThrowAsync<IOException>(() =>
			BookBackup.ReplaceCurrentAsync(fixture.Store, fixture.Location, review, current, fixture.Safety, fixture.Device, token));
		(await File.ReadAllTextAsync(fixture.Safety, token)).ShouldBe("Keep prior backup");
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Location, token)).ShouldBe(current);
		(await fixture.Store.ReadRecoveryEpochAsync(fixture.Location, token)).ShouldBe(Guid.Empty);
	}

	#endregion

	#region Private Types

	private sealed class Fixture : IDisposable
	{
		internal static readonly byte[] BeforeBytes = Encoding.UTF8.GetBytes("{title: Before}\r\n[C]Original\r\n");
		internal static readonly byte[] AfterBytes = Encoding.UTF8.GetBytes("{title: After!}\r\n[D]Changed!\r\n");

		private Fixture(Action<FileSystemCommitStep>? fault)
		{
			this.Root = Path.Combine(Path.GetTempPath(), nameof(BookRecoveryTests), Guid.NewGuid().ToString("N"));
			this.Store = new(this.Root, fault);
		}

		internal string Root { get; }

		internal FileSystemBookStore Store { get; }

		internal BookLocation Location { get; private set; } = null!;

		internal Guid Device { get; } = Guid.NewGuid();

		internal string Directory => this.Store.GetDirectory(this.Location);

		internal string Asset { get; private set; } = string.Empty;

		internal string Archive => Path.Combine(this.Root, "original.mcbbak");

		internal string Safety => Path.Combine(this.Root, "safety.mcbbak");

		public void Dispose()
		{
			this.Store.Dispose();
			System.IO.Directory.Delete(this.Root, recursive: true);
		}

		internal static async Task<Fixture> CreateAsync(CancellationToken token, Action<FileSystemCommitStep>? fault = null)
		{
			Fixture result = new(fault);
			result.Location = await result.Store.CreateBookAsync("Recovery fixture", result.Device, token);
			await BookImportService.ImportAsync(result.Store, result.Location, "before.cho", new MemoryStream(BeforeBytes), result.Device, token);
			await BookBackup.CreateFileAsync(result.Store, result.Location, result.Archive, token);
			string json = await result.Store.ReadDatabaseJsonAsync(result.Location, token);
			ChordDatabase database = DatabaseJson.Deserialize(json);
			SongFile file = database.SongFiles.Single();
			result.Asset = Path.Combine(result.Directory, file.RelativePath);
			file.ContentHash = SongFileAnalyzer.Hash(AfterBytes);
			file.ObservedLength = AfterBytes.Length;
			file.ContentRevision++;
			await using (IStagedBookWrite write = await result.Store.StageWriteAsync(result.Location, json, token))
			{
				await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(AfterBytes), token);
				await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
				await write.CommitAsync(token);
			}

			await BookImportService.ImportAsync(
				result.Store, result.Location, "extra.cho", new MemoryStream(Encoding.UTF8.GetBytes("{title: Extra}\n[G]Later")), result.Device, token);
			return result;
		}
	}

	#endregion
}