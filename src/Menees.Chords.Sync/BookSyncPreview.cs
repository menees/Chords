#region Using Directives

using System.Collections.ObjectModel;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>A detached comparison used by explicit sync and exceptional confirmation workflows.</summary>
public sealed class BookSyncPreview
{
	#region Constructors

	internal BookSyncPreview(SyncTransferDocument document, bool resumed, string? canonicalLocal = null, string? canonicalCloud = null)
	{
		this.Document = document;
		this.IsResuming = resumed;
		ChordDatabase target = document.GetTargetDatabase();
		ChordDatabase local = document.GetLocalDatabase();
		ChordDatabase? cloud = document.GetCloudDatabase();
		this.SongCount = target.Songs.Count;
		HashSet<Guid> targetSongs = [.. target.Songs.Select(song => song.Id)];
		this.LocalRemovedSongCount = local.Songs.Count(song => !targetSongs.Contains(song.Id));
		this.CloudRemovedSongCount = cloud?.Songs.Count(song => !targetSongs.Contains(song.Id)) ?? 0;
		HashSet<Guid> targetFiles = [.. target.SongFiles.Select(file => file.Id)];
		this.LocalRemovedSheetCount = local.SongFiles.Count(file => !targetFiles.Contains(file.Id));
		this.CloudRemovedSheetCount = cloud?.SongFiles.Count(file => !targetFiles.Contains(file.Id)) ?? 0;
		this.ConflictDescriptions = document.Conflicts.Count == 0 ? Array.Empty<string>()
			: DescribeConflicts(document.Conflicts, target, local, cloud);
		bool localChanged = document.TargetJson != document.LocalJson
			&& document.TargetJson != (canonicalLocal ??= DatabaseJson.Serialize(local));
		bool cloudChanged = cloud is null || (document.Direction != SyncDirection.UpdateThisDevice
			&& document.TargetJson != document.CloudJson
			&& document.TargetJson != (document.CloudJson == document.LocalJson
				? canonicalLocal ?? DatabaseJson.Serialize(local) : canonicalCloud ?? DatabaseJson.Serialize(cloud)));
		this.HasChanges = resumed || localChanged || cloudChanged || document.Assets.Any(asset => asset.Stage || asset.Upload);
	}

	#endregion

	#region Properties

	public CloudReplicaIdentity Target => this.Document.Target!;

	public SyncDirection Direction => this.Document.Direction;

	public Guid RecoveryEpoch => this.Document.RecoveryEpoch;

	public bool IsInitialPublication => this.Document.CloudCatalog is null;

	public bool IsResuming { get; }

	/// <summary>Includes metadata-only changes and pending completion, not just asset transfer counts.</summary>
	public bool HasChanges { get; }

	public int UploadCount => this.Document.Assets.Count(asset => asset.Upload);

	public int DownloadCount => this.Document.Assets.Count(asset => asset.SourceItem is not null && asset.Stage);

	public long EstimatedBytes => this.Document.Assets.Where(asset => asset.Stage).Sum(asset => asset.Length);

	public IReadOnlyList<SyncConflict> Conflicts => this.Document.Conflicts.AsReadOnly();

	public int RecoveryCount => this.Document.RecoveryCount;

	public int SongCount { get; }

	public int LocalRemovedSongCount { get; }

	public int CloudRemovedSongCount { get; }

	public int LocalRemovedSheetCount { get; }

	public int CloudRemovedSheetCount { get; }

	public IReadOnlyList<string> ConflictDescriptions { get; }

	internal SyncTransferDocument Document { get; }

	#endregion

	#region Private Methods

	private static ReadOnlyCollection<string> DescribeConflicts(
		IReadOnlyList<SyncConflict> conflicts, ChordDatabase target, ChordDatabase local, ChordDatabase? cloud)
	{
		Dictionary<string, string> names = new(StringComparer.Ordinal);
		foreach (ChordDatabase database in new[] { target, local, cloud }.OfType<ChordDatabase>())
		{
			Dictionary<Guid, string> songs = database.Songs.ToDictionary(song => song.Id, song => song.Title);
			foreach (Song song in database.Songs)
			{
				names.TryAdd("Song:" + song.Id, "Song: " + song.Title);
			}

			foreach (SongFile file in database.SongFiles)
			{
				names.TryAdd("SongFile:" + file.Id, "Sheet: " + songs[file.SongId]);
			}

			foreach (Setlist setlist in database.Setlists)
			{
				names.TryAdd("Setlist:" + setlist.Id, "Setlist: " + setlist.Name);
			}

			names.TryAdd("BookName:" + database.Id, "Book name");
			names.TryAdd("BookSettings:" + database.Id, "Book settings");
		}

		return Array.AsReadOnly(conflicts.Select(conflict =>
		{
			string name = names.GetValueOrDefault(conflict.EntityId, "Book item");
			string winner = conflict.Winner == SyncSide.Local ? "this device" : "OneDrive";
			return name + " — use " + winner + ".";
		}).ToArray());
	}

	#endregion
}
