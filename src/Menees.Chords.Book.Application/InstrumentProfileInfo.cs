namespace Menees.Chords.Book.Application;

public sealed record InstrumentProfileInfo(Guid BookId, Guid Id, string Name, long Revision);
