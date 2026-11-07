#region Using Directives

using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>A detached setlist draft. Discarding it requires no database writes or asset access.</summary>
public sealed class SetlistEdit
{
	#region Private Data

	private readonly Setlist original;

	#endregion

	#region Constructors

	internal SetlistEdit(Guid bookId, Setlist source)
	{
		this.BookId = bookId;
		this.original = Copy(source);
		this.Value = Copy(source);
	}

	#endregion

	#region Public Properties

	public Guid BookId { get; }

	public Guid Id => this.original.Id;

	public Setlist Value { get; }

	public bool DeleteRequested { get; private set; }

	public bool HasChanges => this.DeleteRequested || !SetlistContent.AreEqual(this.original, this.Value);

	#endregion

	#region Public Methods

	public void SetOrder(IReadOnlyList<Guid> ids)
	{
		ArgumentNullException.ThrowIfNull(ids);
		Dictionary<Guid, SetlistEntry> entries = this.Value.Entries.ToDictionary(entry => entry.Id);
		HashSet<Guid> seen = [];
		List<SetlistEntry> ordered = [];
		foreach (Guid id in ids)
		{
			if (!seen.Add(id) || !entries.TryGetValue(id, out SetlistEntry? entry))
			{
				throw new ArgumentException("Choose each existing setlist entry exactly once.", nameof(ids));
			}

			ordered.Add(entry);
		}

		if (ordered.Count != entries.Count)
		{
			throw new ArgumentException("Choose each existing setlist entry exactly once.", nameof(ids));
		}

		this.Value.Entries = ordered;
	}

	public void SortByName(IReadOnlyDictionary<Guid, string> songNames)
	{
		ArgumentNullException.ThrowIfNull(songNames);

		// Stable ordering retains repeated occurrences and the relative order of equal titles.
		this.Value.Entries = [.. this.Value.Entries.OrderBy(entry => songNames[entry.SongId], StringComparer.CurrentCultureIgnoreCase)];
	}

	public void AddSongs(IEnumerable<Guid> songIds)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		this.Value.Entries.AddRange(songIds.Select(id => new SetlistEntry { Id = Guid.CreateVersion7(), SongId = id }));
	}

	public SetlistEntrySettings GetEntrySettings(Guid entryId)
	{
		SetlistEntry entry = this.Value.Entries.Single(item => item.Id == entryId);
		long revision = this.original.Revision.Revision;
		return new(this.Id, entry.Id, entry.SongId, revision, entry.PreferredSongFileId, entry.TransposeSemitones, entry.InstrumentProfileId);
	}

	public void SetEntrySettings(ChordDatabase database, Guid entryId, Guid? fileId, int? transpose, Guid? instrumentId)
	{
		ArgumentNullException.ThrowIfNull(database);
		SetlistEntry entry = this.Value.Entries.Single(item => item.Id == entryId);
		SetlistEntrySettings.Validate(database, entry.SongId, fileId, transpose, instrumentId);
		entry.PreferredSongFileId = fileId;
		entry.TransposeSemitones = transpose;
		entry.InstrumentProfileId = instrumentId;
	}

	public void RequestDeletion()
	{
		if (!this.Value.IsArchived)
		{
			throw new InvalidOperationException("Archive the setlist before permanently deleting it.");
		}

		this.DeleteRequested = true;
	}

	#endregion

	#region Internal Methods

	internal Setlist Validate(ChordDatabase database)
	{
		Setlist? current = database.Setlists.SingleOrDefault(item => item.Id == this.Id);
		if (database.Id != this.BookId || current is null || this.Value.Id != this.Id
			|| current.Revision.Revision != this.original.Revision.Revision || !SetlistContent.AreEqual(current, this.original))
		{
			throw new InvalidOperationException("This setlist changed while it was being edited. Reopen it before saving.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(this.Value.Name);
		if (this.DeleteRequested && !this.Value.IsArchived)
		{
			throw new InvalidOperationException("Archive the setlist before permanently deleting it.");
		}

		HashSet<Guid> songs = [.. database.Songs.Select(song => song.Id)];
		HashSet<Guid> ids = [];
		Dictionary<Guid, SetlistEntry> originals = this.original.Entries.ToDictionary(entry => entry.Id);
		foreach (SetlistEntry entry in this.Value.Entries)
		{
			if (entry.Id == Guid.Empty || !ids.Add(entry.Id) || !songs.Contains(entry.SongId))
			{
				throw new ArgumentException("Setlist entries must have distinct identities and refer to existing songs.");
			}

			if (!originals.TryGetValue(entry.Id, out SetlistEntry? previous) || !SetlistContent.EntriesEqual(previous, entry))
			{
				if (previous is not null && previous.SongId != entry.SongId)
				{
					throw new ArgumentException("An existing setlist entry cannot change song identity.");
				}

				SetlistEntrySettings.Validate(database, entry.SongId, entry.PreferredSongFileId, entry.TransposeSemitones, entry.InstrumentProfileId);
			}
		}

		return current;
	}

	internal void Apply(Setlist current, Guid deviceId, DateTimeOffset now)
	{
		current.Name = this.Value.Name.Trim();
		current.Date = this.Value.Date;
		current.Notes = this.Value.Notes;
		current.IsArchived = this.Value.IsArchived;
		current.Entries = [.. this.Value.Entries.Select(CopyEntry)];
		current.Revision = BookApplicationSession.NextRevision(current.Revision, deviceId, now);
	}

	#endregion

	#region Private Methods

	private static Setlist Copy(Setlist source) => new()
	{
		Id = source.Id, Name = source.Name, Notes = source.Notes, Date = source.Date, IsArchived = source.IsArchived,
		Entries = [.. source.Entries.Select(CopyEntry)],
		Revision = new() { Revision = source.Revision.Revision, DeviceId = source.Revision.DeviceId, ModifiedUtc = source.Revision.ModifiedUtc },
	};

	private static SetlistEntry CopyEntry(SetlistEntry entry) => new()
	{
		Id = entry.Id, SongId = entry.SongId, PreferredSongFileId = entry.PreferredSongFileId,
		InstrumentProfileId = entry.InstrumentProfileId, TransposeSemitones = entry.TransposeSemitones,
	};

	#endregion
}
