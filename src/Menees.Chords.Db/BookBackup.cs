#region Using Directives

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Creates and restores validated, provider-neutral <c>.mcbbak</c> archives.</summary>
public static class BookBackup
{
	#region Private Data

	private const string DatabaseEntryName = "database.json";
	private const string ManifestEntryName = "manifest.json";
	private static readonly UTF8Encoding Utf8NoBom = new(false, true);
	private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

	#endregion

	#region Public API

	/// <summary>Writes a consistent, validated backup to a new or replaced local file.</summary>
	public static async Task CreateFileAsync(
		IBookStore store,
		BookLocation location,
		string outputPath,
		CancellationToken cancellationToken = default)
	{
		await WriteFileAsync(store, location, outputPath, overwrite: true, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Writes a consistent, validated backup to a stream.</summary>
	public static async Task CreateAsync(
		IBookStore store,
		BookLocation location,
		Stream output,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(output);
		string databaseJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(databaseJson);
		byte[] databaseBytes = Utf8NoBom.GetBytes(databaseJson);
		BookBackupManifest manifest = new();
		manifest.Entries.Add(DatabaseEntryName, SongFileAnalyzer.Hash(databaseBytes));
		using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true);
		await WriteEntryAsync(archive, DatabaseEntryName, databaseBytes, cancellationToken).ConfigureAwait(false);
		foreach (SongFile file in database.SongFiles.OrderBy(file => file.RelativePath, PortableManagedFileName.Comparer))
		{
			await using Stream source = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken).ConfigureAwait(false);
			ZipArchiveEntry entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
			await using Stream destination = entry.Open();
			using SHA256 algorithm = SHA256.Create();
			await using CryptoStream hashing = new(destination, algorithm, CryptoStreamMode.Write, leaveOpen: true);
			await source.CopyToAsync(hashing, cancellationToken).ConfigureAwait(false);
			await hashing.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
			string hash = Convert.ToHexString(algorithm.Hash!).ToLowerInvariant();
			if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
			{
				throw new BookStoreValidationException($"Managed asset '{file.RelativePath}' does not match its database hash.");
			}

			manifest.Entries.Add(file.RelativePath, hash);
		}

		byte[] manifestBytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(manifest, ManifestOptions) + "\n");
		await WriteEntryAsync(archive, ManifestEntryName, manifestBytes, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Validates an archive and restores it as an independent new book.</summary>
	public static async Task<BookLocation> RestoreAsNewAsync(
		IBookStore store,
		Stream input,
		Guid deviceId,
		string? name = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(input);
		using ZipArchive archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
		Dictionary<string, ZipArchiveEntry> payloads = await ReadAndValidateAsync(archive, cancellationToken).ConfigureAwait(false);
		byte[] databaseBytes = await ReadEntryAsync(payloads[DatabaseEntryName], cancellationToken).ConfigureAwait(false);
		ChordDatabase restored = DatabaseJson.Deserialize(Utf8NoBom.GetString(databaseBytes));
		BookLocation location = await store.CreateBookAsync(name ?? restored.Name, deviceId, cancellationToken).ConfigureAwait(false);
		try
		{
			ChordDatabase created = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
			ResetClone(restored, created.Id, name, deviceId);
			await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
			foreach (SongFile file in restored.SongFiles)
			{
				await using Stream content = payloads[file.RelativePath].Open();
				await write.WriteManagedAssetAsync(
					file.Id,
					file.RelativePath,
					content,
					cancellationToken).ConfigureAwait(false);
			}

			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(restored), cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await store.DeleteBookAsync(location, CancellationToken.None).ConfigureAwait(false);
			throw;
		}

		return location;
	}

	/// <summary>Validates and holds a backup file open for review and subsequent recovery.</summary>
	public static async Task<BookBackupReview> ReviewFileAsync(string path, CancellationToken cancellationToken = default)
	{
		FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
		ZipArchive? archive = null;
		try
		{
			archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
			Dictionary<string, ZipArchiveEntry> entries = await ReadAndValidateAsync(archive, cancellationToken).ConfigureAwait(false);
			byte[] json = await ReadEntryAsync(entries[DatabaseEntryName], cancellationToken).ConfigureAwait(false);
			ChordDatabase database = DatabaseJson.Deserialize(Utf8NoBom.GetString(json));
			return new(input, archive, entries, database);
		}
		catch
		{
			archive?.Dispose();
			await input.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>Restores a reviewed matching book through a journaled filesystem transaction with an automatic safety backup.</summary>
	public static async Task ReplaceCurrentAsync(
		FileSystemBookStore store,
		BookLocation location,
		BookBackupReview review,
		string expectedJson,
		string safetyBackupPath,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(review);
		ChordDatabase current = DatabaseJson.Deserialize(expectedJson);
		ChordDatabase restored = review.GetDatabase();
		if (current.Id != restored.Id || current.Id != location.Token)
		{
			throw new BookStoreValidationException("This backup belongs to a different book. Use Restore as New Book.");
		}

		restored.Revision = new()
		{
			Revision = checked(Math.Max(current.Revision.Revision, restored.Revision.Revision) + 1),
			DeviceId = deviceId,
			ModifiedUtc = DateTimeOffset.UtcNow,
		};
		await using IStagedBookWrite write = await store.StageRecoveryAsync(location, expectedJson, safetyBackupPath, cancellationToken).ConfigureAwait(false);
		HashSet<Guid> restoredIds = [.. restored.SongFiles.Select(file => file.Id)];
		foreach (SongFile file in current.SongFiles)
		{
			if (!restoredIds.Contains(file.Id))
			{
				await write.DeleteManagedAssetAsync(file.Id, cancellationToken).ConfigureAwait(false);
			}
		}

		foreach (SongFile file in restored.SongFiles)
		{
			await using Stream content = review.OpenAsset(file.RelativePath);
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, content, cancellationToken).ConfigureAwait(false);
		}

		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(restored), cancellationToken).ConfigureAwait(false);
		await write.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	internal static Task CreateNewFileAsync(
		IBookStore store, BookLocation location, string path, CancellationToken cancellationToken)
		=> WriteFileAsync(store, location, path, overwrite: false, cancellationToken);

	#endregion

	#region Private Methods

	private static async Task WriteFileAsync(
		IBookStore store, BookLocation location, string outputPath, bool overwrite, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
		string fullPath = Path.GetFullPath(outputPath);
		string temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await CreateAsync(store, location, output, cancellationToken).ConfigureAwait(false);
				output.Flush(flushToDisk: true);
			}

			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporary, fullPath, overwrite);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	private static async Task<Dictionary<string, ZipArchiveEntry>> ReadAndValidateAsync(ZipArchive archive, CancellationToken cancellationToken)
	{
		Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = entry.FullName;
			bool allowed = name is DatabaseEntryName or ManifestEntryName || PortableManagedFileName.Validate(name).Count == 0;
			if (!allowed || !entries.TryAdd(name, entry))
			{
				throw new BookStoreValidationException($"Backup contains an unsafe or duplicate entry '{name}'.");
			}
		}

		if (!entries.TryGetValue(ManifestEntryName, out ZipArchiveEntry? manifestEntry)
			|| !entries.TryGetValue(DatabaseEntryName, out ZipArchiveEntry? databaseEntry))
		{
			throw new BookStoreValidationException("Backup must contain database.json and manifest.json.");
		}

		byte[] manifestBytes = await ReadEntryAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
		byte[] databaseBytes = await ReadEntryAsync(databaseEntry, cancellationToken).ConfigureAwait(false);
		BookBackupManifest manifest = JsonSerializer.Deserialize<BookBackupManifest>(manifestBytes, ManifestOptions)
			?? throw new BookStoreValidationException("Backup manifest is invalid.");
		if (manifest.FormatVersion != 1 || manifest.Entries.Count != entries.Count - 1)
		{
			throw new BookStoreValidationException("Backup manifest version or entry count is invalid.");
		}

		foreach ((string name, string expectedHash) in manifest.Entries)
		{
			if (!entries.TryGetValue(name, out ZipArchiveEntry? entry))
			{
				throw new BookStoreValidationException($"Backup entry '{name}' is missing.");
			}

			await using Stream content = entry.Open();
			byte[] hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
			if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(hash), expectedHash))
			{
				throw new BookStoreValidationException($"Backup entry '{name}' is corrupt.");
			}
		}

		ChordDatabase database = DatabaseJson.Deserialize(Utf8NoBom.GetString(databaseBytes));
		HashSet<string> expectedFiles = [.. database.SongFiles.Select(file => file.RelativePath), DatabaseEntryName];
		if (!expectedFiles.SetEquals(manifest.Entries.Keys))
		{
			throw new BookStoreValidationException("Backup payloads do not exactly match database references.");
		}

		return entries;
	}

	private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
	{
		await using Stream input = entry.Open();
		using MemoryStream copy = new();
		await input.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
		return copy.ToArray();
	}

	private static void ResetClone(ChordDatabase database, Guid databaseId, string? name, Guid deviceId)
	{
		RevisionStamp initial = RevisionStamp.Initial(deviceId);
		database.Id = databaseId;
		database.Name = name ?? database.Name;
		database.Revision = initial;
		database.BookSettings.Revision = RevisionStamp.Initial(deviceId);
		database.Tombstones.Clear();
		foreach (SongFile file in database.SongFiles)
		{
			file.ContentRevision = 1;
			file.RecoveryVersion = null;
		}

		foreach (RevisionStamp revision in database.Songs.Select(item => item.Revision)
			.Concat(database.SongFiles.Select(item => item.Revision))
			.Concat(database.Setlists.Select(item => item.Revision))
			.Concat(database.CustomTabs.Select(item => item.Revision))
			.Concat(database.InstrumentProfiles.Select(item => item.Revision))
			.Concat(database.SongInstrumentSettings.Select(item => item.Revision)))
		{
			revision.Revision = initial.Revision;
			revision.ModifiedUtc = initial.ModifiedUtc;
			revision.DeviceId = deviceId;
		}
	}

	private static async Task WriteEntryAsync(
		ZipArchive archive,
		string name,
		byte[] content,
		CancellationToken cancellationToken)
	{
		ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
		await using Stream output = entry.Open();
		await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
	}

	#endregion
}
