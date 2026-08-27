#region Using Directives

using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Creates and validates flat, cross-platform managed asset filenames.</summary>
public static class PortableManagedFileName
{
	#region Private Data

	/// <summary>The maximum filename length measured in UTF-16 code units.</summary>
	public const int MaxUtf16Length = 200;

	/// <summary>The maximum filename length measured in UTF-8 bytes.</summary>
	public const int MaxUtf8Length = 240;

	private const int GuidTextLength = 36;
	private const int BracketedGuidTextLength = 37;
	private const int HighestNumberedDevice = 9;
	private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create("<>:\"/\\|?*");
	private static readonly HashSet<string> ReservedBaseNames = CreateReservedBaseNames();

	#endregion

	#region Public API

	/// <summary>Gets the comparer used for portable filename uniqueness.</summary>
	public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

	/// <summary>Creates a normalized filename containing a permanent song-file GUID suffix.</summary>
	public static string Create(string description, Guid songFileId, string? extension)
	{
		if (songFileId == Guid.Empty)
		{
			throw new ArgumentException("A non-empty song-file ID is required.", nameof(songFileId));
		}

		string safeExtension = NormalizeExtension(extension);
		string prefix = Sanitize(description);
		string suffix = $" [{songFileId:D}]{safeExtension}";
		if (suffix.Length > MaxUtf16Length || Encoding.UTF8.GetByteCount(suffix) > MaxUtf8Length)
		{
			throw new ArgumentException("The extension is too long for a portable managed filename.", nameof(extension));
		}

		int utf16Available = MaxUtf16Length - suffix.Length;
		int utf8Available = MaxUtf8Length - Encoding.UTF8.GetByteCount(suffix);
		prefix = TruncateAtTextElement(prefix, utf16Available, utf8Available).TrimEnd(' ', '.');
		if (prefix.Length == 0)
		{
			prefix = "Song";
		}

		string result = (prefix + suffix).Normalize(NormalizationForm.FormC);
		IReadOnlyList<string> errors = Validate(result);
		if (errors.Count != 0)
		{
			throw new ArgumentException(string.Join(" ", errors), nameof(description));
		}

		return result;
	}

	/// <summary>Returns all violations of the portable managed-filename rules.</summary>
	public static IReadOnlyList<string> Validate(string relativePath)
	{
		List<string> errors = [];
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			errors.Add("A managed filename is required.");
		}
		else
		{
			if (Path.IsPathRooted(relativePath) || relativePath.Contains('/') || relativePath.Contains('\\')
				|| relativePath is "." or "..")
			{
				errors.Add("A managed path must be exactly one unrooted top-level filename.");
			}

			if (!relativePath.IsNormalized(NormalizationForm.FormC))
			{
				errors.Add("A managed filename must use Unicode normalization form C.");
			}

			if (relativePath.AsSpan().IndexOfAny(InvalidCharacters) >= 0 || relativePath.Any(char.IsControl))
			{
				errors.Add("A managed filename contains a non-portable character.");
			}

			if (relativePath.EndsWith(' ') || relativePath.EndsWith('.'))
			{
				errors.Add("A managed filename cannot end with a space or period.");
			}

			string baseName = relativePath;
			int period = baseName.IndexOf('.');
			if (period >= 0)
			{
				baseName = baseName[..period];
			}

			if (ReservedBaseNames.Contains(baseName))
			{
				errors.Add("A managed filename cannot use a reserved Windows basename.");
			}

			if (relativePath.Length > MaxUtf16Length)
			{
				errors.Add($"A managed filename cannot exceed {MaxUtf16Length} UTF-16 code units.");
			}

			if (Encoding.UTF8.GetByteCount(relativePath) > MaxUtf8Length)
			{
				errors.Add($"A managed filename cannot exceed {MaxUtf8Length} UTF-8 bytes.");
			}
		}

		return errors;
	}

	/// <summary>Extracts a recognizable song-file GUID suffix from a filename.</summary>
	public static bool TryGetSongFileId(string filename, out Guid songFileId)
	{
		songFileId = Guid.Empty;
		int closeBracket = filename.LastIndexOf(']');
		int openBracket = closeBracket >= 0 ? filename.LastIndexOf('[', closeBracket) : -1;
		bool result = openBracket >= 0
			&& closeBracket - openBracket == BracketedGuidTextLength
			&& Guid.TryParseExact(filename.AsSpan(openBracket + 1, GuidTextLength), "D", out songFileId);
		return result;
	}

	#endregion

	#region Private Methods

	private static string Sanitize(string description)
	{
		StringBuilder result = new(description.Length);
		foreach (char character in description.Normalize(NormalizationForm.FormC))
		{
			result.Append(char.IsControl(character) || InvalidCharacters.Contains(character) ? '_' : character);
		}

		return result.ToString().Trim().TrimEnd('.');
	}

	private static string NormalizeExtension(string? extension)
	{
		string result = string.Empty;
		if (!string.IsNullOrEmpty(extension))
		{
			result = extension.StartsWith('.') ? extension : "." + extension;
			result = result.Normalize(NormalizationForm.FormC);
			if (result.Length == 1 || result.AsSpan().IndexOfAny(InvalidCharacters) >= 0 || result.Any(char.IsControl)
				|| result.EndsWith(' ') || result.EndsWith('.'))
			{
				throw new ArgumentException("The extension is not portable.", nameof(extension));
			}
		}

		return result;
	}

	private static string TruncateAtTextElement(string value, int maxUtf16, int maxUtf8)
	{
		StringBuilder result = new();
		TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
		while (elements.MoveNext())
		{
			string element = elements.GetTextElement();
			if (result.Length + element.Length > maxUtf16
				|| Encoding.UTF8.GetByteCount(result.ToString()) + Encoding.UTF8.GetByteCount(element) > maxUtf8)
			{
				break;
			}

			result.Append(element);
		}

		return result.ToString();
	}

	private static HashSet<string> CreateReservedBaseNames()
	{
		HashSet<string> result = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
		for (int index = 1; index <= HighestNumberedDevice; index++)
		{
			result.Add("COM" + index.ToString(CultureInfo.InvariantCulture));
			result.Add("LPT" + index.ToString(CultureInfo.InvariantCulture));
		}

		return result;
	}

	#endregion
}
