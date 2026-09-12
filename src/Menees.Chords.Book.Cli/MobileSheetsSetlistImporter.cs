#region Using Directives

using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;
using Microsoft.VisualBasic.FileIO;

#endregion

namespace Menees.Chords.Book.Cli;

internal static class MobileSheetsSetlistImporter
{
	#region Public Methods

	public static async Task<MobileSheetsSetlistImportResult> ImportAsync(
		string bookDirectory,
		string extractDirectory,
		Guid deviceId,
		bool apply,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string songsCsv = Path.Combine(extractDirectory, "Songs.csv");
		string setlistsDirectory = Path.Combine(extractDirectory, "SetLists");
		string filesDirectory = Path.Combine(extractDirectory, "Files");
		if (!File.Exists(songsCsv) || !Directory.Exists(setlistsDirectory) || !Directory.Exists(filesDirectory))
		{
			throw new DirectoryNotFoundException("The extract must contain Songs.csv, SetLists, and Files.");
		}

		Dictionary<int, LegacySong> legacySongs = ReadLegacySongs(songsCsv);
		List<LegacySetlist> legacySetlists = ReadLegacySetlists(setlistsDirectory);
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		Dictionary<int, Guid> songIds = ResolveSongIds(database, legacySongs);
		int[] requiredIds = [.. legacySetlists.SelectMany(setlist => setlist.SongIds).Distinct()];
		int[] missingIds = [.. requiredIds.Where(id => !songIds.TryGetValue(id, out _))];
		string[] missingPaths =
		[
			.. missingIds.Select(id => legacySongs.TryGetValue(id, out LegacySong? song)
				? Path.Combine(filesDirectory, song.FileName)
				: throw new InvalidDataException($"Setlist entry {id} is absent from Songs.csv.")),
		];
		string? missingFile = missingPaths.FirstOrDefault(path => !File.Exists(path));
		if (missingFile is not null)
		{
			throw new FileNotFoundException("A setlist song is absent from the extract's Files folder.", missingFile);
		}

		int importedSongCount = missingPaths.Length;
		if (apply && missingPaths.Length > 0)
		{
			IReadOnlyList<BookImportResult> imported = await BookImportService.ImportFilesAsync(
				store,
				location,
				missingPaths,
				deviceId,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			if (imported.Count != missingPaths.Length)
			{
				throw new InvalidDataException("Not every missing setlist song was imported.");
			}

			database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
			songIds = ResolveSongIds(database, legacySongs);
		}

		if (requiredIds.Any(id => !songIds.TryGetValue(id, out _)) && apply)
		{
			throw new InvalidDataException("Not every setlist entry could be matched to an imported song.");
		}

		List<Setlist> additions = [];
		int existingSetlistCount = 0;
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (LegacySetlist legacySetlist in legacySetlists)
		{
			Guid[] orderedSongIds = apply
				? [.. legacySetlist.SongIds.Select(id => songIds[id])]
				: [];
			Setlist? existing = database.Setlists.SingleOrDefault(
				setlist => setlist.Name.Equals(legacySetlist.Name, StringComparison.OrdinalIgnoreCase));
			if (existing is null)
			{
				additions.Add(new Setlist
				{
					Id = Guid.CreateVersion7(),
					Name = legacySetlist.Name,
					Date = TryGetDate(legacySetlist.Name),
					Entries =
					[
						.. orderedSongIds.Select(songId => new SetlistEntry { Id = Guid.CreateVersion7(), SongId = songId }),
					],
					Revision = RevisionStamp.Initial(deviceId, now),
				});
			}
			else if (!apply || existing.Entries.Select(entry => entry.SongId).SequenceEqual(orderedSongIds))
			{
				existingSetlistCount++;
			}
			else
			{
				throw new InvalidDataException($"Setlist '{legacySetlist.Name}' already exists with different entries.");
			}
		}

		if (apply && additions.Count > 0)
		{
			database.Setlists.AddRange(additions);
			database.Revision = NextRevision(database.Revision, deviceId, now);
			DatabaseValidation.ThrowIfInvalid(database);
			await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		}

		int entryCount = legacySetlists.Sum(setlist => setlist.SongIds.Count);
		return new(legacySetlists.Count, entryCount, importedSongCount, additions.Count, existingSetlistCount, apply);
	}

	#endregion

	#region Private Methods

	private static FileSystemBookStore CreateStore(string bookDirectory)
	{
		string fullPath = Path.GetFullPath(bookDirectory);
		string root = Path.GetDirectoryName(fullPath)
			?? throw new ArgumentException("Book folder must have a parent directory.", nameof(bookDirectory));
		return new(root);
	}

	private static string GetOriginalFileName(SongFile file)
	{
		string marker = $" [{file.Id:D}]";
		int index = file.RelativePath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
		return index >= 0
			? file.RelativePath.Remove(index, marker.Length)
			: throw new InvalidDataException($"Managed file '{file.RelativePath}' has no expected GUID suffix.");
	}

	private static RevisionStamp NextRevision(RevisionStamp current, Guid deviceId, DateTimeOffset now) => new()
	{
		Revision = current.Revision + 1,
		ModifiedUtc = now,
		DeviceId = deviceId,
	};

	private static Dictionary<int, LegacySong> ReadLegacySongs(string path)
	{
		List<IReadOnlyDictionary<string, string>> rows = ReadRows(path);
		Dictionary<int, LegacySong> result = [];
		foreach (IReadOnlyDictionary<string, string> row in rows)
		{
			if (int.TryParse(row["Id"], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
			{
				result.Add(id, new(row["FileName"]));
			}
		}

		return result;
	}

	private static List<LegacySetlist> ReadLegacySetlists(string directory)
	{
		List<LegacySetlist> result = [];
		foreach (string path in Directory.EnumerateFiles(directory, "*.csv", System.IO.SearchOption.TopDirectoryOnly)
			.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
		{
			List<int> songIds =
			[
				.. ReadRows(path).Select(row => int.Parse(row["Id"], NumberStyles.None, CultureInfo.InvariantCulture)),
			];
			result.Add(new(Path.GetFileNameWithoutExtension(path), songIds));
		}

		return result;
	}

	private static List<IReadOnlyDictionary<string, string>> ReadRows(string path)
	{
		using TextFieldParser parser = new(path, Encoding.UTF8, detectEncoding: true)
		{
			TextFieldType = FieldType.Delimited,
			HasFieldsEnclosedInQuotes = true,
			TrimWhiteSpace = false,
		};
		parser.SetDelimiters(",");
		string[] headers = parser.ReadFields() ?? throw new InvalidDataException($"CSV '{path}' has no header row.");
		List<IReadOnlyDictionary<string, string>> result = [];
		while (!parser.EndOfData)
		{
			string[] fields = parser.ReadFields() ?? [];
			if (fields.Length != headers.Length)
			{
				throw new InvalidDataException($"CSV '{path}' contains a row with {fields.Length} fields; expected {headers.Length}.");
			}

			result.Add(headers.Select((header, index) => new KeyValuePair<string, string>(header, fields[index]))
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
		}

		return result;
	}

	private static Dictionary<int, Guid> ResolveSongIds(
		ChordDatabase database,
		Dictionary<int, LegacySong> legacySongs)
	{
		Dictionary<string, Guid> managedSongs = new(StringComparer.OrdinalIgnoreCase);
		foreach (SongFile file in database.SongFiles)
		{
			string originalName = GetOriginalFileName(file);
			if (managedSongs.TryGetValue(originalName, out Guid existingSongId) && existingSongId != file.SongId)
			{
				throw new InvalidDataException($"Multiple songs map to original filename '{originalName}'.");
			}

			managedSongs[originalName] = file.SongId;
		}

		Dictionary<int, Guid> result = [];
		foreach ((int id, LegacySong song) in legacySongs)
		{
			if (managedSongs.TryGetValue(song.FileName, out Guid songId))
			{
				result.Add(id, songId);
			}
		}

		return result;
	}

	private static DateOnly? TryGetDate(string name)
	{
		DateOnly? result = null;
		foreach (string token in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
			{
				result = date;
				break;
			}
		}

		return result;
	}

	#endregion

	#region Private Types

	private sealed record LegacySetlist(string Name, IReadOnlyList<int> SongIds);

	private sealed record LegacySong(string FileName);

	#endregion
}
