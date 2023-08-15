namespace Menees.Chords.Transformers;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Transforms a <see cref="Document"/> into ChordPro format.
/// </summary>
public sealed class ChordProTransformer : DocumentTransformer
{
	#region Private Data Members

	private readonly bool? preferLongNames;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified document.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	/// <param name="preferLongNames">How ChordPro directive names should be converted to text.
	/// If null, then <see cref="ChordProDirectiveLine.Name"/> will be used if available or a long name will be generated.
	/// If true, then <see cref="ChordProDirectiveLine.LongName"/> will be used.
	/// If false, then <see cref="ChordProDirectiveLine.ShortName"/> will be used.</param>
	public ChordProTransformer(Document document, bool? preferLongNames = null)
		: base(document)
	{
		this.preferLongNames = preferLongNames;
	}

	#endregion

	#region Private Enums

	private enum Environment
	{
		None,
		Implicit,
		Explicit,
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Converts <see cref="DocumentTransformer.Document"/> to ChordPro format.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public ChordProTransformer ToChordPro()
	{
		// TODO: Create tests. [Bill, 8/13/2023]
		IReadOnlyList<Entry> input = this.GetGroupedEntries();
		IReadOnlyList<Entry> tab = this.GroupByEnvironment<TablatureLine>(input, "tab");
		IReadOnlyList<Entry> grid = this.GroupByEnvironment<ChordProGridLine>(tab, "grid");
		IReadOnlyList<Entry> result = this.ConvertEntries(grid);
		this.SetEntries(result);
		return this;
	}

	#endregion

	#region Private Methods

	private IReadOnlyList<Entry> ConvertEntries(IReadOnlyList<Entry> input)
	{
		List<Entry> output = new(input.Count);
		ChordProDirectiveLine? pendingEnd = null;

		foreach (Entry entry in input)
		{
			switch (entry)
			{
				case ChordLyricPair pair:
					Add(ChordProLyricLine.Convert(pair));
					break;

				case ChordLine chord:
					Add(ChordProLyricLine.Convert(chord));
					break;

				case Comment comment:
					if (comment.Prefix == "#")
					{
						Add(new ChordProRemarkLine(comment.ToString()), comment.Annotations);
					}
					else
					{
						Add(new ChordProDirectiveLine(nameof(comment), comment.Text), comment.Annotations);
					}

					break;

				case IEntryContainer container:
					Add(new Section(this.ConvertEntries(container.Entries)), entry.Annotations);
					break;

				case HeaderLine header:
					AddPendingEnd();
					(ChordProDirectiveLine start, pendingEnd) = ChordProDirectiveLine.Convert(header, this.preferLongNames);
					Add(start);
					break;

				case ChordDefinitions definitions:
					foreach (ChordDefinition definition in definitions.Definitions)
					{
						Add(ChordProDirectiveLine.Convert(definition));
					}

					AddAnnotations(definitions.Annotations);
					break;

				default:
					Add(entry);
					break;
			}
		}

		AddPendingEnd();
		return output;

		void Add(Entry newEntry, IReadOnlyList<Entry>? annotations = null)
		{
			Entry nonAnnotatedEntry = newEntry.Annotations.Count == 0 ? newEntry : newEntry.Clone(null);
			output.Add(nonAnnotatedEntry);

			annotations ??= newEntry.Annotations;
			AddAnnotations(annotations);
		}

		void AddAnnotations(IReadOnlyList<Entry> annotations)
		{
			if (annotations.Count > 0)
			{
				IReadOnlyList<Entry> converted = this.ConvertEntries(annotations);
				output.AddRange(converted);
			}
		}

		void AddPendingEnd()
		{
			if (pendingEnd != null)
			{
				output.Add(pendingEnd);
				pendingEnd = null;
			}
		}
	}

	private IReadOnlyList<Entry> GroupByEnvironment<T>(IReadOnlyList<Entry> input, string suffix)
		where T : Entry
	{
		List<Entry> result = new(input.Count);

		Environment environment = Environment.None;
		foreach (Entry entry in input)
		{
			switch (entry)
			{
				case T target:
					if (environment == Environment.None)
					{
						string startName = this.preferLongNames ?? true ? $"start_of_{suffix}" : $"so{suffix[0]}";
						result.Add(new ChordProDirectiveLine(startName, null));
						environment = Environment.Implicit;
					}

					result.Add(entry);
					break;

				case ChordProDirectiveLine directive:
					if (ChordParser.Comparer.Equals(directive.LongName, $"end_of_{suffix}"))
					{
						environment = Environment.None;
					}
					else
					{
						FinishEnvironment();
						if (ChordParser.Comparer.Equals(directive.LongName, $"start_of_{suffix}"))
						{
							environment = Environment.Explicit;
						}
					}

					result.Add(entry);
					break;

				case ChordLyricPair pair:
					FinishEnvironment();
					result.Add(pair);
					break;

				case IEntryContainer container:
					FinishEnvironment();
					result.Add(new Section(this.GroupByEnvironment<T>(container.Entries, suffix)));
					break;

				default:
					FinishEnvironment();
					result.Add(entry);
					break;
			}
		}

		FinishEnvironment();

		void FinishEnvironment()
		{
			if (environment == Environment.Implicit)
			{
				string endName = this.preferLongNames ?? true ? $"end_of_{suffix}" : $"eo{suffix[0]}";
				result.Add(new ChordProDirectiveLine(endName, null));
				environment = Environment.None;
			}
		}

		return result;
	}

	#endregion
}
