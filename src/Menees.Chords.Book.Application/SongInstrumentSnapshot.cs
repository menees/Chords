namespace Menees.Chords.Book.Application;

public sealed record SongInstrumentSnapshot(
	Guid BookId, Guid SongId, Guid InstrumentId, Guid? SettingId, long Revision, SongInstrumentOptions Options);
