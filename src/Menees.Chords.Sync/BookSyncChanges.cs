#region Using Directives

using System.Collections.ObjectModel;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Detached descriptions of transferred assets and changed whole setlists. Never reads sheet bytes.</summary>
public sealed class BookSyncChanges
{
	#region Constructors

	internal BookSyncChanges(SyncTransferDocument document, ChordDatabase target, ChordDatabase local, ChordDatabase? cloud)
	{
		Dictionary<Guid, Song> songs = target.Songs.ToDictionary(song => song.Id);
		Dictionary<Guid, SongFile> files = target.SongFiles.ToDictionary(file => file.Id);
		Dictionary<Guid, IReadOnlyList<string>> artists = [];
		List<SyncSheetDetail> uploads = [], downloads = [];
		foreach (SyncTransferAsset asset in document.Assets)
		{
			bool download = asset.SourceItem is not null && asset.Stage;
			if (asset.Upload || download)
			{
				SongFile file = files[asset.FileId];
				Song song = songs[file.SongId];
				SyncSheetDetail detail = CreateSheetDetail(file, song, artists);
				if (asset.Upload)
				{
					uploads.Add(detail);
				}

				if (download)
				{
					downloads.Add(detail);
				}
			}
		}

		this.UploadedSheets = uploads.AsReadOnly();
		this.DownloadedSheets = downloads.AsReadOnly();
		this.UploadedSetlists = document.Direction == SyncDirection.UpdateThisDevice ? []
			: ChangedSetlists(target.Setlists, cloud?.Setlists ?? []);
		this.DownloadedSetlists = ChangedSetlists(target.Setlists, local.Setlists);
		HashSet<Guid> retained = [.. target.Setlists.Select(list => list.Id)];
		this.RemovedLocalSetlists = Array.AsReadOnly(local.Setlists.Where(list => !retained.Contains(list.Id))
			.Select(list => new SyncSetlistDetail(list.Id, list.Name)).ToArray());
		this.RemovedCloudSetlists = document.Direction == SyncDirection.UpdateThisDevice || cloud is null ? []
			: Array.AsReadOnly(cloud.Setlists.Where(list => !retained.Contains(list.Id))
				.Select(list => new SyncSetlistDetail(list.Id, list.Name)).ToArray());
	}

	private BookSyncChanges(ChordDatabase downloaded)
	{
		Dictionary<Guid, Song> songs = downloaded.Songs.ToDictionary(song => song.Id);
		Dictionary<Guid, IReadOnlyList<string>> artists = [];
		this.UploadedSheets = [];
		this.UploadedSetlists = [];
		this.RemovedLocalSetlists = [];
		this.RemovedCloudSetlists = [];
		this.DownloadedSheets = Array.AsReadOnly(downloaded.SongFiles.Select(file => CreateSheetDetail(file, songs[file.SongId], artists)).ToArray());
		this.DownloadedSetlists = Array.AsReadOnly(downloaded.Setlists.Select(list => new SyncSetlistDetail(list.Id, list.Name)).ToArray());
	}

	#endregion

	#region Public Properties

	public IReadOnlyList<SyncSheetDetail> UploadedSheets { get; }

	public IReadOnlyList<SyncSheetDetail> DownloadedSheets { get; }

	public IReadOnlyList<SyncSetlistDetail> UploadedSetlists { get; }

	public IReadOnlyList<SyncSetlistDetail> DownloadedSetlists { get; }

	public IReadOnlyList<SyncSetlistDetail> RemovedLocalSetlists { get; }

	public IReadOnlyList<SyncSetlistDetail> RemovedCloudSetlists { get; }

	#endregion

	#region Public Methods

	/// <summary>Describes every managed sheet and setlist in a successfully downloaded/adopted book.</summary>
	public static BookSyncChanges FromDownloadedBook(ChordDatabase downloaded)
	{
		ArgumentNullException.ThrowIfNull(downloaded);
		return new(downloaded);
	}

	#endregion

	#region Internal Methods

	internal static string SheetName(SongFile file)
	{
		string name = PortableManagedFileName.WithoutLegacySyncToken(file.RelativePath, file.Id);
		return name.Replace($" [{file.Id:D}]", string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region Private Methods

	private static SyncSheetDetail CreateSheetDetail(SongFile file, Song song, Dictionary<Guid, IReadOnlyList<string>> artists)
	{
		if (!artists.TryGetValue(song.Id, out IReadOnlyList<string>? capturedArtists))
		{
			capturedArtists = Array.AsReadOnly(song.Artists.ToArray());
			artists.Add(song.Id, capturedArtists);
		}

		string kind = file.RecoveryVersion is not null
			? "Recovery version" : file.IsArchived ? "Archived sheet" : "Current sheet";
		return new(file.Id, song.Id, song.Title, capturedArtists, SheetName(file), kind);
	}

	private static ReadOnlyCollection<SyncSetlistDetail> ChangedSetlists(IReadOnlyList<Setlist> target, IReadOnlyList<Setlist> baseline)
	{
		Dictionary<Guid, Setlist> previous = baseline.ToDictionary(list => list.Id);
		return Array.AsReadOnly(target.Where(list => !previous.TryGetValue(list.Id, out Setlist? old) || !SetlistContent.AreEqual(list, old))
			.Select(list => new SyncSetlistDetail(list.Id, list.Name)).ToArray());
	}

	#endregion
}
