[![Windows](https://github.com/menees/Chords/actions/workflows/windows.yml/badge.svg)](https://github.com/menees/Chords/actions/workflows/windows.yml)
[![Ubuntu](https://github.com/menees/Chords/actions/workflows/ubuntu.yml/badge.svg)](https://github.com/menees/Chords/actions/workflows/ubuntu.yml)
[![NuGet Chords](https://img.shields.io/nuget/vpre/Menees.Chords?label=NuGet%20Chords)](https://www.nuget.org/packages/Menees.Chords/)
[![NuGet Cli](https://img.shields.io/nuget/vpre/Menees.Chords.Cli?label=NuGet%20CLI)](https://www.nuget.org/packages/Menees.Chords.Cli/)

# Chords
This repo contains a .NET library and applications (e.g., an online [Blazor WebAssembly app](https://chords.menees.com/)) for parsing, transforming, and reformatting chord sheets.
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
and formatting objects. It can be reused in any application targeting .NET Framework 4.8, .NET 8.0, or .NET Standard 2.0.
<!-- DOTNET_VERSION: Document correct targets in readme. -->

### Code Example
``` C#
// Parsing
Document inputDocument = Document.Load(inputFileName);

// Transforming
ChordProTransformer transformer = new ChordProTransformer(inputDocument);
Document outputDocument = transformer.Transform().Document;

// Formatting
ContainerFormatter formatter = new TextFormatter(outputDocument);
string outputText = formatter.ToString();

// Saving
File.WriteAllText(outputFileName, outputText);
```

### Parsing
The `Document` class provides methods to `Load` chord sheet files and to `Parse` chord sheet text into an in-memory
[DOM](https://en.wikipedia.org/wiki/Document_Object_Model). `Document` is similar to .NET's 
[XDocument](https://learn.microsoft.com/en-us/dotnet/standard/linq/xdocument-class-overview) class except instead of
XML nodes, `Document` represents a parsed chord sheet as a tree of `Entry` objects in `IEntryContainer`s (e.g., `Section`s).
The `Entry`-derived class hierarchy is:
* [Entry](src/Menees.Chords/Entry.cs) - The abtract base for each item in `Document.Entries`.
    - [ChordDefinitions](src/Menees.Chords/ChordDefinitions.cs) - One or more chord definitions on a line (e.g., `G 320033, G7 320001`).
	- [ChordLyricPair](src/Menees.Chords/ChordLyricPair.cs) - Groups a [ChordLine](src/Menees.Chords/ChordLine.cs) and a [LyricLine](src/Menees.Chords/LyricLine.cs) together in a "chords over text" file.
	- [ChordProDirectiveLine](src/Menees.Chords/ChordProDirectiveLine.cs) - A [ChordPro directive](https://www.chordpro.org/chordpro/chordpro-directives/) (e.g., `{title: Grey Street}`)
	- [Section](src/Menees.Chords/Section.cs) - An `IEntryContainer` of related entries (e.g., all lines in a chorus, or a [ChordPro environment](https://www.chordpro.org/chordpro/directives-env/))
	- [SegmentedEntry](src/Menees.Chords/SegmentedEntry.cs) - The abstract base for an entry composed of multiple text segments (e.g., [ChordPro lines](https://www.chordpro.org/chordpro/chordpro-introduction/#the-basics) with bracketed chords and unbracketed lyrics).
    	* [ChordLine](src/Menees.Chords/ChordLine.cs) - A line from a "chords over text" document that just contains chords (and maybe a few annotations).
		* [ChordProLyricLine](src/Menees.Chords/ChordProLyricLine.cs) - A line from a ChordPro document that contains [interlaced chords and lyrics](https://www.chordpro.org/chordpro/chordpro-introduction/#the-basics).
	- [TextEntry](src/Menees.Chords/TextEntry.cs) - The abstract base for a text entry that has a recognizable structure.
    	* [BlankLine](src/Menees.Chords/BlankLine.cs) - A line that's blank or was all whitespace and was trimmed to be blank.
		* [ChordProGridLine](src/Menees.Chords/ChordProGridLine.cs) - A line in a [ChordPro grid environment](https://www.chordpro.org/chordpro/directives-env_grid/).
		* [ChordProRemarkLine](src/Menees.Chords/ChordProRemarkLine.cs) - A `#`-prefixed [ChordPro remark line](https://www.chordpro.org/chordpro/chordpro-introduction/#the-basics) (i.e., a comment for maintainers that's not displayed in rendered content).
		* [Comment](src/Menees.Chords/Comment.cs) - A comment line that should be displayed in the rendered content.
		* [HeaderLine](src/Menees.Chords/HeaderLine.cs) - A bracketed header line that begins a section in "chords over text" format (e.g., `[Verse 1]`).
		* [LyricLine](src/Menees.Chords/LyricLine.cs) - A line of lyrics (or anything that the parser couldn't match to another entry type).
		* [TablatureLine](src/Menees.Chords/TablatureLine.cs) - A tablature line, typically inside a [ChordPro tab environment](https://www.chordpro.org/chordpro/directives-env_tab/).

Chord sheets are parsed line-by-line. Parsing can be customized using the `DocumentParser` class with an
ordered collection of specialized line parsers and groupers.

### Transforming
`Document`s (and `Entry`s) are immutable after construction. The `DocumentTransformer` class provides a way
to build a new in-memory `Document` by transforming an existing one. The primary transformer-derived types are:
* [ChordProTransformer](src/Menees.Chords/Transformers/ChordProTransformer.cs) - Transforms "chords over text" (e.g., Ultimate Guitar) syntax into standard ChordPro syntax.
* [MobileSheetsTransformer](src/Menees.Chords/Transformers/MobileSheetsTransformer.cs) - A ChordProTransformer-derived type that restricts the output to a subset of ChordPro syntax compatible with the [MobileSheets](https://zubersoft.com/mobilesheets/) application.
* [ChordOverLyricTransformer](src/Menees.Chords/Transformers/ChordOverLyricTransformer.cs) - Transforms standard ChordPro syntax into "chords over text" (e.g., Ultimate Guitar) syntax.

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

## Applications
### Web
The [Menees Chord Sheet Converter](https://chords.menees.com/) web application converts [Ultimate Guitar](https://www.ultimate-guitar.com/)-style chords-over-text sheets into [ChordPro](https://www.chordpro.org/) format or [MobileSheets](https://www.zubersoft.com/mobilesheets/) format. The converter is a Blazor WebAssembly app, so it needs to be run in a modern, up-to-date web browser.

Because this is a WebAssembly app, it runs completely in your browser. It never sends any data to an external server. None of the information you enter into it leaves your brower, so it's safe to use it to convert private transcriptions.
 
### Console
The `.\Menees.Chords.Cli.exe` .NET console application is a wrapper over the `Menees.Chords.dll` library.
Run `.\Menees.Chords.Cli.exe --help` to see its available commands and options. This is also available as the `menees-chords` [dotnet tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools). 
#### DotNet Local Tool
It can be installed as a local tool with:
```
dotnet tool install Menees.Chords.Cli --local
```
To run it as a local tool and see the `convert` command's help:
```
dotnet menees-chords convert --help
```
#### DotNet Global Tool
It can be installed as a global tool with:
```
dotnet tool install Menees.Chords.Cli --global
```
To run it as a global tool and see the `convert` command's help:
```
menees-chords convert --help
```
*Note:* The `dotnet` prefix isn't required (or supported) when running it as a global tool.

## Others
Here are some links to similar software that might be of interest:
* [ChordPro](https://www.chordpro.org/chordpro/index.html) - The ChordPro format specification and reference app
* [Ultimate Guitar Tablature Guide](https://www.ultimate-guitar.com/contribution/help/rubric#iii) - Chords formatting rules
* [Ultimate Guitar to ChordPro Converter](https://ultimate.ftes.de/) - Web site for converting chord sheets
* [ChordSheetJS](https://github.com/martijnversluis/ChordSheetJS) - JavaScript chord sheet parser
* [Konves.ChordPro](https://github.com/skonves/Konves.ChordPro) - Older C# chord sheet parser
* [Songpress](https://www.skeed.it/songpress) - Windows and Linux app for chord sheet conversion and printing
* [LaTeX Songs](https://ctan.org/pkg/songs) - Specification for rendering chord sheets via LaTeX type setting