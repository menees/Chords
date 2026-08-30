#region Using Directives

using System.Text;
using System.Text.RegularExpressions;

#endregion

namespace Menees.Chords.Db;

/// <summary>Represents a compiled song title or subtitle template.</summary>
public sealed partial class SongTemplate
{
	#region Private Data

	private readonly Part[] parts;

	#endregion

	#region Constructors

	private SongTemplate(Part[] parts) => this.parts = parts;

	#endregion

	#region Public API

	/// <summary>Compiles a reusable template.</summary>
	/// <param name="template">A template containing field tokens such as <c>{title}</c>.</param>
	/// <returns>The compiled template.</returns>
	/// <remarks>
	/// Segments separated by an em dash or middle dot are omitted when all of their fields are
	/// missing. This makes templates such as <c>C:{capo} · T:{tempos} · K:{keys}</c> portable
	/// without leaving labels or punctuation behind.
	/// </remarks>
	public static SongTemplate Compile(string template)
	{
		ArgumentNullException.ThrowIfNull(template);
		List<Part> parts = [];
		int start = 0;
		foreach (Match match in SeparatorRegex().Matches(template))
		{
			parts.Add(new Part(template[start..match.Index], match.Value));
			start = match.Index + match.Length;
		}

		parts.Add(new Part(template[start..], null));
		return new SongTemplate([.. parts]);
	}

	/// <summary>Evaluates this template against normalized song metadata.</summary>
	/// <param name="context">The song template context.</param>
	/// <returns>The rendered text with empty decorated segments omitted.</returns>
	public string Evaluate(SongTemplateContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		StringBuilder result = new();
		string? pendingSeparator = null;
		foreach (Part part in this.parts)
		{
			string rendered = RenderSegment(part.Text, context);
			if (rendered.Length > 0)
			{
				if (result.Length > 0 && pendingSeparator is not null)
				{
					result.Append(pendingSeparator);
				}

				result.Append(rendered);
				pendingSeparator = part.SeparatorAfter;
			}
			else if (result.Length > 0 && part.SeparatorAfter is not null)
			{
				pendingSeparator = part.SeparatorAfter;
			}
		}

		return result.ToString().Trim();
	}

	#endregion

	#region Private Methods

	private static string RenderSegment(string segment, SongTemplateContext context)
	{
		bool hasToken = false;
		bool hasValue = false;
		string rendered = FieldRegex().Replace(segment, match =>
		{
			hasToken = true;
			IReadOnlyList<string> values = context.GetValues(match.Groups["name"].Value);
			hasValue |= values.Count > 0;
			return string.Join(", ", values);
		});
		return hasToken && !hasValue ? string.Empty : rendered.Trim();
	}

	[GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9_-]*)\}", RegexOptions.CultureInvariant)]
	private static partial Regex FieldRegex();

	[GeneratedRegex(@"\s+(?:—|·)\s+", RegexOptions.CultureInvariant)]
	private static partial Regex SeparatorRegex();

	#endregion

	#region Private Types

	private sealed record Part(string Text, string? SeparatorAfter);

	#endregion
}
