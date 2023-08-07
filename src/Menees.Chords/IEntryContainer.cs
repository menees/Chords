namespace Menees.Chords;

/// <summary>
/// Defines common functionality for objects that contains multiple <see cref="Entry"/>s.
/// </summary>
public interface IEntryContainer
{
	/// <summary>
	/// Gets the ordered collection of entries within the current container.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }
}
