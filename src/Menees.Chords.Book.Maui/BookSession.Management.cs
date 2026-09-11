using Menees.Chords.Book.Application;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui;

public sealed partial class BookSession
{
	public MetronomeSettings GetMetronomeSettings(Guid id) => this.application.GetMetronomeSettings(id);

	public Task SaveSongMetronomeAsync(Guid id, MetronomeSettings? settings)
		=> this.application.SaveSongMetronomeAsync(id, settings, this.DeviceId);

	public Task SetSetlistEntryPositionAsync(Guid setlistId, Guid entryId, int position)
		=> Task.Run(() => this.application.SetSetlistEntryPositionAsync(setlistId, entryId, position, this.DeviceId));

	public Task SetSetlistOrderAsync(Guid setlistId, IReadOnlyList<Guid> entryIds)
		=> Task.Run(() => this.application.SetSetlistOrderAsync(setlistId, entryIds, this.DeviceId));

	public Task SetSongsArchivedAsync(IReadOnlyList<Guid> ids, bool archived)
		=> this.application.SetSongsArchivedAsync(ids, archived, this.DeviceId);

	public Task DeleteArchivedSongsAsync(IReadOnlyList<Guid> ids)
		=> this.application.DeleteArchivedSongsAsync(ids, this.DeviceId);

	public Task DeleteArchivedSetlistAsync(Guid id)
		=> this.application.DeleteArchivedSetlistAsync(id, this.DeviceId);

	public Task<SongEditDocument> GetSongEditAsync(Guid id) => this.application.GetSongEditAsync(id);

	public Task SaveSongEditAsync(SongEditDocument original, string title, IReadOnlyList<string> artists, IReadOnlyList<string> tags, string? text)
		=> this.application.SaveSongEditAsync(original, title, artists, tags, text, this.DeviceId);
}
