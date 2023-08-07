namespace Menees.Chords;

#region Using Directives

using System.IO;

#endregion

public static class TestUtility
{
	#region Public Properties

	public static string SwingLowSweetChariotFileName { get; } = GetSampleFileName("Swing Low Sweet Chariot.cho");

	#endregion

	#region Public Methods

	public static string GetSampleFileName(string fileName)
		=> Path.Combine("Samples", fileName);

	public static Document LoadSwingLowSweetChariot()
		=> Document.Load(SwingLowSweetChariotFileName);

	#endregion
}
