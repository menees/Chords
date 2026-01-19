namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A parsed ChordPro directive name with <see cref="Selector"/> and <see cref="InvertSelection"/> factored out.
/// </summary>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-directives/#conditional-directives"/>
public sealed class ChordProDirectiveName : IEquatable<ChordProDirectiveName>
{
	#region Private Data Members

	private static readonly StringComparer Comparer = ChordParser.Comparer;

	private static readonly Dictionary<string, string> LongNameToShortNameMap = new(Comparer)
	{
		{ "chordfont", "cf" },
		{ "chordsize", "cs" },
		{ "column_break", "colb" },
		{ "columns", "col" },
		{ "comment", "c" },
		{ "comment_box", "cb" },
		{ "comment_italic", "ci" },
		{ "end_of_bridge", "eob" },
		{ "end_of_chorus", "eoc" },
		{ "end_of_grid", "eog" },
		{ "end_of_tab", "eot" },
		{ "end_of_verse", "eov" },
		{ "grid", "g" },
		{ "new_page", "np" },
		{ "new_physical_page", "npp" },
		{ "new_song", "ns" },
		{ "no_grid", "ng" },
		{ "start_of_bridge", "sob" },
		{ "start_of_chorus", "soc" },
		{ "start_of_grid", "sog" },
		{ "start_of_tab", "sot" },
		{ "start_of_verse", "sov" },
		{ "subtitle", "st" },
		{ "textfont", "tf" },
		{ "textsize", "ts" },
		{ "title", "t" },
	};

	private static readonly Dictionary<string, string> ShortNameToLongNameMap = LongNameToShortNameMap
		.ToDictionary(pair => pair.Value, pair => pair.Key, LongNameToShortNameMap.Comparer);

	#endregion

	#region Constructors

	internal ChordProDirectiveName(string name, string? selector = null, bool invertSelection = false)
	{
		this.Name = name;
		this.Selector = selector;
		this.InvertSelection = invertSelection;

		this.LongName = ShortNameToLongNameMap.TryGetValue(this.Name, out string? longName) ? longName : this.Name;
		this.ShortName = LongNameToShortNameMap.TryGetValue(this.Name, out string? shortName) ? shortName : this.Name;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the base name of the directive without any <see cref="Selector"/> suffix.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the optional postfixed selector expression without any prefixed operator to <see cref="InvertSelection"/>.
	/// </summary>
	public string? Selector { get; }

	/// <summary>
	/// Gets whether the <see cref="Selector"/> matching should be inverted.
	/// </summary>
	/// <remarks>
	/// This is indicated with a '!' prefix after the dash and before the <see cref="Selector"/>.
	/// </remarks>
	public bool InvertSelection { get; }

	/// <summary>
	/// Gets the directive's long name form or <see cref="Name"/>.
	/// </summary>
	public string LongName { get; }

	/// <summary>
	/// Gets the directive's short name form or <see cref="Name"/>.
	/// </summary>
	public string ShortName { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets the fully-qualfiied directive <see cref="Name"/> including any optional <see cref="Selector"/>
	/// and operator (i.e., <see cref="InvertSelection"/>).
	/// </summary>
	public override string ToString() => string.IsNullOrEmpty(this.Selector)
		? this.Name
		: $"{this.Name}-{(this.InvertSelection ? "!" : string.Empty)}{this.Selector}";

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		int result = HashCode.Combine(this.Name, this.Selector, this.InvertSelection);
		return result;
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is ChordProDirectiveName other && this.Equals(other);

	/// <inheritdoc/>
	public bool Equals(ChordProDirectiveName? other)
	{
		bool result = ReferenceEquals(this, other)
			|| (other != null
				&& this.Name == other.Name
				&& this.Selector == other.Selector
				&& this.InvertSelection == other.InvertSelection);
		return result;
	}

#endregion

	#region Internal Methods

	/// <summary>
	/// Creates a new instance with the specified <paramref name="name"/> and the
	/// current <see cref="Selector"/> and <see cref="InvertSelection"/> values.
	/// </summary>
	/// <param name="name">The new value for the name.</param>
	/// <returns>A new instance with the specified <paramref name="name"/>
	/// and with the other property values copied from the current instance.
	/// </returns>
	internal ChordProDirectiveName Rename(string name)
		=> new(name, this.Selector, this.InvertSelection);

	internal bool TryGetPreferredName(bool? preferLongNames, [NotNullWhen(true)] out ChordProDirectiveName? preferred)
	{
		bool result = false;
		preferred = null;

		if (preferLongNames is not null
			&& !this.ShortName.Equals(this.LongName, ChordParser.Comparison)
			&& ((preferLongNames.Value && !this.Name.Equals(this.LongName, ChordParser.Comparison))
				|| (!preferLongNames.Value && !this.Name.Equals(this.ShortName, ChordParser.Comparison))))
		{
			preferred = this.Rename(preferLongNames.Value ? this.LongName : this.ShortName);
			result = true;
		}

		return result;
	}

	#endregion
}
