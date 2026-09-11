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
public sealed partial class BookApplicationSession
{
	#region Private Constants

	private const int CapoMetadataOrder = 0;
	private const int TempoMetadataOrder = 1;
	private const int KeyMetadataOrder = 2;
	private const int OtherMetadataOrder = 3;

	#endregion

	#region Private Data

	private readonly SemaphoreSlim mutationLock = new(1, 1);
	private string? committedJson;
	private IBookStore? store;
	private BookLocation? location;
	private BookSearchIndex? searchIndex;
	private IReadOnlyDictionary<Guid, SongCatalogItem>? catalogItems;

	#endregion

	#region Public API

	/// <summary>Gets the active mutable database. Session operations preserve object identity on successful saves.</summary>
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
		this.committedJson = json;
		this.store = store;
		this.location = location;
		this.SetDatabase(database);
	}

	/// <summary>Reloads the active database after an external use case commits changes.</summary>
	public async Task ReloadAsync(CancellationToken cancellationToken = default)
	{
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		string json = await activeStore.ReadDatabaseJsonAsync(activeLocation, cancellationToken).ConfigureAwait(false);
		this.SetDatabase(DatabaseJson.Deserialize(json));
		this.committedJson = json;
	}

	/// <summary>Changes the user-facing name of the active book.</summary>
	public async Task RenameAsync(string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		await this.MutateMetadataAsync(
			(database, now) => database.Name = name.Trim(), deviceId, cancellationToken).ConfigureAwait(false);
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

	/// <summary>Gets the current book's setlists in user-facing order.</summary>
	public IReadOnlyList<SetlistCatalogItem> GetSetlists(bool includeArchived = false)
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		IReadOnlyDictionary<Guid, int?> durations = database.Songs.ToDictionary(song => song.Id, song => song.DurationSeconds);
		return
		[
			.. database.Setlists
				.Where(setlist => includeArchived || !setlist.IsArchived)
				.OrderByDescending(setlist => setlist.Date)
				.ThenBy(setlist => setlist.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(setlist => setlist.Id)
				.Select(setlist => new SetlistCatalogItem(
					setlist.Id,
					setlist.Name,
					setlist.Date,
					setlist.Notes,
					setlist.IsArchived,
					setlist.Entries.Count,
					setlist.Entries.Count(entry => durations[entry.SongId].HasValue),
					setlist.Entries.Sum(entry => durations[entry.SongId] ?? 0))),
		];
	}

	/// <summary>Gets the ordered song occurrences in a setlist.</summary>
	public IReadOnlyList<SetlistEntryCatalogItem> GetSetlistEntries(Guid setlistId)
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		IReadOnlyDictionary<Guid, SongCatalogItem> items = this.catalogItems
			?? throw new InvalidOperationException("The catalog is unavailable.");
		Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
		return
		[
			.. setlist.Entries.Select(entry =>
			{
				SongCatalogItem song = items[entry.SongId];
				return new SetlistEntryCatalogItem(entry.Id, song.Id, song.DisplayText);
			}),
		];
	}

	/// <summary>Creates an empty setlist.</summary>
	public async Task<Guid> CreateSetlistAsync(
		string name,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Guid id = Guid.CreateVersion7();
		await this.MutateMetadataAsync(
			(database, now) => database.Setlists.Add(new Setlist
			{
				Id = id,
				Name = name.Trim(),
				Revision = RevisionStamp.Initial(deviceId, now),
			}),
			deviceId,
			cancellationToken).ConfigureAwait(false);
		return id;
	}

	/// <summary>Renames a setlist.</summary>
	public Task RenameSetlistAsync(
		Guid setlistId,
		string name,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				setlist.Name = name.Trim();
				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);
	}

	/// <summary>Archives or restores a setlist while retaining its ordered entries.</summary>
	public Task SetSetlistArchivedAsync(
		Guid setlistId,
		bool isArchived,
		Guid deviceId,
		CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				setlist.IsArchived = isArchived;
				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);

	/// <summary>Adds another occurrence of a song to the end of a setlist.</summary>
	public async Task<Guid> AddSongToSetlistAsync(
		Guid setlistId,
		Guid songId,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Guid> entryIds = await this.AddSongsToSetlistAsync(
			setlistId,
			[songId],
			deviceId,
			cancellationToken).ConfigureAwait(false);
		return entryIds[0];
	}

	/// <summary>Adds songs to the end of a setlist in the supplied order.</summary>
	public async Task<IReadOnlyList<Guid>> AddSongsToSetlistAsync(
		Guid setlistId,
		IReadOnlyList<Guid> songIds,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		Guid[] entryIds = [];
		if (songIds.Count > 0)
		{
			entryIds = [.. songIds.Select(_ => Guid.CreateVersion7())];
			await this.MutateMetadataAsync(
				(database, now) =>
				{
					HashSet<Guid> knownSongs = [.. database.Songs.Select(song => song.Id)];
					if (songIds.Any(songId => !knownSongs.Contains(songId)))
					{
						throw new KeyNotFoundException("One or more songs no longer exist.");
					}

					Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
					for (int index = 0; index < songIds.Count; index++)
					{
						setlist.Entries.Add(new SetlistEntry { Id = entryIds[index], SongId = songIds[index] });
					}

					setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
				},
				deviceId,
				cancellationToken).ConfigureAwait(false);
		}

		return entryIds;
	}

	/// <summary>Removes one song occurrence from a setlist.</summary>
	public Task RemoveSetlistEntryAsync(
		Guid setlistId,
		Guid entryId,
		Guid deviceId,
		CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				int removed = setlist.Entries.RemoveAll(entry => entry.Id == entryId);
				if (removed == 0)
				{
					throw new KeyNotFoundException("The setlist entry no longer exists.");
				}

				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);

	/// <summary>Moves one setlist entry by one position.</summary>
	public Task MoveSetlistEntryAsync(
		Guid setlistId,
		Guid entryId,
		int offset,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		if (offset is not (-1 or 1))
		{
			throw new ArgumentOutOfRangeException(nameof(offset), "The offset must be -1 or 1.");
		}

		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				int oldIndex = setlist.Entries.FindIndex(entry => entry.Id == entryId);
				if (oldIndex < 0)
				{
					throw new KeyNotFoundException("The setlist entry no longer exists.");
				}

				int newIndex = oldIndex + offset;
				if (newIndex >= 0 && newIndex < setlist.Entries.Count)
				{
					SetlistEntry entry = setlist.Entries[oldIndex];
					setlist.Entries.RemoveAt(oldIndex);
					setlist.Entries.Insert(newIndex, entry);
					setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
				}
				else
				{
					throw new ArgumentOutOfRangeException(nameof(offset), "The entry cannot move beyond the setlist boundary.");
				}
			},
			deviceId,
			cancellationToken,
			refreshSongs: false);
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

	private static RevisionStamp NextRevision(RevisionStamp current, Guid deviceId, DateTimeOffset now) => new()
	{
		Revision = current.Revision + 1,
		ModifiedUtc = now,
		DeviceId = deviceId,
	};

	private (IBookStore Store, BookLocation Location) GetActiveBook()
		=> (this.store ?? throw new InvalidOperationException("No book is open."),
			this.location ?? throw new InvalidOperationException("No book is open."));

	private async Task MutateMetadataAsync(
		Action<ChordDatabase, DateTimeOffset> mutation,
		Guid deviceId,
		CancellationToken cancellationToken,
		bool refreshSongs = false)
	{
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			ChordDatabase database = this.Database!;
			string expectedJson = this.committedJson!;
			try
			{
				DateTimeOffset now = DateTimeOffset.UtcNow;
				mutation(database, now);
				database.Revision = NextRevision(database.Revision, deviceId, now);
				string updatedJson = await activeStore.CommitMetadataAsync(activeLocation, expectedJson, database, cancellationToken).ConfigureAwait(false);
				this.committedJson = updatedJson;
				if (refreshSongs)
				{
					this.SetDatabase(database);
				}
			}
			catch
			{
				// Reconstruct only on failure; normal edits mutate the active objects without cloning.
				this.SetDatabase(DatabaseJson.Deserialize(expectedJson));
				throw;
			}
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

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
