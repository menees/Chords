namespace Menees.Chords.Book.Application;

/// <summary>The stable identities of the initial book instrument choices.</summary>
public static class StandardInstruments
{
	public static IReadOnlyList<(Guid Id, string Name)> All { get; } = Array.AsReadOnly<(Guid Id, string Name)>(
	[
		(new Guid("71078770-d1c5-4a83-95ed-331185559001"), "Acoustic Guitar"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559002"), "Banjo"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559003"), "Bass Guitar"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559004"), "Electric Guitar"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559005"), "Mandolin"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559006"), "Piano"),
		(new Guid("71078770-d1c5-4a83-95ed-331185559007"), "Ukulele"),
	]);
}
