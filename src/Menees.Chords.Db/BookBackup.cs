#region Using Directives

using System.IO;
using System.IO.Compression;
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
		ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
		string fullPath = Path.GetFullPath(outputPath);
		string temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await CreateAsync(store, location, output, cancellationToken).ConfigureAwait(false);
			}

			File.Move(temporary, fullPath, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
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
			using MemoryStream copy = new();
			await source.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
			byte[] bytes = copy.ToArray();
			string hash = SongFileAnalyzer.Hash(bytes);
			if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
			{
				throw new BookStoreValidationException($"Managed asset '{file.RelativePath}' does not match its database hash.");
			}

			manifest.Entries.Add(file.RelativePath, hash);
			await WriteEntryAsync(archive, file.RelativePath, bytes, cancellationToken).ConfigureAwait(false);
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
		Dictionary<string, byte[]> payloads = await ReadAndValidateAsync(input, cancellationToken).ConfigureAwait(false);
		ChordDatabase restored = DatabaseJson.Deserialize(Utf8NoBom.GetString(payloads[DatabaseEntryName]));
		BookLocation location = await store.CreateBookAsync(name ?? restored.Name, deviceId, cancellationToken).ConfigureAwait(false);
		try
		{
			ChordDatabase created = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
			ResetClone(restored, created.Id, name, deviceId);
			await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
			foreach (SongFile file in restored.SongFiles)
			{
				await write.WriteManagedAssetAsync(
					file.Id,
					file.RelativePath,
					new MemoryStream(payloads[file.RelativePath], writable: false),
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

	#endregion

	#region Private Methods

	private static async Task<Dictionary<string, byte[]>> ReadAndValidateAsync(Stream input, CancellationToken cancellationToken)
	{
		Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
		using ZipArchive archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = entry.FullName;
			bool allowed = name is DatabaseEntryName or ManifestEntryName || PortableManagedFileName.Validate(name).Count == 0;
			if (!allowed || !entries.TryAdd(name, await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false)))
			{
				throw new BookStoreValidationException($"Backup contains an unsafe or duplicate entry '{name}'.");
			}
		}

		if (!entries.TryGetValue(ManifestEntryName, out byte[]? manifestBytes)
			|| !entries.TryGetValue(DatabaseEntryName, out byte[]? databaseBytes))
		{
			throw new BookStoreValidationException("Backup must contain database.json and manifest.json.");
		}

		BookBackupManifest manifest = JsonSerializer.Deserialize<BookBackupManifest>(manifestBytes, ManifestOptions)
			?? throw new BookStoreValidationException("Backup manifest is invalid.");
		if (manifest.FormatVersion != 1 || manifest.Entries.Count != entries.Count - 1)
		{
			throw new BookStoreValidationException("Backup manifest version or entry count is invalid.");
		}

		foreach ((string name, string expectedHash) in manifest.Entries)
		{
			if (!entries.TryGetValue(name, out byte[]? bytes)
				|| !StringComparer.OrdinalIgnoreCase.Equals(SongFileAnalyzer.Hash(bytes), expectedHash))
			{
				throw new BookStoreValidationException($"Backup entry '{name}' is missing or corrupt.");
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
