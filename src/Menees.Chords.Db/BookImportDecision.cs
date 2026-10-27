namespace Menees.Chords.Db;

/// <summary>One reviewed source action and optional explicit target.</summary>
/// <param name="ItemId">The reviewed source identity.</param>
/// <param name="Action">The selected import action.</param>
/// <param name="TargetSongId">A suggested existing or earlier incoming song for AddSheet.</param>
public sealed record BookImportDecision(Guid ItemId, BookImportAction Action, Guid? TargetSongId = null);