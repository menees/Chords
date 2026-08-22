namespace Menees.Chords.Cli;

#region Using Directives

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;

#endregion

internal sealed class ConvertCommand : BaseCommand
{
	#region Private Data Members

	private const string ReadStdIn = "-";
	private const string DefaultEncoding = "UTF-8";

	private readonly Parsers parsers;
	private readonly Transformer transformer;
	private readonly Encoding inputEncoding;
	private readonly Encoding outputEncoding;
	private readonly FileInfo? input;
	private readonly FileInfo? output;
	private readonly bool overwrite;
	private readonly Formats format;
	private readonly bool clean;
	private readonly bool? preferLongNames;
	private readonly Notation? notation;
	private readonly TransposeOptionValue? transpose;
	private readonly Key? key;
	private readonly DetectKey? detectKey;

	#endregion

	#region Constructors

	private ConvertCommand(
		ParseResult parseResult,
		FileInfo? input,
		Parsers parsers,
		Transformer transformers,
		Encoding[] encodings,
		FileInfo? output,
		bool overwrite,
		Formats format,
		bool clean,
		bool? preferLongNames,
		Notation? notation,
		TransposeOptionValue? transpose,
		Key? key,
		DetectKey? detectKey)
		: base(parseResult)
	{
		this.input = input;
		this.parsers = parsers;
		this.transformer = transformers;
		this.inputEncoding = encodings[0];
		this.outputEncoding = encodings.Length > 1 ? encodings[1] : this.inputEncoding;
		this.output = output;
		this.overwrite = overwrite;
		this.format = format;
		this.clean = clean;
		this.preferLongNames = preferLongNames;
		this.notation = notation;
		this.transpose = transpose;
		this.key = key;
		this.detectKey = detectKey;
	}

	#endregion

	#region Public Methods

	public static Command Create()
	{
		Command result = new("convert", "Converts a chord sheet file from one format to another.");

		Argument<FileInfo?> inputArgument = new(nameof(input))
		{
			Description = $"The file to convert. Use \"{ReadStdIn}\" to read from stdin.",
			CustomParser = argumentResult =>
			{
				// https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial#add-subcommands-and-custom-validation
				FileInfo? fileInfo = null;
				string? filePath = argumentResult.Tokens.Single().Value;
				if (filePath != ReadStdIn)
				{
					if (File.Exists(filePath))
					{
						fileInfo = new FileInfo(filePath);
					}
					else
					{
						argumentResult.AddError("Input file does not exist.");
					}
				}

				return fileInfo;
			},
		};
		result.Add(inputArgument);

		Option<bool> cleanOption = new("--clean", "-c") { Description = "Clean the input lines before parsing." };
		result.Add(cleanOption);

		Option<Parsers> parseOption = new("--parse", "-p")
		{
			DefaultValueFactory = _ => Parsers.Default,
			Description = "How the input file should be parsed.",
		};
		result.Add(parseOption);

		Option<Transformer> transformOption = new("--transform", "-t")
		{
			DefaultValueFactory = _ => Transformer.ChordPro,
			Description = "How the input should be transformed in memory.",
		};
		result.Add(transformOption);

		Option<NullableBool> longOption = new("--longNames", "-l")
		{
			DefaultValueFactory = _ => NullableBool.Null,
			Description = "Prefer long ChordPro directive names.",
			CustomParser = NullableBoolExtensions.ToNullableBool,
		};
		result.Add(longOption);

		Option<Formats> formatOption = new("--format", "-f")
		{
			DefaultValueFactory = _ => Formats.Text,
			Description = "How the output should be formatted.",
			CustomParser = CommandLineParsers.ParseEnum<Formats>,
		};
		result.Add(formatOption);

		MusicOptions musicOptions = new(result);

		Option<FileInfo> outputOption = new("--output", "-o") { Description = "The output file name. Omit to write to stdout." };
		result.Add(outputOption);

		Option<bool> overwriteOption = new("--overwrite", "-y") { Description = "Overwrite the output file if it already exists." };
		result.Add(overwriteOption);

		Option<string[]?> encodingOption = CreateEncodingOption(result);

		result.SetAction((parseResult, cancellationToken) =>
		{
			FileInfo? input = GetArgumentValue(parseResult, inputArgument);
			Parsers parsers = GetOptionValue(parseResult, parseOption);
			Transformer transformers = GetOptionValue(parseResult, transformOption);
			Encoding[] encodings = [.. (GetOptionValue(parseResult, encodingOption) ?? [DefaultEncoding]).Select(e => Encoding.GetEncoding(e))];
			FileInfo? output = GetOptionValue(parseResult, outputOption);
			bool overwrite = GetOptionValue(parseResult, overwriteOption);
			Formats format = GetOptionValue(parseResult, formatOption);
			bool clean = GetOptionValue(parseResult, cleanOption);
			bool? preferLongNames = GetOptionValue(parseResult, longOption).ToStandardType();
			Notation? notation = GetOptionValue(parseResult, musicOptions.Notation);
			TransposeOptionValue? transpose = GetOptionValue(parseResult, musicOptions.Transpose);
			Key? key = GetOptionValue(parseResult, musicOptions.Key);
			DetectKey? detectKey = GetOptionValue(parseResult, musicOptions.DetectKey);

			ConvertCommand command = new(
				parseResult,
				input,
				parsers,
				transformers,
				encodings,
				output,
				overwrite,
				format,
				clean,
				preferLongNames,
				notation,
				transpose,
				key,
				detectKey);
			return command.ExecuteAsync(cancellationToken);
		});

		return result;
	}

	#endregion

	#region Protected Methods

	protected override Task<int?> OnExecuteAsync(CancellationToken cancellationToken)
	{
		Document inputDocument = this.ParseInput();
		Document outputDocument = this.TransformInMemory(inputDocument);
		string outputText = this.FormatOutputText(outputDocument);
		this.WriteOutput(outputText);
		return Task.FromResult(this.ExitCode);
	}

	#endregion

	#region Private Methods

	private static Option<string[]?> CreateEncodingOption(Command command)
	{
		Option<string[]?> result = new("--encoding", "-e")
		{
			DefaultValueFactory = _ => [DefaultEncoding],
			Description = "How the input and output text are encoded. Takes 1 or 2 encoding names.",
			AllowMultipleArgumentsPerToken = true,
		};
		result.Validators.Add(optionResult =>
		{
			if (optionResult.GetValue(result) is string[] array && array.Length > 2)
			{
				optionResult.AddError("No more than two encodings can be specified.");
			}
		});
		command.Add(result);
		return result;
	}

	private Document ParseInput()
	{
		bool readStdIn = this.input is null;
		TextReader reader = readStdIn ? Console.In : new StreamReader(this.input!.FullName, this.inputEncoding, true);
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
		DocumentTransformer transformer = this.transformer switch
		{
			Transformer.MobileSheets => new MobileSheetsTransformer(inputDocument, this.preferLongNames),
			Transformer.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
			_ => new ChordProTransformer(inputDocument, this.preferLongNames),
		};
		Document result = transformer.Transform().Document;
		if (this.transpose is not null)
		{
			TransposeTransformer transposeTransformer = this.key is not null
				? new(result, this.transpose.HalfSteps, this.key, this.transpose.AccidentalPreference)
				: new(
					result,
					this.transpose.HalfSteps,
					this.transpose.AccidentalPreference,
					this.detectKey ?? DetectKey.MetadataOnly);
			result = transposeTransformer.Transform().Document;
		}

		if (this.notation is not null)
		{
			result = new NotationTransformer(result, this.notation.Value).Transform().Document;
		}

		return result;
	}

	private string FormatOutputText(Document outputDocument)
	{
		ContainerFormatter formatter = this.format switch
		{
			Formats.Xml => new XmlFormatter(outputDocument),
			Formats.Html => new HtmlFormatter(outputDocument),
			_ => new TextFormatter(outputDocument),
		};
		string outputText = formatter.ToString();
		return outputText;
	}

	private void WriteOutput(string outputText)
	{
		if (this.output is null)
		{
			this.Write(outputText);
		}
		else if (this.overwrite || !this.output.Exists)
		{
			File.WriteAllText(this.output.FullName, outputText, this.outputEncoding);
		}
		else
		{
			this.WriteErrorLine("The specified output file already exists, and the --overwrite option was not used.");
			this.ExitCode = 1;
		}
	}

	#endregion

	#region Private Types

	private sealed class MusicOptions
	{
		public MusicOptions(Command command)
		{
			this.Notation = new("--notation")
			{
				Description = "Change all chords to the specified notation.",
				CustomParser = CommandLineParsers.ParseNullableEnum<Notation>,
			};
			command.Add(this.Notation);

			this.Transpose = new("--transpose")
			{
				Description = "Transpose by a signed byte with an optional Default, Sharps, or Flats preference.",
				CustomParser = CommandLineParsers.ParseTranspose,
				Arity = new ArgumentArity(1, 2),
				AllowMultipleArgumentsPerToken = true,
			};
			command.Add(this.Transpose);

			this.Key = new("--key")
			{
				Description = "Use an explicit song key when transposing.",
				CustomParser = CommandLineParsers.ParseKey,
			};
			command.Add(this.Key);

			this.DetectKey = new("--detectKey")
			{
				Description = "Detect the song key from metadata, the first chord, or the last chord when transposing.",
				CustomParser = CommandLineParsers.ParseNullableEnum<DetectKey>,
			};
			command.Add(this.DetectKey);
			command.Validators.Add(this.Validate);
		}

		public Option<DetectKey?> DetectKey { get; }

		public Option<Key?> Key { get; }

		public Option<Notation?> Notation { get; }

		public Option<TransposeOptionValue?> Transpose { get; }

		private void Validate(CommandResult commandResult)
		{
			TransposeOptionValue? transpose = commandResult.GetValue(this.Transpose);
			Key? key = commandResult.GetValue(this.Key);
			DetectKey? detectKey = commandResult.GetValue(this.DetectKey);
			if (key is not null && detectKey is not null)
			{
				commandResult.AddError("--key and --detectKey cannot be used together.");
			}

			if (transpose is null && (key is not null || detectKey is not null))
			{
				commandResult.AddError("--key and --detectKey can only be used with --transpose.");
			}
		}
	}

	#endregion
}
