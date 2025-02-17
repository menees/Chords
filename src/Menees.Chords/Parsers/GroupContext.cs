namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Text;

#endregion

/// <summary>
/// Provides the <see cref="DocumentParser"/>'s current <see cref="Entry"/> grouping context.
/// </summary>
public sealed class GroupContext
{
	#region Constructors

	internal GroupContext(DocumentParser parser)
	{
		this.Parser = parser;
		this.Entries = [];
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the associated document parser.
	/// </summary>
	public DocumentParser Parser { get; }

	/// <summary>
	/// Gets the (input) entries that need to be grouped.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; internal set; }

	#endregion
}
