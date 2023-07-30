namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;

#endregion

/// <summary>
/// One or more related, parsed lines from a <see cref="Document"/>.
/// </summary>
public abstract class Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	protected Entry()
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets the text representation of the current entry.
	/// </summary>
	public abstract override string ToString();

	#endregion
}
