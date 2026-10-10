#region Using Directives

using System.Threading;
using Menees.Chords.Db;
using Menees.Chords.Formatters;
using Menees.Chords.Transformers;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Explicit conversion of an editor buffer using the shared musical transformers.</summary>
public static class SongTextConversion
{
	#region Public Methods

	public static string Convert(string source, SourceFormat target, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (target is not (SourceFormat.ChordPro or SourceFormat.ChordOverText))
		{
			throw new ArgumentException("Choose ChordPro or chords over text.", nameof(target));
		}

		cancellationToken.ThrowIfCancellationRequested();
		string result = source;
		if (!string.IsNullOrWhiteSpace(source))
		{
			Document document = Document.Parse(source);
			cancellationToken.ThrowIfCancellationRequested();
			DocumentTransformer transformer = target == SourceFormat.ChordPro
				? new ChordProTransformer(document) : new ChordOverLyricTransformer(document);
			result = new TextFormatter(transformer.Transform().Document).ToString();
			cancellationToken.ThrowIfCancellationRequested();
		}

		return result;
	}

	#endregion
}
