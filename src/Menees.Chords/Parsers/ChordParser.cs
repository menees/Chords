namespace Menees.Chords.Parsers;

/// <summary>
/// Parses <see cref="Chord"/> names.
/// </summary>
public sealed class ChordParser
{
	#region Public Methods

	/// <summary>
	/// Gets the length of a note in <paramref name="text"/> at <paramref name="startIndex"/>.
	/// </summary>
	/// <param name="text">The text to match in.</param>
	/// <param name="startIndex">The 0-based index to start matching.</param>
	/// <returns>1 for a single letter note match (e.g., A, G), 2 for a sharp or flat match (e.g., A#, Bb), or 0 if no match.</returns>
	public static int GetNoteLength(string text, int startIndex = 0)
	{
		int result = 0;

		if (text.Length > startIndex)
		{
			char ch = text[startIndex];
			if ((ch >= 'A' && ch <= 'G') || (ch >= 'a' && ch <= 'g'))
			{
				result = 1;
				if (text.Length > (startIndex + 1))
				{
					ch = text[startIndex + 1];
					if (ch == '#' || ch == 'b')
					{
						result = 2;
					}
				}
			}
		}

		return result;
	}

	#endregion
}
