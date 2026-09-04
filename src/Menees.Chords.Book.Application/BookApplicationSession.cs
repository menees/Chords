#region Using Directives

using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;
using Menees.Chords.Formatters;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Coordinates portable use cases for the currently active chord book.</summary>
public sealed class BookApplicationSession
{
	#region Private Constants

	private const int CapoMetadataOrder = 0;
	private const int TempoMetadataOrder = 1;
	private const int KeyMetadataOrder = 2;
	private const int OtherMetadataOrder = 3;

	#endregion

	#region Private Data

	private IBookStore? store;
	private BookLocation? location;
	private BookSearchIndex? searchIndex;
	private IReadOnlyDictionary<Guid, SongCatalogItem>? catalogItems;

	#endregion

	#region Public API

	/// <summary>Gets the active database snapshot.</summary>
	public ChordDatabase? Database { get; private set; }

	/// <summary>Activates an already opened book.</summary>
	public async Task ActivateAsync(
		IBookStore store,
		BookLocation location,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(location);
		string json = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(json);
		this.store = store;
		this.location = location;
		this.SetDatabase(database);
	}

	/// <summary>Reloads the active database after an external use case commits changes.</summary>
	public async Task ReloadAsync(CancellationToken cancellationToken = default)
	{
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		this.SetDatabase(DatabaseJson.Deserialize(
			await activeStore.ReadDatabaseJsonAsync(activeLocation, cancellationToken).ConfigureAwait(false)));
	}

	/// <summary>Changes the user-facing name of the active book.</summary>
	public async Task RenameAsync(string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		ChordDatabase database = DatabaseJson.Deserialize(
			await activeStore.ReadDatabaseJsonAsync(activeLocation, cancellationToken).ConfigureAwait(false));
		string trimmedName = name.Trim();
		if (!StringComparer.Ordinal.Equals(database.Name, trimmedName))
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			database.Name = trimmedName;
			database.Revision = new RevisionStamp
			{
				Revision = database.Revision.Revision + 1,
				ModifiedUtc = now,
				DeviceId = deviceId,
			};
			await using IStagedBookWrite write = await activeStore.StageWriteAsync(activeLocation, cancellationToken)
				.ConfigureAwait(false);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
			this.SetDatabase(database);
		}
	}

	/// <summary>Searches the current catalog using the shared normalized metadata index.</summary>
	public IReadOnlyList<SongCatalogItem> Search(string? query, bool includeArchived = false)
	{
		BookSearchIndex index = this.searchIndex ?? throw new InvalidOperationException("The search index is unavailable.");
		IReadOnlyDictionary<Guid, SongCatalogItem> items = this.catalogItems
			?? throw new InvalidOperationException("The catalog is unavailable.");
		return
		[
			.. index.Search(query ?? string.Empty)
				.Select(hit => items[hit.SongId])
				.Where(song => includeArchived || !song.IsArchived),
		];
	}

	/// <summary>Resolves and renders the preferred active file for a song.</summary>
	public async Task<BookSongPresentation> GetPresentationAsync(
		Guid songId,
		CancellationToken cancellationToken = default)
	{
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		Song song = database.Songs.Single(item => item.Id == songId);
		SongFile? file = database.SongFiles
			.Where(item => item.SongId == songId && !item.IsArchived)
			.OrderByDescending(item => item.DisplayPriority)
			.ThenBy(item => item.MediaKind)
			.ThenBy(item => item.Id)
			.FirstOrDefault();
		BookSongPresentation result;
		if (file is null)
		{
			result = new(song.Title, null, null, "<html><body><p>This song has no active file.</p></body></html>");
		}
		else if (file.MediaKind == MediaKind.Pdf)
		{
			result = new(song.Title, file.Id, file.MediaKind, null);
		}
		else
		{
			using Stream stream = await activeStore.OpenManagedAssetAsync(activeLocation, file.Id, cancellationToken)
				.ConfigureAwait(false);
			Document document = Document.Load(stream);
			result = new(song.Title, file.Id, file.MediaKind, new HtmlFormatter(document).ToString());
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static string CreateDisplayText(Song song)
	{
		List<string> segments = [song.Title];
		if (song.Artists.Count > 0)
		{
			segments[0] += " — " + string.Join(", ", song.Artists);
		}

		List<(string Name, string Value)> displayMetadata = [];
		HashSet<string> metadataNames = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string name, List<SourceMetadataValue> metadata) in song.SourceMetadata)
		{
			metadataNames.Add(name);
			if (!IsIdentityMetadata(name))
			{
				string[] values = [.. metadata.Select(value => value.Value).Where(value => !string.IsNullOrWhiteSpace(value))];
				if (values.Length > 0)
				{
					displayMetadata.Add((name, string.Join(", ", values)));
				}
			}
		}

		if (song.Tags.Count > 0 && !metadataNames.Contains("tag") && !metadataNames.Contains("tags"))
		{
			displayMetadata.Add(("tags", string.Join(", ", song.Tags)));
		}

		if (song.DurationSeconds is int durationSeconds && !metadataNames.Contains("duration"))
		{
			displayMetadata.Add((
				"duration",
				TimeSpan.FromSeconds(durationSeconds).ToString(@"m\:ss", CultureInfo.InvariantCulture)));
		}

		string[] displayNames = [.. displayMetadata.Select(metadata => metadata.Name)];
		foreach ((string name, string value) in displayMetadata
			.OrderBy(metadata => GetMetadataOrder(metadata.Name))
			.ThenBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase))
		{
			segments.Add($"{GetCompactMetadataLabel(name, displayNames)}:{value}");
		}

		if (song.IsArchived)
		{
			segments.Add("Archived");
		}

		return string.Join(" · ", segments);
	}

	private static string GetCompactMetadataLabel(string name, IReadOnlyCollection<string> displayedNames)
	{
		string? commonLabel = GetCommonMetadataLabel(name);
		string result = commonLabel ?? name;
		if (commonLabel is null)
		{
			for (int length = 1; length <= name.Length; length++)
			{
				string candidate = name[..length];
				bool conflictsWithCommonLabel = length == 1
					&& (candidate.Equals("C", StringComparison.OrdinalIgnoreCase)
						|| candidate.Equals("T", StringComparison.OrdinalIgnoreCase)
						|| candidate.Equals("K", StringComparison.OrdinalIgnoreCase));
				bool conflictsWithAnotherName = displayedNames.Any(otherName =>
					!otherName.Equals(name, StringComparison.OrdinalIgnoreCase)
					&& otherName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
				if (!conflictsWithCommonLabel && !conflictsWithAnotherName)
				{
					result = candidate;
					break;
				}
			}

			result = char.ToUpperInvariant(result[0]) + result[1..].ToLowerInvariant();
		}

		return result;
	}

	private static string? GetCommonMetadataLabel(string name) => name.ToUpperInvariant() switch
	{
		"CAPO" => "C",
		"BPM" or "TEMPO" or "TEMPOS" => "T",
		"KEY" or "KEYS" => "K",
		_ => null,
	};

	private static int GetMetadataOrder(string name) => GetCommonMetadataLabel(name) switch
	{
		"C" => CapoMetadataOrder,
		"T" => TempoMetadataOrder,
		"K" => KeyMetadataOrder,
		_ => OtherMetadataOrder,
	};

	private static bool IsIdentityMetadata(string name)
		=> name.Equals("title", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("titles", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("t", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("artist", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("artists", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("author", StringComparison.OrdinalIgnoreCase);

	private (IBookStore Store, BookLocation Location) GetActiveBook()
		=> (this.store ?? throw new InvalidOperationException("No book is open."),
			this.location ?? throw new InvalidOperationException("No book is open."));

	private void SetDatabase(ChordDatabase database)
	{
		Dictionary<Guid, int> activeFileCounts = database.SongFiles
			.Where(file => !file.IsArchived)
			.GroupBy(file => file.SongId)
			.ToDictionary(group => group.Key, group => group.Count());
		this.Database = database;
		this.searchIndex = new(database);
		this.catalogItems = database.Songs.ToDictionary(
			song => song.Id,
			song =>
			{
				int activeFileCount = activeFileCounts.GetValueOrDefault(song.Id);
				return new SongCatalogItem(
					song.Id,
					song.Title,
					[.. song.Artists],
					CreateDisplayText(song),
					song.IsArchived,
					activeFileCount,
					song.LastAccessedUtc);
			});
	}

	#endregion
}
