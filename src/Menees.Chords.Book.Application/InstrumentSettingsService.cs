#region Using Directives

using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Edits instrument metadata through the session's serialized, failure-safe commit path.</summary>
public sealed class InstrumentSettingsService
{
	#region Private Data

	private readonly BookApplicationSession session;

	#endregion

	#region Constructors

	public InstrumentSettingsService(BookApplicationSession session) => this.session = session;

	#endregion

	#region Public Methods

	public IReadOnlyList<InstrumentProfileInfo> GetProfiles()
	{
		ChordDatabase database = this.GetDatabase();
		return [.. database.InstrumentProfiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(profile => new InstrumentProfileInfo(database.Id, profile.Id, profile.Name, profile.Revision.Revision))];
	}

	/// <summary>Adds initial choices without recreating deliberately deleted profiles or touching sheet assets.</summary>
	public async Task EnsureDefaultsAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		ChordDatabase current = this.GetDatabase();
		if (!current.BookSettings.StandardInstrumentsInitialized)
		{
			await this.session.MutateMetadataAsync(
				(database, now) =>
				{
					HashSet<Guid> deleted = [.. database.Tombstones
						.Where(item => item.EntityType == nameof(InstrumentProfile)).Select(item => item.EntityId)];
					foreach ((Guid id, string name) in StandardInstruments.All)
					{
						if (!deleted.Contains(id) && !database.InstrumentProfiles.Any(profile => profile.Id == id
							|| profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
						{
							database.InstrumentProfiles.Add(new() { Id = id, Name = name, Revision = RevisionStamp.Initial(deviceId, now) });
						}
					}

					database.BookSettings.StandardInstrumentsInitialized = true;
					if (database.BookSettings.DefaultInstrumentId is null)
					{
						database.BookSettings.DefaultInstrumentId = database.InstrumentProfiles
							.FirstOrDefault(profile => profile.Name.Equals("Acoustic Guitar", StringComparison.OrdinalIgnoreCase))?.Id
							?? database.InstrumentProfiles.FirstOrDefault()?.Id;
					}

					database.BookSettings.Revision = BookApplicationSession.NextRevision(database.BookSettings.Revision, deviceId, now);
				},
				deviceId,
				cancellationToken).ConfigureAwait(false);
		}
	}

	public IReadOnlyList<InstrumentProfileInfo> GetAssignedProfiles(Guid songId)
	{
		ChordDatabase database = this.GetDatabase();
		return [.. database.InstrumentProfiles.Where(profile => this.session.GetInstrumentSetting(songId, profile.Id) is not null)
			.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(profile => new InstrumentProfileInfo(database.Id, profile.Id, profile.Name, profile.Revision.Revision))];
	}

	/// <summary>Uses a selected assigned instrument, the assigned book default, or the first assigned instrument.</summary>
	public Guid? ResolveInstrument(Guid songId, Guid? preferred = null)
	{
		ChordDatabase database = this.GetDatabase();
		InstrumentProfile? firstAssigned = null;
		foreach (InstrumentProfile profile in database.InstrumentProfiles)
		{
			if (this.session.GetInstrumentSetting(songId, profile.Id) is not null
				&& (firstAssigned is null || StringComparer.CurrentCultureIgnoreCase.Compare(profile.Name, firstAssigned.Name) < 0))
			{
				firstAssigned = profile;
			}
		}

		Guid? result = preferred ?? database.BookSettings.DefaultInstrumentId;
		if (firstAssigned is not null && !(result is Guid selected && this.session.GetInstrumentSetting(songId, selected) is not null))
		{
			result = database.BookSettings.DefaultInstrumentId is Guid bookDefault && this.session.GetInstrumentSetting(songId, bookDefault) is not null
				? bookDefault : firstAssigned.Id;
		}

		return result;
	}

	public Task SaveDefaultAsync(Guid bookId, long revision, Guid instrumentId, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.session.MutateMetadataAsync(
			(database, now) =>
			{
				if (database.Id != bookId || database.BookSettings.Revision.Revision != revision)
				{
					throw new InvalidOperationException("Book settings changed. Reopen them before saving.");
				}

				InstrumentProfile profile = database.InstrumentProfiles.Single(item => item.Id == instrumentId);
				database.BookSettings.DefaultInstrumentId = instrumentId;
				database.BookSettings.Revision = BookApplicationSession.NextRevision(database.BookSettings.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);

	public async Task<Guid> SaveProfileAsync(
		InstrumentProfileInfo? original, string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Guid id = original?.Id ?? Guid.CreateVersion7();
		await this.session.MutateMetadataAsync(
			(database, now) =>
			{
				if (database.InstrumentProfiles.Any(profile => profile.Id != id && profile.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
				{
					throw new ArgumentException("An instrument with this name already exists.", nameof(name));
				}

				InstrumentProfile profile;
				if (original is null)
				{
					profile = new() { Id = id };
					database.InstrumentProfiles.Add(profile);
				}
				else
				{
					profile = database.InstrumentProfiles.Single(item => item.Id == id);
					if (database.Id != original.BookId || profile.Revision.Revision != original.Revision)
					{
						throw new InvalidOperationException("The instrument changed. Reopen it before saving.");
					}
				}

				profile.Name = name.Trim();
				profile.Revision = BookApplicationSession.NextRevision(profile.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken).ConfigureAwait(false);
		return id;
	}

	public Task DeleteProfileAsync(InstrumentProfileInfo original, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.session.MutateMetadataAsync(
			(database, now) =>
			{
				InstrumentProfile profile = database.InstrumentProfiles.Single(item => item.Id == original.Id);
				if (database.Id != original.BookId || profile.Revision.Revision != original.Revision)
				{
					throw new InvalidOperationException("The instrument changed. Reopen it before deleting.");
				}

				if (database.BookSettings.DefaultInstrumentId == profile.Id
					|| database.SongInstrumentSettings.Any(item => item.InstrumentProfileId == profile.Id)
					|| database.Setlists.Any(list => list.Entries.Any(entry => entry.InstrumentProfileId == profile.Id)))
				{
					throw new InvalidOperationException(
						"This instrument is a book default or is used by songs or setlist entries. Change those settings before deleting it.");
				}

				database.InstrumentProfiles.Remove(profile);
				database.Tombstones.Add(new()
				{
					EntityId = profile.Id,
					EntityType = nameof(InstrumentProfile),
					Revision = BookApplicationSession.NextRevision(profile.Revision, deviceId, now),
				});
			},
			deviceId,
			cancellationToken);

	public SongInstrumentSnapshot GetSongSettings(Guid songId, Guid instrumentId)
	{
		ChordDatabase database = this.GetDatabase();
		Song song = database.Songs.Single(item => item.Id == songId);
		InstrumentProfile profile = database.InstrumentProfiles.Single(item => item.Id == instrumentId);
		SongInstrumentSetting? setting = this.session.GetInstrumentSetting(songId, instrumentId);
		SongInstrumentOptions options = this.GetOptions(song, profile, setting);
		return new(database.Id, songId, instrumentId, setting?.Id, setting?.Revision.Revision ?? 0, options);
	}

	/// <summary>Seeds a new assignment from song capo metadata; Piano matches the capoed reference instrument's sounding pitch.</summary>
	public SongInstrumentOptions GetNewAssignmentOptions(
		Guid songId, Guid instrumentId, SongInstrumentOptions? reference = null)
	{
		ChordDatabase database = this.GetDatabase();
		Song song = database.Songs.Single(item => item.Id == songId);
		InstrumentProfile profile = database.InstrumentProfiles.Single(item => item.Id == instrumentId);
		return this.CreateNewAssignmentOptions(song, profile, reference);
	}

	public Task SaveSongSettingsAsync(
		SongInstrumentSnapshot original,
		SongInstrumentOptions? options,
		Guid deviceId,
		SetlistEntrySettings? clearEntryTranspose = null,
		CancellationToken cancellationToken = default)
	{
		return this.SaveSongSettingsBatchAsync([(original, options)], deviceId, clearEntryTranspose, original.InstrumentId, cancellationToken);
	}

	/// <summary>Commits a song's instrument assignments and options together.</summary>
	public Task SaveSongSettingsBatchAsync(
		IReadOnlyList<(SongInstrumentSnapshot Original, SongInstrumentOptions? Options)> updates,
		Guid deviceId,
		SetlistEntrySettings? clearEntryTranspose = null,
		Guid? clearEntryInstrumentId = null,
		CancellationToken cancellationToken = default)
	{
		var snapshot = updates.ToArray();
		if (snapshot.Select(item => (item.Original.SongId, item.Original.InstrumentId)).Distinct().Count() != snapshot.Length)
		{
			throw new ArgumentException("Instrument settings must not be repeated.", nameof(updates));
		}

		foreach (var (_, options) in snapshot)
		{
			if (options is not null)
			{
				_ = PerformanceTranspose.GetShownSemitones(
					options.TransposeSemitones, options.CapoFret, options.CapoBehavior);
				if (!Enum.IsDefined(options.CapoBehavior) || !Enum.IsDefined(options.Spelling))
				{
					throw new ArgumentException("Choose a supported capo behavior and chord spelling.", nameof(updates));
				}
			}
		}

		return this.session.MutateMetadataAsync(
			(database, now) =>
			{
				foreach (var (original, options) in snapshot)
				{
					SetlistEntrySettings? clear = clearEntryInstrumentId == original.InstrumentId ? clearEntryTranspose : null;
					this.UpdateSongSettings(database, now, original, options, deviceId, clear);
				}
			},
			deviceId,
			cancellationToken);
	}

	#endregion

	#region Internal Methods

	internal static int? GetCatalogCapo(Song song)
	{
		IReadOnlyList<string> values = SongMetadata.GetValues(song, "capo");
		const int MaximumCapo = 24;
		return values.Count > 0 && int.TryParse(
			values[0],
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out int capo) && capo is >= 0 and <= MaximumCapo ? capo : null;
	}

	internal static bool IsPiano(InstrumentProfile profile) => profile.Id == StandardInstruments.All[5].Id
		|| profile.Name.Equals("Piano", StringComparison.OrdinalIgnoreCase);

	internal static AccidentalPreference ParseSpelling(string? value)
		=> value is null ? AccidentalPreference.Default : Enum.TryParse(value, out AccidentalPreference result) && Enum.IsDefined(result)
			? result : throw new InvalidOperationException("This song uses an unsupported chord-spelling preference.");

	internal SongInstrumentOptions GetEffectiveOptions(Song song, InstrumentProfile profile)
		=> this.GetOptions(song, profile, this.session.GetInstrumentSetting(song.Id, profile.Id));

	#endregion

	#region Private Methods

	private SongInstrumentOptions CreateNewAssignmentOptions(Song song, InstrumentProfile profile, SongInstrumentOptions? reference)
	{
		int? capo = GetCatalogCapo(song);
		SongInstrumentOptions result = new(CapoFret: capo);
		if (IsPiano(profile))
		{
			Guid? defaultId = this.ResolveInstrument(song.Id);
			SongInstrumentSetting? setting = defaultId is Guid id && id != profile.Id
				? this.session.GetInstrumentSetting(song.Id, id) : null;
			reference ??= setting is null ? new(CapoFret: capo)
				: new(
					setting.TransposeSemitones,
					setting.CapoFret ?? capo,
					setting.CapoBehavior,
					ParseSpelling(setting.ChordSpellingPreference),
					setting.PreferredSongFileId);
			int physicalCapo = reference.CapoFret ?? capo ?? 0;
			int sounding = PerformanceTranspose.GetShownSemitones(reference.TransposeSemitones, physicalCapo, reference.CapoBehavior) + physicalCapo;
			const int MaximumTranspose = 24;
			if (sounding is < -MaximumTranspose or > MaximumTranspose)
			{
				const int SemitonesPerOctave = 12;
				sounding %= SemitonesPerOctave;
			}

			result = reference with { TransposeSemitones = sounding, CapoFret = 0, CapoBehavior = CapoBehavior.DisplayOnly };
		}

		return result;
	}

	private SongInstrumentOptions GetOptions(Song song, InstrumentProfile profile, SongInstrumentSetting? setting)
		=> setting is null ? this.CreateNewAssignmentOptions(song, profile, null)
			: new(
				setting.TransposeSemitones,
				setting.CapoFret,
				setting.CapoBehavior,
				ParseSpelling(setting.ChordSpellingPreference),
				setting.PreferredSongFileId);

	private void UpdateSongSettings(
		ChordDatabase database,
		DateTimeOffset now,
		SongInstrumentSnapshot original,
		SongInstrumentOptions? options,
		Guid deviceId,
		SetlistEntrySettings? clearEntryTranspose)
	{
		SongInstrumentSetting? setting = this.session.GetInstrumentSetting(original.SongId, original.InstrumentId);
		if (database.Id != original.BookId || setting?.Id != original.SettingId || (setting?.Revision.Revision ?? 0) != original.Revision)
		{
			throw new InvalidOperationException("These song settings changed. Reopen them before saving.");
		}

		_ = database.Songs.Single(song => song.Id == original.SongId);
		_ = database.InstrumentProfiles.Single(profile => profile.Id == original.InstrumentId);
		if (options?.PreferredSongFileId is Guid fileId && !database.SongFiles.Any(file => file.Id == fileId && file.SongId == original.SongId
			&& !file.IsArchived && file.RecoveryVersion is null))
		{
			throw new ArgumentException("Choose an active sheet belonging to this song.", nameof(options));
		}

		if (clearEntryTranspose is not null)
		{
			Setlist list = database.Setlists.Single(item => item.Id == clearEntryTranspose.SetlistId);
			SetlistEntry entry = list.Entries.Single(item => item.Id == clearEntryTranspose.EntryId);
			if (list.Revision.Revision != clearEntryTranspose.SetlistRevision || entry.SongId != original.SongId
				|| (entry.InstrumentProfileId is Guid profileId && profileId != original.InstrumentId))
			{
				throw new InvalidOperationException("The setlist entry changed. Reopen its settings before saving.");
			}

			entry.TransposeSemitones = null;
			list.Revision = BookApplicationSession.NextRevision(list.Revision, deviceId, now);
		}

		if (options is null)
		{
			if (setting is not null)
			{
				database.SongInstrumentSettings.Remove(setting);
				database.Tombstones.Add(new()
				{
					EntityId = setting.Id,
					EntityType = nameof(SongInstrumentSetting),
					Revision = BookApplicationSession.NextRevision(setting.Revision, deviceId, now),
				});
			}

			this.session.SetInstrumentSetting(original.SongId, original.InstrumentId, null);
		}
		else
		{
			if (setting is null)
			{
				setting = new() { Id = Guid.CreateVersion7(), SongId = original.SongId, InstrumentProfileId = original.InstrumentId };
				database.SongInstrumentSettings.Add(setting);
			}

			setting.TransposeSemitones = options.TransposeSemitones;
			setting.CapoFret = options.CapoFret;
			setting.CapoBehavior = options.CapoBehavior;
			setting.ChordSpellingPreference = options.Spelling.ToString();
			setting.PreferredSongFileId = options.PreferredSongFileId;
			setting.Revision = BookApplicationSession.NextRevision(setting.Revision, deviceId, now);
			this.session.SetInstrumentSetting(original.SongId, original.InstrumentId, setting);
		}
	}

	private ChordDatabase GetDatabase() => this.session.Database ?? throw new InvalidOperationException("No book is open.");

	#endregion
}
