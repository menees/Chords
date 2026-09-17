using System.IO;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Supplies either rendered HTML or a factory for the selected PDF's seekable read stream.</summary>
public sealed record DocumentViewerContent(string? Html, Func<Stream>? OpenPdf = null, DocumentViewerPosition? Position = null,
	IReadOnlyList<PerformanceKeyBinding>? InputBindings = null);
