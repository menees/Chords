namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Text;

#endregion

/// <summary>
/// A ChordPro meta-data directive line like {meta: name value}.
/// </summary>
public sealed class ChordProMetaDirectiveLine : ChordProDirectiveLine
{
	#region Constructors

	internal ChordProMetaDirectiveLine(
		string metadataName,
		string metadataValue,
		string? argument,
		IReadOnlyDictionary<string, string>? attributes = null)
		: base("meta", argument ?? $"{metadataName} {metadataValue}", attributes)
	{
		this.MetadataName = metadataName;
		this.MetadataValue = metadataValue;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the meta-data's name, e.g. "artist".
	/// </summary>
	public string MetadataName { get; }

	/// <summary>
	/// Gets the meta-data's value, e.g. "The Beatles".
	/// </summary>
	public string MetadataValue { get; }

	#endregion
}
