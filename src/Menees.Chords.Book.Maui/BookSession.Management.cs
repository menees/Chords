#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class BookSession
{
	#region Public Methods

	public SetlistEntrySettings GetSetlistEntrySettings(Guid setlistId, Guid entryId)
		=> this.application.GetSetlistEntrySettings(setlistId, entryId);

	public Task SaveSetlistEntrySettingsAsync(SetlistEntrySettings original, Guid? fileId, int? transpose, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SaveSetlistEntrySettingsAsync(original, fileId, transpose, this.DeviceId, cancellationToken), cancellationToken);

	public DisplayProfile GetDisplaySettings(Guid? songId)
		=> this.application.GetDisplaySettings(songId);

	public Task SaveDisplaySettingsAsync(Guid? songId, DisplayProfile? profile, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SaveDisplaySettingsAsync(songId, profile, this.DeviceId, cancellationToken), cancellationToken);

	public IReadOnlyList<CustomTabCatalogItem> GetCustomTabs() => this.application.GetCustomTabs();

	public Task<Guid> SaveCustomTabAsync(Guid? id, string name, string search, string? groupBy, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SaveCustomTabAsync(id, name, search, groupBy, this.DeviceId, cancellationToken), cancellationToken);

	public Task DeleteCustomTabAsync(Guid id, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.DeleteCustomTabAsync(id, this.DeviceId, cancellationToken), cancellationToken);

	public Task<BookReconcilePreview> PreviewReconcileAsync(CancellationToken cancellationToken = default)
		=> Task.Run(
		() =>
		{
			(FileSystemBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			return activeStore.PreviewReconcileAsync(activeLocation, this.DeviceId, cancellationToken);
		},
		cancellationToken);

	public Task<BookReconcileResult> ApplyReconcileAsync(
		BookReconcilePreview preview,
		IReadOnlySet<string> useSourceValues,
		CancellationToken cancellationToken = default)
		=> Task.Run(
		async () =>
		{
			(FileSystemBookStore activeStore, _) = this.GetActiveBook();
			BookReconcileResult result = await activeStore.ApplyReconcileAsync(preview, useSourceValues, cancellationToken).ConfigureAwait(false);
			await this.application.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
			return result;
		},
		cancellationToken);

	public Task<Guid> CreateSongAsync(
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string text,
		CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.CreateSongAsync(title, artists, tags, text, this.DeviceId, cancellationToken), cancellationToken);

	public IReadOnlyList<SongFileCatalogItem> GetSongFiles(Guid songId)
		=> this.application.GetSongFiles(songId);

	public Task<int> ImportSongFilesAsync(Guid songId, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.ImportSongFilesAsync(songId, paths, this.DeviceId, cancellationToken), cancellationToken);

	public Task SetSongFilePositionAsync(Guid songId, Guid fileId, int position, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SetSongFilePositionAsync(songId, fileId, position, this.DeviceId, cancellationToken), cancellationToken);

	public Task SetSongFileArchivedAsync(Guid songId, Guid fileId, bool archived, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SetSongFileArchivedAsync(songId, fileId, archived, this.DeviceId, cancellationToken), cancellationToken);

	public Task RenameSongFileAsync(Guid songId, Guid fileId, string name, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.RenameSongFileAsync(songId, fileId, name, this.DeviceId, cancellationToken), cancellationToken);

	public Task DeleteArchivedSongFileAsync(Guid songId, Guid fileId, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.DeleteArchivedSongFileAsync(songId, fileId, this.DeviceId, cancellationToken), cancellationToken);

	public MetronomeSettings GetMetronomeSettings(Guid? id)
		=> this.application.GetMetronomeSettings(id);

	public Task SaveBookMetronomeAsync(MetronomeSettings settings, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SaveBookMetronomeAsync(settings, this.DeviceId, cancellationToken), cancellationToken);

	public Task SaveSongMetronomeAsync(Guid id, MetronomeSettings? settings, CancellationToken cancellationToken = default)
		=> this.application.SaveSongMetronomeAsync(id, settings, this.DeviceId, cancellationToken);

	public Task SetSetlistEntryPositionAsync(Guid setlistId, Guid entryId, int position, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SetSetlistEntryPositionAsync(setlistId, entryId, position, this.DeviceId, cancellationToken), cancellationToken);

	public Task SetSetlistOrderAsync(Guid setlistId, IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.SetSetlistOrderAsync(setlistId, entryIds, this.DeviceId, cancellationToken), cancellationToken);

	public Task SetSongsArchivedAsync(IReadOnlyList<Guid> ids, bool archived, CancellationToken cancellationToken = default)
		=> this.application.SetSongsArchivedAsync(ids, archived, this.DeviceId, cancellationToken);

	public Task DeleteArchivedSongsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
		=> this.application.DeleteArchivedSongsAsync(ids, this.DeviceId, cancellationToken);

	public Task DeleteArchivedSetlistAsync(Guid id, CancellationToken cancellationToken = default)
		=> this.application.DeleteArchivedSetlistAsync(id, this.DeviceId, cancellationToken);

	public Task<SongEditDocument> GetSongEditAsync(Guid id, CancellationToken cancellationToken = default)
		=> this.application.GetSongEditAsync(id, cancellationToken);

	public Task<SongEditDocument> GetSongFileEditAsync(Guid songId, Guid fileId, CancellationToken cancellationToken = default)
		=> Task.Run(() => this.application.GetSongFileEditAsync(songId, fileId, cancellationToken), cancellationToken);

	public Task SaveSongEditAsync(
		SongEditDocument original,
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string? text,
		CancellationToken cancellationToken = default)
		=> this.application.SaveSongEditAsync(original, title, artists, tags, text, this.DeviceId, cancellationToken);
	#endregion
}
