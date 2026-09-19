#region Using Directives

using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;
using Menees.Chords.Transformers;

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
	private readonly RenderedSongCache renderedSongs = new();
	private Dictionary<(Guid Song, Guid Instrument), SongInstrumentSetting> instrumentSettings = [];
	private string? committedJson;
	private IBookStore? store;
	private BookLocation? location;
	private BookSearchIndex? searchIndex;
	private Dictionary<Guid, SongCatalogItem>? catalogItems;

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

	/// <summary>Streams a validated backup while serializing application mutations.</summary>
	public async Task CreateBackupAsync(string outputPath, CancellationToken cancellationToken = default)
	{
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var (activeStore, activeLocation) = this.GetActiveBook();
			await BookBackup.CreateFileAsync(activeStore, activeLocation, outputPath, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

	/// <summary>Replaces a reviewed filesystem book, then refreshes all derived application state.</summary>
	public async Task ReplaceFromBackupAsync(
		BookBackupReview review, long expectedRevision, string safetyBackupPath, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(review);
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var (activeStore, activeLocation) = this.GetActiveBook();
			if (activeStore is not FileSystemBookStore fileStore)
			{
				throw new NotSupportedException("Book replacement requires a filesystem recovery transaction.");
			}

			if (this.Database!.Id != review.BookId || this.Database.Revision.Revision != expectedRevision)
			{
				throw new BookStoreConcurrencyException();
			}

			await BookBackup.ReplaceCurrentAsync(
				fileStore, activeLocation, review, this.committedJson!, safetyBackupPath, deviceId, cancellationToken).ConfigureAwait(false);

			// Replacement is committed. Finish rebuilding catalog/render state even if cancellation arrives now.
			await this.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

	/// <summary>Searches the current catalog using the shared normalized metadata index.</summary>
	public IReadOnlyList<SongCatalogItem> Search(string? query, bool includeArchived = false)
	{
		BookSearchIndex index = this.searchIndex ?? throw new InvalidOperationException("The search index is unavailable.");
		Dictionary<Guid, SongCatalogItem> items = this.catalogItems
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
		Dictionary<Guid, int?> durations = database.Songs.ToDictionary(song => song.Id, song => song.DurationSeconds);
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
		Dictionary<Guid, SongCatalogItem> items = this.catalogItems
			?? throw new InvalidOperationException("The catalog is unavailable.");
		Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
		Dictionary<Guid, string> instruments = database.InstrumentProfiles.ToDictionary(profile => profile.Id, profile => profile.Name);
		return
		[
			.. setlist.Entries.Select(entry =>
			{
				SongCatalogItem song = items[entry.SongId];
				string display = song.DisplayText;
				if (entry.InstrumentProfileId is Guid profileId && instruments.TryGetValue(profileId, out string? name))
				{
					display += " · " + name;
				}

				if (entry.PreferredSongFileId is not null)
				{
					display += " · Sheet override";
				}

				if (entry.TransposeSemitones is int transpose)
				{
					display += FormattableString.Invariant($" · Transpose {transpose:+0;-0;0}");
				}

				return new SetlistEntryCatalogItem(entry.Id, song.Id, display);
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

	/// <summary>Gets an entry settings snapshot for an optimistic concurrency checked edit.</summary>
	public SetlistEntrySettings GetSetlistEntrySettings(Guid setlistId, Guid entryId)
	{
		Setlist setlist = this.Database!.Setlists.Single(item => item.Id == setlistId);
		SetlistEntry entry = setlist.Entries.Single(item => item.Id == entryId);
		return new(setlistId, entryId, entry.SongId, setlist.Revision.Revision, entry.PreferredSongFileId, entry.TransposeSemitones, entry.InstrumentProfileId);
	}

	public Task SaveSetlistEntrySettingsAsync(
		SetlistEntrySettings original, Guid? preferredFileId, int? transpose, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.SaveSetlistEntrySettingsAsync(original, preferredFileId, transpose, original.InstrumentProfileId, deviceId, cancellationToken);

	public Task SaveSetlistEntrySettingsAsync(
		SetlistEntrySettings original,
		Guid? preferredFileId,
		int? transpose,
		Guid? instrumentId,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		const int MaximumTranspose = 24;
		if (transpose is < -MaximumTranspose or > MaximumTranspose)
		{
			throw new ArgumentOutOfRangeException(nameof(transpose), "Use a transposition from -24 to +24 semitones.");
		}

		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == original.SetlistId);
				if (setlist.Revision.Revision != original.SetlistRevision)
				{
					throw new InvalidOperationException("This setlist changed. Reopen its entry settings before saving.");
				}

				SetlistEntry entry = setlist.Entries.Single(item => item.Id == original.EntryId);
				if (preferredFileId is Guid fileId && !database.SongFiles.Any(file => file.Id == fileId && file.SongId == entry.SongId
					&& !file.IsArchived && file.RecoveryVersion is null))
				{
					throw new ArgumentException("Choose an active sheet belonging to this song.", nameof(preferredFileId));
				}

				if (instrumentId is Guid profileId && !database.InstrumentProfiles.Any(profile => profile.Id == profileId))
				{
					throw new ArgumentException("Choose an instrument from this book.", nameof(instrumentId));
				}

				entry.InstrumentProfileId = instrumentId;
				entry.PreferredSongFileId = preferredFileId;
				entry.TransposeSemitones = transpose;
				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);
	}

	/// <summary>Resolves song overrides over book display defaults.</summary>
	public DisplayProfile GetDisplaySettings(Guid? songId = null)
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		DisplayOverride? patch = songId is Guid id ? database.Songs.Single(song => song.Id == id).DisplayOverride : null;
		return SongDisplaySettings.Resolve(database.BookSettings.DefaultDisplayProfile, patch);
	}

	public Task SaveDisplaySettingsAsync(Guid? songId, DisplayProfile? profile, Guid deviceId, CancellationToken cancellationToken = default)
	{
		if (profile is not null)
		{
			SongDisplaySettings.Validate(profile);
		}

		if (songId is null && profile is null)
		{
			throw new ArgumentException("Book defaults require a display profile.", nameof(profile));
		}

		return this.MutateMetadataAsync(
			(database, now) =>
			{
				if (songId is Guid id)
				{
					Song song = database.Songs.Single(item => item.Id == id);
					DisplayOverride? patch = profile is null ? null : SongDisplaySettings.CreatePatch(profile, database.BookSettings.DefaultDisplayProfile);
					song.DisplayOverride = patch?.HasValues == true ? patch : null;
					song.Revision = NextRevision(song.Revision, deviceId, now);
				}
				else
				{
					database.BookSettings.DefaultDisplayProfile = SongDisplaySettings.Resolve(profile!, null);
					database.BookSettings.Revision = NextRevision(database.BookSettings.Revision, deviceId, now);
				}
			},
			deviceId,
			cancellationToken,
			refreshPredicatesFor: songId);
	}

	public Task<BookSongPresentation> GetPresentationAsync(
		Guid songId, Guid? setlistId, Guid? entryId, Guid? activeInstrumentId, CancellationToken cancellationToken)
		=> this.GetPresentationAsync(songId, setlistId, entryId, activeInstrumentId, null, 0, cancellationToken);

	/// <summary>Resolves and renders the preferred active file for a song.</summary>
	public async Task<BookSongPresentation> GetPresentationAsync(
		Guid songId,
		Guid? setlistId = null,
		Guid? entryId = null,
		Guid? activeInstrumentId = null,
		string? notationOverride = null,
		int transposeOffset = 0,
		CancellationToken cancellationToken = default)
	{
		const int MaximumQuickTranspose = 11;
		ArgumentOutOfRangeException.ThrowIfLessThan(transposeOffset, -MaximumQuickTranspose);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(transposeOffset, MaximumQuickTranspose);
		cancellationToken.ThrowIfCancellationRequested();
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		Song song = database.Songs.Single(item => item.Id == songId);
		SetlistEntry? entry = setlistId is Guid listId && entryId is Guid itemId
			? database.Setlists.Single(list => list.Id == listId).Entries.Single(item => item.Id == itemId && item.SongId == songId) : null;
		SongFile[] activeFiles = [.. this.GetOrderedSongFiles(songId).Where(item => !item.IsArchived && item.RecoveryVersion is null)];
		SongFile? file = activeFiles.FirstOrDefault();
		Guid? instrumentId = entry?.InstrumentProfileId ?? activeInstrumentId;
		InstrumentProfile? instrument = instrumentId is Guid profileId ? database.InstrumentProfiles.Single(item => item.Id == profileId) : null;
		SongInstrumentSetting? setting = instrumentId is Guid selectedId ? this.GetInstrumentSetting(songId, selectedId) : null;
		if (setting?.PreferredSongFileId is Guid instrumentFileId)
		{
			file = activeFiles.FirstOrDefault(item => item.Id == instrumentFileId) ?? file;
		}

		if (entry?.PreferredSongFileId is Guid entryFileId)
		{
			file = activeFiles.FirstOrDefault(item => item.Id == entryFileId) ?? file;
		}

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
			DisplayProfile profile = SongDisplaySettings.Resolve(database.BookSettings.DefaultDisplayProfile, song.DisplayOverride);
			if (notationOverride is not null)
			{
				profile.NotationSystem = notationOverride;
			}

			SongDisplaySettings.Validate(profile);
			int transpose = transposeOffset + PerformanceTranspose.GetShownSemitones(
				entry?.TransposeSemitones ?? setting?.TransposeSemitones ?? 0,
				setting?.CapoFret,
				setting?.CapoBehavior == CapoBehavior.AffectsShownChords);
			AccidentalPreference spelling = InstrumentSettingsService.ParseSpelling(setting?.ChordSpellingPreference);
			string cacheKey = System.Text.Json.JsonSerializer.Serialize(new
			{
				Book = database.Id, File = file.Id, file.ContentHash, file.ContentRevision, Profile = profile, Transpose = transpose, Spelling = spelling,
				BaseTranspose = transpose - transposeOffset,
			});
			RenderedSong? rendered = this.renderedSongs.Get(cacheKey);
			if (rendered is null)
			{
				using Stream stream = await activeStore.OpenManagedAssetAsync(activeLocation, file.Id, cancellationToken).ConfigureAwait(false);
				Document document = Document.Load(stream);
				Key? key = Key.Find(document, DetectKey.FirstChord);
				if (key is not null && (transpose != 0 || spelling != AccidentalPreference.Default))
				{
					document = new TransposeTransformer(document, checked((sbyte)transpose), key, spelling).Transform().Document;
				}

				cancellationToken.ThrowIfCancellationRequested();
				string html = SongDisplaySettings.Render(document, profile);
				string? baseKey = key is null ? null
					: Chord.Parse(key.Name).Transpose(checked((sbyte)(transpose - transposeOffset)), spelling).Name;
				rendered = new(html, baseKey);
				this.renderedSongs.Put(cacheKey, rendered);
			}

			result = new(song.Title, file.Id, file.MediaKind, rendered.Html)
			{
				OriginalKey = rendered.OriginalKey,
			};
		}

		string? description = instrument?.Name;
		if (instrument is not null)
		{
			int soundingTranspose = entry?.TransposeSemitones ?? setting?.TransposeSemitones ?? 0;
			description += FormattableString.Invariant($" · Transpose {soundingTranspose:+0;-0;0}");
			if (setting?.CapoFret is int capo)
			{
				description += $" · Capo {capo} ({(setting.CapoBehavior == CapoBehavior.AffectsShownChords ? "chord shapes" : "display only")})";
			}

			if (file?.MediaKind == MediaKind.Pdf)
			{
				description += " · PDF chords unchanged";
			}
		}

		return result with { PerformanceDescription = description };
	}

	internal static RevisionStamp NextRevision(RevisionStamp current, Guid deviceId, DateTimeOffset now) => new()
	{
		Revision = current.Revision + 1,
		ModifiedUtc = now,
		DeviceId = deviceId,
	};

	internal async Task MutateMetadataAsync(
		Action<ChordDatabase, DateTimeOffset> mutation,
		Guid deviceId,
		CancellationToken cancellationToken,
		bool refreshSongs = false,
		Guid? refreshPredicatesFor = null)
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
				else if (refreshPredicatesFor is Guid songId)
				{
					Song song = database.Songs.Single(item => item.Id == songId);
					this.searchIndex!.RefreshSongPredicates(song);
					SongCatalogItem previous = this.catalogItems![songId];
					this.catalogItems[songId] = CreateCatalogItem(song, previous.ActiveFileCount, previous.ArchivedFileCount, previous.RecoveryFileCount);
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

	internal SongInstrumentSetting? GetInstrumentSetting(Guid songId, Guid instrumentId)
		=> this.instrumentSettings.GetValueOrDefault((songId, instrumentId));

	internal void SetInstrumentSetting(Guid songId, Guid instrumentId, SongInstrumentSetting? setting)
	{
		if (setting is null)
		{
			this.instrumentSettings.Remove((songId, instrumentId));
		}
		else
		{
			this.instrumentSettings[(songId, instrumentId)] = setting;
		}
	}

	#endregion

	#region Private Methods

	private static SongCatalogItem CreateCatalogItem(Song song, int active, int archived, int recovery)
	{
		List<string> parts = [CreateDisplayText(song)];
		if (active > 1)
		{
			parts.Add($"{active} sheets");
		}

		if (archived > 0)
		{
			parts.Add($"{archived} archived sheets");
		}

		if (recovery > 0)
		{
			parts.Add($"{recovery} recovery sheets");
		}

		bool display = song.DisplayOverride?.HasValues == true;
		bool metronome = song.MetronomeOverride is not null;
		if (display)
		{
			parts.Add("Display override");
		}

		if (metronome)
		{
			parts.Add("Metronome override");
		}

		return new(
			song.Id,
			song.Title,
			[.. song.Artists],
			string.Join(" · ", parts),
			song.IsArchived,
			active,
			song.LastAccessedUtc,
			archived,
			recovery,
			display,
			metronome);
	}

	private static string CreateDisplayText(Song song)
	{
		List<string> segments = [song.Title];
		if (song.Artists.Count > 0)
		{
			segments[0] += " — " + string.Join(", ", song.Artists);
		}

		List<(string Name, string Value)> displayMetadata = [];
		HashSet<string> metadataNames = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string name, IReadOnlyList<string> metadata) in SongMetadata.Enumerate(song))
		{
			metadataNames.Add(name);
			if (!IsIdentityMetadata(name))
			{
				string[] values = [.. metadata.Where(value => !string.IsNullOrWhiteSpace(value))];
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
		ILookup<Guid, SongFile> files = database.SongFiles.ToLookup(file => file.SongId);
		this.Database = database;
		this.instrumentSettings = database.SongInstrumentSettings.ToDictionary(item => (item.SongId, item.InstrumentProfileId));
		this.searchIndex = new(database);
		this.catalogItems = database.Songs.ToDictionary(
			song => song.Id,
			song =>
			{
				int active = 0;
				int archived = 0;
				int recovery = 0;
				foreach (SongFile file in files[song.Id])
				{
					active += !file.IsArchived && file.RecoveryVersion is null ? 1 : 0;
					archived += file.IsArchived ? 1 : 0;
					recovery += file.RecoveryVersion is not null ? 1 : 0;
				}

				return CreateCatalogItem(song, active, archived, recovery);
			});
	}

	#endregion
}
