namespace Menees.Chords.Book.Application;

/// <summary>A bounded device-local recent list. Recording an access performs no storage work.</summary>
public sealed class RecentSongHistory
{
	#region Private Data

	private const int MaximumSongs = 100;
	private readonly List<Guid> songs;
	private readonly Dictionary<Guid, DateTimeOffset> accessed;

	#endregion

	#region Constructors

	public RecentSongHistory(IEnumerable<Guid>? saved = null, IReadOnlyDictionary<Guid, DateTimeOffset>? accessedUtc = null)
	{
		this.songs = [.. (saved ?? []).Where(id => id != Guid.Empty).Distinct().Take(MaximumSongs)];
		this.SongIds = this.songs.AsReadOnly();
		this.accessed = accessedUtc is null ? [] : new(accessedUtc);
		this.AccessedUtc = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, DateTimeOffset>(this.accessed);
	}

	#endregion

	#region Public Properties

	public IReadOnlyList<Guid> SongIds { get; }

	public bool IsDirty { get; private set; }

	public IReadOnlyDictionary<Guid, DateTimeOffset> AccessedUtc { get; }

	#endregion

	#region Public Methods

	public void Record(Guid songId, DateTimeOffset? now = null)
	{
		if (songId != Guid.Empty)
		{
			this.accessed[songId] = now ?? DateTimeOffset.UtcNow;
			this.IsDirty = true;
		}

		if (songId != Guid.Empty && (this.songs.Count == 0 || this.songs[0] != songId))
		{
			this.songs.Remove(songId);
			this.songs.Insert(0, songId);
			if (this.songs.Count > MaximumSongs)
			{
				this.songs.RemoveAt(MaximumSongs);
			}

			this.IsDirty = true;
		}
	}

	/// <summary>Records a completed batch in source order, with the last song most recent and no storage work.</summary>
	public void Record(IEnumerable<Guid> songIds)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		foreach (Guid songId in songIds)
		{
			this.Record(songId);
		}
	}

	public void PruneAccessTimes(IEnumerable<Guid> songIds)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		HashSet<Guid> retained = [.. songIds];
		foreach (Guid id in this.accessed.Keys.Where(id => !retained.Contains(id)).ToArray())
		{
			this.accessed.Remove(id);
			this.IsDirty = true;
		}
	}

	public void MarkSaved() => this.IsDirty = false;
	#endregion
}
