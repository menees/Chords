namespace Menees.Chords.Cli;

#region Using Directives

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;
using Menees.Shell;

#endregion

internal sealed class ConvertCommand
{
	#region Private Data Members

	private Parsers parsers;
	private Transformers transformers;
	private Encoding encoding = Encoding.UTF8;
	private FileInfo? input;
	private FileInfo? output;
	private bool overwrite;
	private Formats format;
	private bool clean;

	#endregion

	#region Constructors

	private ConvertCommand()
	{
	}

	#endregion

	#region Public Methods

	public static ConvertCommand? TryCreate(CommandLine commandLine, string[] args)
	{
		string appName = Path.GetFileNameWithoutExtension(CommandLine.ExecutableFileName);
		string versionInfo = ShellUtility.GetVersionInfo(typeof(ConvertCommand).Assembly);
		commandLine.AddHeader($"{appName} - {versionInfo}");
		commandLine.AddHeader("Converts a chord sheet file from one format to another.");
		commandLine.AddHeader($"Usage: {CommandLine.ExecutableFileName} input|- [/output File] [/overwrite] [/clean]");
		commandLine.AddHeader("            [/parse ...] [/transform ...] [/format ...] [/encoding ...]");

		ConvertCommand command = new();
		const string ReadStdIn = "-";
		bool useStdIn = false;
		commandLine.AddValueHandler(
			(filePath, errors) =>
			{
				if (filePath == ReadStdIn)
				{
					useStdIn = true;
				}
				else if (File.Exists(filePath))
				{
					command.input = new FileInfo(filePath);
				}
				else
				{
					errors.Add("Input file does not exist.");
				}
			},
			new KeyValuePair<string, string>(nameof(input), $"The file to convert. Use \"{ReadStdIn}\" to read from stdin."));

		commandLine.AddSwitch(
			nameof(output),
			"The output file name. Omit to write to stdout.",
			(value, errors) => command.output = new FileInfo(value));

		commandLine.AddSwitch(
			nameof(overwrite),
			"Whether to overwrite the output file if it already exists.",
			value => command.overwrite = value);

		commandLine.AddSwitch(
			nameof(clean),
			"Whether to clean (i.e., scrub) the input lines before parsing.",
			value => command.clean = value);

		commandLine.AddSwitch(
			"parse",
			GetDescription("How the input file should be parsed.", command.parsers),
			(value, errors) => TryParse(value, errors, ref command.parsers));

		commandLine.AddSwitch(
			"transform",
			GetDescription("How the input should be transformed in memory.", command.transformers),
			(value, errors) => TryParse(value, errors, ref command.transformers));

		commandLine.AddSwitch(
			nameof(format),
			GetDescription("How the output should be formatted.", command.format),
			(value, errors) => TryParse(value, errors, ref command.format));

		commandLine.AddSwitch(
			nameof(encoding),
			$"How the input and output text is encoded. [default: {command.encoding.WebName}]",
			(value, errors) =>
			{
				try
				{
					command.encoding = Encoding.GetEncoding(value);
				}
				catch (ArgumentException ex)
				{
					errors.Add($"Unable to find encoding {value}. {ex.Message}");
				}
			});

		commandLine.AddFinalValidation(errors =>
		{
			if (!useStdIn && command.input is null)
			{
				errors.Add($"An input file (or \"{ReadStdIn}\" for stdin) is required.");
			}
		});

		// LONG-TERM-TODO: Other possible command line options:
		// transpose - +N use sharps. -N use flats. Like https://www.chordpro.org/chordpro/using-chordpro/#transpose
		// normalize - Call Chord.Normalize() for every parsed Chord in a document.
		// compact - Replace identical start/end_of_chorus sections with {chorus} directive. One blank line per section.
		// clean - Run Cleaner class first to remove extra whitespace lines.
		CommandLineParseResult parseResult = commandLine.Parse(args);
		ConvertCommand? result = parseResult == CommandLineParseResult.Valid ? command : null;
		return result;
	}

	public void Execute()
	{
		Document inputDocument = this.ParseInput();
		Document outputDocument = this.TransformInMemory(inputDocument);
		string outputText = this.FormatOutputText(outputDocument);
		this.WriteOutput(outputText);
	}

	#endregion

	#region Private Methods

	private static string GetDescription<T>(string description, T defaultValue)
		where T : struct, Enum
	{
		StringBuilder sb = new(description);
		sb.Append(" (");
		sb.AppendJoin(
			"|",
			Enum.GetValues(typeof(T)).Cast<T>()
				.Select(e => e.Equals(defaultValue) ? $"[{e}]" : e.ToString()));
		sb.Append(')');
		string result = sb.ToString();
		return result;
	}

	private static void TryParse<T>(string text, IList<string> errors, ref T value)
		where T : struct, Enum
	{
		if (!Enum.TryParse(text, out value))
		{
			errors.Add($"Unable to parse {text} as {typeof(T).Name}.");
		}
	}

	private Document ParseInput()
	{
		bool readStdIn = this.input is null;
		TextReader reader = readStdIn ? Console.In : new StreamReader(this.input!.FullName, this.encoding, true);
		try
		{
			DocumentParser parser = new(
				this.parsers == Parsers.ChordPro
					? DocumentParser.ChordProLineParsers
					: DocumentParser.DefaultLineParsers);

			Document inputDocument;
			if (this.clean)
			{
				Cleaner cleaner = new(reader.ReadToEnd());
				inputDocument = Document.Parse(cleaner.CleanText, parser);
			}
			else
			{
				inputDocument = Document.Load(reader, parser);
			}

			return inputDocument;
		}
		finally
		{
			if (!readStdIn)
			{
				reader.Dispose();
			}
		}
	}

	private Document TransformInMemory(Document inputDocument)
	{
		DocumentTransformer transformer = this.transformers switch
		{
			Transformers.MobileSheets => new MobileSheetsTransformer(inputDocument),
			Transformers.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
			_ => new ChordProTransformer(inputDocument),
		};
		Document outputDocument = transformer.Transform().Document;
		return outputDocument;
	}

	private string FormatOutputText(Document outputDocument)
	{
		ContainerFormatter formatter = this.format == Formats.Xml
			? new XmlFormatter(outputDocument)
			: new TextFormatter(outputDocument);
		string outputText = formatter.ToString();
		return outputText;
	}

	private void WriteOutput(string outputText)
	{
		if (this.output is null)
		{
			Console.Write(outputText);
		}
		else if (this.overwrite || !this.output.Exists)
		{
			File.WriteAllText(this.output.FullName, outputText, this.encoding);
		}
		else
		{
			Console.Error.WriteLine("The specified output file already exists, and the --overwrite option was not used.");
			Environment.ExitCode = 1;
		}
	}

	#endregion
}
