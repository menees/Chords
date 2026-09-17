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

				if (database.SongInstrumentSettings.Any(item => item.InstrumentProfileId == profile.Id)
					|| database.Setlists.Any(list => list.Entries.Any(entry => entry.InstrumentProfileId == profile.Id)))
				{
					throw new InvalidOperationException("This instrument is used by songs or setlist entries. Reset those settings before deleting it.");
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
		_ = database.Songs.Single(song => song.Id == songId);
		_ = database.InstrumentProfiles.Single(profile => profile.Id == instrumentId);
		SongInstrumentSetting? setting = this.session.GetInstrumentSetting(songId, instrumentId);
		AccidentalPreference spelling = ParseSpelling(setting?.ChordSpellingPreference);
		SongInstrumentOptions options = setting is null ? new()
			: new(setting.TransposeSemitones, setting.CapoFret, setting.CapoBehavior, spelling, setting.PreferredSongFileId);
		return new(database.Id, songId, instrumentId, setting?.Id, setting?.Revision.Revision ?? 0, options);
	}

	public Task SaveSongSettingsAsync(
		SongInstrumentSnapshot original,
		SongInstrumentOptions? options,
		Guid deviceId,
		SetlistEntrySettings? clearEntryTranspose = null,
		CancellationToken cancellationToken = default)
	{
		if (options is not null)
		{
			_ = PerformanceTranspose.GetShownSemitones(options.TransposeSemitones, options.CapoFret, options.CapoBehavior == CapoBehavior.AffectsShownChords);
			if (!Enum.IsDefined(options.CapoBehavior) || !Enum.IsDefined(options.Spelling))
			{
				throw new ArgumentException("Choose a supported capo behavior and chord spelling.", nameof(options));
			}
		}

		return this.session.MutateMetadataAsync(
			(database, now) =>
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
			},
			deviceId,
			cancellationToken);
	}

	#endregion

	#region Internal Methods

	internal static AccidentalPreference ParseSpelling(string? value)
		=> value is null ? AccidentalPreference.Default : Enum.TryParse(value, out AccidentalPreference result) && Enum.IsDefined(result)
			? result : throw new InvalidOperationException("This song uses an unsupported chord-spelling preference.");

	#endregion

	#region Private Methods

	private ChordDatabase GetDatabase() => this.session.Database ?? throw new InvalidOperationException("No book is open.");

	#endregion
}
