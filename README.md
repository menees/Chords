TODO: NuGet

# Chords
This repo contains a .NET library and application for parsing, transforming, and reformatting chord sheets.
It can parse files in human-friendly "chords over text" format (e.g., from [Ultimate Guitar](https://www.ultimate-guitar.com/)),
machine-friendly [ChordPro](https://www.chordpro.org/) format, or a mix of both. For example, given this "chords over text" input:

``` text
[Verse 2]
D          G      C          G
 Bring him peace,  bring him joy,
C      Bm     Am      C      D
 He is young,   he is only a boy.
```

Running `.\Menees.Chords.Cli.exe convert` with that input produces this output in ChordPro format:

``` text
{start_of_verse: Verse 2}
[D] Bring him [G]peace, [C] bring him [G]joy,
[C] He is [Bm]young, [Am]  he is [C]only a [D]boy.
{end_of_verse}
```

## Library
The `Menees.Chords.dll` .NET library is the main focus of this repo, and it contains all of the parsing, transforming, 
and formatting API. It can be reused in any application targeting .NET Framework 4.8, .NET 6.0, or .NET Standard 2.0.

### Code Example
``` C#
// Parsing
Document inputDocument = Document.Load(inputFileName);

// Transforming
ChordProTransformer transformer = new ChordProTransformer(inputDocument);
Document outputDocument = transformer.ToChordPro().Document;

// Formatting
ContainerFormatter formatter = new TextFormatter(outputDocument);
string outputText = formatter.ToString();

// Saving
File.WriteAllText(outputFileName, outputText);
```

### Parsing
The `Document` class provides methods to `Load` chord sheet files and to `Parse` chord sheet text into an in-memory
[DOM](https://en.wikipedia.org/wiki/Document_Object_Model). `Menees.Chords.Document` is similar to .NET's 
[XDocument](https://learn.microsoft.com/en-us/dotnet/standard/linq/xdocument-class-overview) class except instead of
XML nodes, `Document` represents a parsed chord sheet as a tree of `Entry` objects in `IEntryContainer`s (e.g., `Section`s).
The `Entry`-derived class hierarchy is:
* [Entry](src/Menees.Chords/Entry.cs)
    - [ChordDefinitions](src/Menees.Chords/ChordDefinitions.cs)
	- [ChordLyricPair](src/Menees.Chords/ChordLyricPair.cs)
	- [ChordProDirectiveLine](src/Menees.Chords/ChordProDirectiveLine.cs)
	- [Section](src/Menees.Chords/Section.cs)
	- [SegmentedEntry](src/Menees.Chords/SegmentedEntry.cs)
    	* [ChordLine](src/Menees.Chords/ChordLine.cs)
		* [ChordProLyricLine](src/Menees.Chords/ChordProLyricLine.cs)
	- [TextEntry](src/Menees.Chords/TextEntry.cs)
    	* [BlankLine](src/Menees.Chords/BlankLine.cs)
		* [ChordProGridLine](src/Menees.Chords/ChordProGridLine.cs)
		* [ChordProRemarkLine](src/Menees.Chords/ChordProRemarkLine.cs)
		* [Comment](src/Menees.Chords/Comment.cs)
		* [HeaderLine](src/Menees.Chords/HeaderLine.cs)
		* [LyricLine](src/Menees.Chords/LyricLine.cs)
		* [TablatureLine](src/Menees.Chords/TablatureLine.cs)

Chord sheets are parsed line-by-line. Parsing can be customized using the `DocumentParser` class with an
ordered collection of specialized line parsers and groupers.

### Transforming
`Document`s (and `Entry`s) are immutable after construction. The `DocumentTransformer` class provides a way
to build a new in-memory `Document` by transforming an existing one. The primary transformer-derived types are:
* [ChordProTransformer](src/Menees.Chords/Transformers/ChordProTransformer.cs) - Transforms "chords over text" (e.g., Ultimate Guitar) syntax into standard ChordPro syntax.
* [MobileSheetsTransformer](src/Menees.Chords/Transformers/MobileSheetsTransformer.cs) - A ChordProTransformer-derived type that restricts the output to a subset of ChordPro syntax compatible with the [MobileSheets](https://zubersoft.com/mobilesheets/) application.

### Formatting
In-memory `Document`s can be formatted as text using one of the `ContainerFormatter`-derived types:
* [TextFormatter](src/Menees.Chords/Formatters/TextFormatter.cs)
* [XmlFormatter](src/Menees.Chords/Formatters/XmlFormatter.cs)

These are useful when saving chord sheets back into text files after transforming them to a new syntax.

### Helpers
The library also contains some helper classes for specialized parsing situtations:
* [Chord](src/Menees.Chords/Chord.cs)
* [ChordDefinition](src/Menees.Chords/ChordDefinition.cs)
* [Lexer](src/Menees.Chords/Parsers/Lexer.cs)

## Application
The `.\Menees.Chords.Cli.exe` .NET console application is a thin wrapper over the `Menees.Chords.dll` library.
Run `.\Menees.Chords.Cli.exe --help` to see its available commands and options. Its primary command is `convert`.
Run `.\Menees.Chords.Cli.exe convert --help` to see its arguments and options.