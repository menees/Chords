#region Using Directives

using System.IO;
using System.Threading;
using Menees.Diffs;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Bounded, exact line comparison using the established Myers TextDiff implementation.</summary>
public static class SongTextDifference
{
	#region Public Methods

	public static IReadOnlyList<SongDifferenceLine> Compare(string left, string right, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(left);
		ArgumentNullException.ThrowIfNull(right);
		cancellationToken.ThrowIfCancellationRequested();
		List<string> a = Split(left), b = Split(right);
		int prefix = 0;
		while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
		{
			prefix++;
		}

		int suffix = 0;
		while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
		{
			suffix++;
		}

		int lengthA = a.Count - prefix - suffix, lengthB = b.Count - prefix - suffix;
		const long MaximumDifferenceWork = 25_000_000;
		if ((long)lengthA * lengthB > MaximumDifferenceWork)
		{
			throw new InvalidOperationException("These sheets differ too extensively for line highlighting. "
				+ "Copy either complete version or compare smaller sheets.");
		}

		TextDiff diff = new(HashType.Unique, ignoreCase: false, ignoreOuterWhiteSpace: false);
		EditScript edits = diff.Execute(a.GetRange(prefix, lengthA), b.GetRange(prefix, lengthB));
		cancellationToken.ThrowIfCancellationRequested();
		List<SongDifferenceLine> rows = new(Math.Max(a.Count, b.Count));
		int indexA = 0, indexB = 0;
		foreach (Edit edit in edits)
		{
			int startA = edit.StartA + prefix, startB = edit.StartB + prefix;
			while (indexA < startA && indexB < startB)
			{
				rows.Add(new(SongDifferenceKind.Unchanged, indexA + 1, a[indexA++], indexB + 1, b[indexB++]));
			}

			for (int offset = 0; offset < edit.Length; offset++)
			{
				rows.Add(edit.EditType switch
				{
					EditType.Delete => new(SongDifferenceKind.Removed, indexA + 1, a[indexA++], null, null),
					EditType.Insert => new(SongDifferenceKind.Added, null, null, indexB + 1, b[indexB++]),
					EditType.Change => new(SongDifferenceKind.Changed, indexA + 1, a[indexA++], indexB + 1, b[indexB++]),
					_ => throw new InvalidOperationException("The difference contains an unsupported edit."),
				});
			}
		}

		while (indexA < a.Count || indexB < b.Count)
		{
			rows.Add(indexA < a.Count && indexB < b.Count
				? new(SongDifferenceKind.Unchanged, indexA + 1, a[indexA++], indexB + 1, b[indexB++])
				: indexA < a.Count ? new(SongDifferenceKind.Removed, indexA + 1, a[indexA++], null, null)
				: new(SongDifferenceKind.Added, null, null, indexB + 1, b[indexB++]));
		}

		return rows.AsReadOnly();
	}

	#endregion

	#region Private Methods

	private static List<string> Split(string text)
	{
		const int MaximumLines = 20_000;
		using StringReader reader = new(text);
		List<string> result = [];
		string? line;
		while ((line = reader.ReadLine()) is not null)
		{
			if (result.Count >= MaximumLines)
			{
				throw new InvalidOperationException("Line comparison supports up to 20,000 lines per sheet.");
			}

			result.Add(line);
		}

		if (text.Length == 0 || text.EndsWith('\n') || text.EndsWith('\r'))
		{
			result.Add(string.Empty);
		}

		return result;
	}

	#endregion
}
