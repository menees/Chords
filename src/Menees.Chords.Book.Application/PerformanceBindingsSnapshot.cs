using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

public sealed record PerformanceBindingsSnapshot(Guid BookId, long Revision, IReadOnlyList<PerformanceKeyBinding> Bindings);
