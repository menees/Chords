using System.Globalization;

namespace Menees.Chords.Book.Application;

public static class TransposeChoice
{
	public static string GetLabel(string? originalKey, int offset)
	{
		string number = offset.ToString("+0;-0;0", CultureInfo.InvariantCulture);
		string? name = Key.TryParse(originalKey, out Key? key)
			? Chord.Parse(key.Name).Transpose(checked((sbyte)offset)).Name : null;
		return name is null ? number : $"{name} ({number})";
	}
}
