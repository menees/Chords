#region Using Directives

using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookBackupTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RestoreAsNewPreservesEntitiesAndBytesButChangesBookIdentity()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid sourceDevice = Guid.NewGuid();
		BookLocation source = await store.CreateBookAsync("Source", sourceDevice, cancellationToken);
		BookImportResult imported = await BookImportService.ImportAsync(
			store,
			source,
			"extensionless opensong",
			new MemoryStream(TestData.OpenSongBytes(), writable: false),
			sourceDevice,
			cancellationToken);
		ChordDatabase sourceDatabase = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(source, cancellationToken));
		using MemoryStream backup = new();
		await BookBackup.CreateAsync(store, source, backup, cancellationToken);
		backup.Position = 0;

		Guid restoreDevice = Guid.NewGuid();
		BookLocation restoredLocation = await BookBackup.RestoreAsNewAsync(
			store,
			backup,
			restoreDevice,
			"Restored",
			cancellationToken);

		ChordDatabase restored = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(restoredLocation, cancellationToken));
		restored.Id.ShouldNotBe(sourceDatabase.Id);
		restored.Name.ShouldBe("Restored");
		restored.Songs.Single().Id.ShouldBe(sourceDatabase.Songs.Single().Id);
		restored.SongFiles.Single().Id.ShouldBe(imported.SongFileId);
		restored.Revision.DeviceId.ShouldBe(restoreDevice);
		using Stream restoredContent = await store.OpenManagedAssetAsync(restoredLocation, imported.SongFileId, cancellationToken);
		using MemoryStream copy = new();
		await restoredContent.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
	}

	[TestMethod]
	[DataRow("corrupt")]
	[DataRow("duplicate")]
	[DataRow("unsafe")]
	public async Task InvalidArchiveDoesNotCreateABookOrChangeTheOriginal(string fault)
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookBackupTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Original", device, token);
			await BookImportService.ImportAsync(store, location, "song", new MemoryStream(TestData.OpenSongBytes()), device, token);
			string original = await store.ReadDatabaseJsonAsync(location, token);
			using MemoryStream archive = new();
			await BookBackup.CreateAsync(store, location, archive, token);
			archive.Position = 0;
			using (ZipArchive zip = new(archive, ZipArchiveMode.Update, leaveOpen: true))
			{
				string entryName = fault == "unsafe" ? "../outside.txt" : "database.json";
				if (fault == "corrupt")
				{
					zip.GetEntry(entryName)!.Delete();
				}

				using Stream output = zip.CreateEntry(entryName).Open();
				output.Write([1, 2, 3]);
			}

			archive.Position = 0;
			await Should.ThrowAsync<BookStoreValidationException>(() => BookBackup.RestoreAsNewAsync(store, archive, device, cancellationToken: token));
			(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(original);
			Directory.GetDirectories(root).Length.ShouldBe(1);
			File.Exists(Path.Combine(root, "outside.txt")).ShouldBeFalse();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task CanceledFileBackupRetainsPriorArchiveAndRemovesItsStage()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookBackupTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			InMemoryBookStore store = new();
			BookLocation location = await store.CreateBookAsync("Book", Guid.NewGuid(), token);
			string path = Path.Combine(root, "book.mcbbak");
			byte[] previous = [1, 2, 3];
			await File.WriteAllBytesAsync(path, previous, token);
			using CancellationTokenSource cancellation = new();
			cancellation.Cancel();
			await Should.ThrowAsync<OperationCanceledException>(() => BookBackup.CreateFileAsync(store, location, path, cancellation.Token));
			(await File.ReadAllBytesAsync(path, token)).ShouldBe(previous);
			Directory.GetFiles(root).ShouldBe([path]);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FileBackupIgnoresLockedUnrelatedContentAndIncludesExpandedBookSettings()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookBackupTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Original", device, token);
			await BookImportService.ImportAsync(store, location, "song", new MemoryStream(TestData.OpenSongBytes()), device, token);
			string baseline = await store.ReadDatabaseJsonAsync(location, token);
			ChordDatabase database = DatabaseJson.Deserialize(baseline);
			database.BookSettings.DefaultMetronome.Sound = "Cowbell";
			database.BookSettings.DefaultMetronome.Subdivision = 3;
			database.BookSettings.StopMetronomeOnSetlistTransition = false;
			await store.CommitMetadataAsync(location, baseline, database, token);
			string note = Path.Combine(store.GetDirectory(location), "unrelated.txt");
			await File.WriteAllTextAsync(note, "Leave this alone.", token);
			using FileStream locked = new(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			string backup = Path.Combine(root, "book.mcbbak");
			await BookBackup.CreateFileAsync(store, location, backup, token);
			using FileStream input = File.OpenRead(backup);
			using (ZipArchive zip = new(input, ZipArchiveMode.Read, leaveOpen: true))
			{
				zip.GetEntry("unrelated.txt").ShouldBeNull();
				zip.Entries.Count.ShouldBe(3);
			}

			input.Position = 0;
			BookLocation restored = await BookBackup.RestoreAsNewAsync(store, input, device, cancellationToken: token);
			BookSettings settings = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(restored, token)).BookSettings;
			settings.DefaultMetronome.Sound.ShouldBe("Cowbell");
			settings.DefaultMetronome.Subdivision.ShouldBe(3);
			settings.StopMetronomeOnSetlistTransition.ShouldBeFalse();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	#endregion
}
