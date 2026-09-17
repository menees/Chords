namespace Menees.Chords.Book.Application;

public sealed record DocumentPositionKey(Guid SongId, Guid? EntryId, Guid? FileId);
