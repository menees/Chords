namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Text;

#endregion

/// <summary>
/// Provides helper methods for grouping <see cref="Entry"/>s within a <see cref="DocumentParser"/>.
/// </summary>
public static class GroupEntries
{
	#region Public Methods

	/// <summary>
	/// Groups each consecutive <see cref="ChordLine"/> and <see cref="LyricLine"/> pair
	/// into an individual <see cref="ChordLyricPair"/> entry.
	/// </summary>
	/// <param name="context">The document parser's grouping context.</param>
	/// <returns>A new list of entries with <see cref="ChordLyricPair"/>s where possible.</returns>
	public static IReadOnlyList<Entry> ByChordLinePair(GroupContext context)
	{
		Conditions.RequireReference(context);

		IReadOnlyList<Entry> entries = context.Entries;
		int entryCount = entries.Count;
		List<Entry> result = new(entryCount);

		for (int index = 0; index < entryCount; index++)
		{
			Entry entry = entries[index];
			if (entry is ChordLine chords && (index + 1) < entryCount && entries[index + 1] is LyricLine lyrics)
			{
				ChordLyricPair pair = new(chords, lyrics);
				result.Add(pair);
				index++;
			}
			else
			{
				result.Add(entry);
			}
		}

		return result;
	}

	/// <summary>
	/// Groups entries within a ChordPro environment (e.g., soc-eoc) into <see cref="Section"/>s.
	/// </summary>
	/// <param name="context">The document parser's grouping context.</param>
	/// <returns>A new list of entries with <see cref="Section"/>s for known ChordPro environments.</returns>
	/// <seealso href="https://www.chordpro.org/chordpro/directives-env/"/>
	public static IReadOnlyList<Entry> ByChordProEnvironment(GroupContext context)
	{
		Conditions.RequireReference(context);

		IReadOnlyList<Entry> entries = context.Entries;
		int entryCount = entries.Count;
		List<Entry> result = new(entryCount);

		Stack<List<Entry>> sectionStack = new();
		for (int index = 0; index < entryCount; index++)
		{
			Entry entry = entries[index];

			if (entry is not ChordProDirectiveLine directive)
			{
				AddEntry(entry);
			}
			else
			{
				const string StartOfPrefix = "start_of_";
				const string EndOfPrefix = "end_of_";
				const StringComparison Comparison = ChordParser.Comparison;
				if (directive.LongName.StartsWith(StartOfPrefix, Comparison))
				{
					List<Entry> section = new() { directive };
					sectionStack.Push(section);
				}
				else if (directive.LongName.StartsWith(EndOfPrefix, Comparison)
					&& sectionStack.Count > 0
					&& sectionStack.Peek()[0] is ChordProDirectiveLine startDirective
					&& startDirective.LongName.Equals($"{StartOfPrefix}{directive.LongName[EndOfPrefix.Length..]}", Comparison))
				{
					AddEntry(directive);
					List<Entry> sectionEntries = sectionStack.Pop();
					AddEntry(new Section(sectionEntries));
				}
				else
				{
					AddEntry(entry);
				}
			}
		}

		while (sectionStack.Count > 0)
		{
			List<Entry> sectionEntries = sectionStack.Pop();
			AddEntry(new Section(sectionEntries));
		}

		void AddEntry(Entry entry) => (sectionStack.Count > 0 ? sectionStack.Peek() : result).Add(entry);

		return result;
	}

	/// <summary>
	/// Groups entries starting with a <see cref="HeaderLine"/> up to but not including
	/// the next <see cref="HeaderLine"/> into <see cref="Section"/>s.
	/// </summary>
	/// <param name="context">The document parser's grouping context.</param>
	/// <returns>A new list of entries with <see cref="Section"/>s for headered entry blocks.</returns>
	public static IReadOnlyList<Entry> ByHeaderLine(GroupContext context)
	{
		Conditions.RequireReference(context);

		IReadOnlyList<Entry> entries = context.Entries;
		IReadOnlyList<Entry> result;
		if (!entries.OfType<HeaderLine>().Any())
		{
			result = entries;
		}
		else
		{
			int entryCount = entries.Count;
			List<Entry> grouped = new(entryCount);
			result = grouped;

			List<Entry>? section = null;
			for (int index = 0; index < entryCount; index++)
			{
				Entry entry = entries[index];

				if (entry is HeaderLine header)
				{
					FinishSection();
					section = new() { header };
				}
				else
				{
					AddEntry(entry);
				}
			}

			FinishSection();

			void FinishSection()
			{
				if (section != null)
				{
					grouped.Add(section.Count == 1 ? section[0] : new Section(section));
#pragma warning disable IDE0059 // Unnecessary assignment of a value. Code may change later. It's safer to leave this to help maintainability.
					section = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
				}
			}

			void AddEntry(Entry entry) => (section ?? grouped).Add(entry);
		}

		return result;
	}

	/// <summary>
	/// Groups entries into <see cref="Section"/>s separated by one or more <see cref="BlankLine"/>s.
	/// </summary>
	/// <param name="context">The document parser's grouping context.</param>
	/// <returns>A new list of entries with <see cref="Section"/>s for headered entry blocks.</returns>
	public static IReadOnlyList<Entry> ByBlankLine(GroupContext context)
	{
		Conditions.RequireReference(context);

		IReadOnlyList<Entry> entries = context.Entries;
		IReadOnlyList<Entry> result;
		if (!entries.OfType<BlankLine>().Any())
		{
			result = entries;
		}
		else
		{
			int entryCount = entries.Count;
			List<Entry> grouped = new(entryCount);
			result = grouped;

			List<Entry>? section = null;
			for (int index = 0; index < entryCount; index++)
			{
				Entry entry = entries[index];

				if (entry is BlankLine or Section)
				{
					FinishSection();
					AddEntry(entry);
				}
				else
				{
					section ??= new();
					AddEntry(entry);
				}
			}

			FinishSection();

			void FinishSection()
			{
				if (section != null)
				{
					grouped.Add(section.Count == 1 ? section[0] : new Section(section));
					section = null;
				}
			}

			void AddEntry(Entry entry) => (section ?? grouped).Add(entry);
		}

		return result;
	}

	#endregion
}
