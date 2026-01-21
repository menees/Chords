namespace Menees.Chords.Web;

#region Using Directives

using System.Linq;
using System.Text;
using Blazored.LocalStorage;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

#endregion

public sealed partial class Index : IDisposable
{
	#region Private Data Members

	private const string MetaFileName = "filename";

	private static readonly Encoding UTF8 = Encoding.UTF8;

	private readonly CancellationTokenSource cts = new();

	private Parser fromType = Parser.General;
	private Transformer toType = Transformer.ChordPro;
	private string input = string.Empty;
	private string output = string.Empty;
	private bool whenTyping = true;
	private bool longNames = true;
	private CopyState copyState = new("Copy", IconName.Copy, "btn-secondary");
	private ElementReference? inputElement;
	private Document? outputDocument;

	#endregion

	#region Private Enums

	private enum Parser
	{
		General,
		ChordPro,
	}

	#endregion

	#region Public Injected Properties

	[Inject]
	public ISyncLocalStorageService Storage { get; set; } = null!; // Set by DI.

	[Inject]
	public IJSRuntime JavaScript { get; set; } = null!; // Set by DI.

	[Inject]
	public HttpClient Http { get; set; } = null!; // Set by DI.

	#endregion

	#region Public Properties

	public string Input
	{
		get => this.input;
		set
		{
			if (this.input != value)
			{
				this.input = value;
				this.Storage.SetItem(nameof(this.input), this.input);
				if (this.whenTyping)
				{
					this.ConvertInput();
				}
			}
		}
	}

	public bool LongNames
	{
		get => this.longNames;
		set
		{
			if (this.longNames != value)
			{
				this.longNames = value;
				this.Storage.SetItem(nameof(this.longNames), this.longNames);
				this.ConvertInput();
			}
		}
	}

	public bool WhenTyping
	{
		get => this.whenTyping;
		set
		{
			if (this.whenTyping != value)
			{
				this.whenTyping = value;
				this.Storage.SetItem(nameof(this.whenTyping), this.whenTyping);
				if (this.whenTyping)
				{
					this.ConvertInput();
				}
			}
		}
	}

	#endregion

	#region Internal Properties

	internal Transformer ToType
	{
		get => this.toType;
		set
		{
			if (this.toType != value)
			{
				this.toType = value;
				this.Storage.SetItem(nameof(this.toType), this.toType);
				this.ConvertInput();
			}
		}
	}

	#endregion

	#region Private Properties

	private Parser FromType
	{
		get => this.fromType;
		set
		{
			if (this.fromType != value)
			{
				this.fromType = value;
				this.Storage.SetItem(nameof(this.fromType), this.fromType);
				this.ConvertInput();
			}
		}
	}

	// IntelliSense kept showing an error if this was inlined in the @bind:event syntax.
	private string InputChangeEvent => this.whenTyping ? "oninput" : "onchange";

	#endregion

	#region Public Methods

	public void Dispose()
	{
		this.cts.Cancel(); // Cancel Task.Delay
		this.cts.Dispose();
	}

	#endregion

	#region Protected Methods

	protected override async Task OnInitializedAsync()
	{
		if (this.Storage.ContainKey(nameof(this.fromType)))
		{
			// Old versions stored fromType as a string; new versions use an int (for the enum).
			// GetItem<Parser> throws a JsonException if it finds a string, so we'll use
			// Enum.TryParse, which can handle either format.
			string? fromType = this.Storage.GetItem<string>(nameof(this.fromType));
			this.fromType = Enum.TryParse(fromType, out Parser parsed) ? parsed : this.fromType;
		}

		if (this.Storage.ContainKey(nameof(this.toType)))
		{
			// Old versions stored toType as a string; new versions use an int (for the enum).
			// GetItem<Transformer> throws a JsonException if it finds a string, so we'll use
			// Enum.TryParse, which can handle either format.
			string? toType = this.Storage.GetItem<string>(nameof(this.toType));
			this.toType = Enum.TryParse(toType, out Transformer parsed) ? parsed : this.toType;
		}

		if (this.Storage.ContainKey(nameof(this.longNames)))
		{
			this.longNames = this.Storage.GetItem<bool>(nameof(this.longNames));
		}

		if (this.Storage.ContainKey(nameof(this.whenTyping)))
		{
			this.whenTyping = this.Storage.GetItem<bool>(nameof(this.whenTyping));
		}

		if (this.Storage.ContainKey(nameof(this.input)))
		{
			this.input = this.Storage.GetItem<string>(nameof(this.input)) ?? this.input;
		}
		else
		{
			this.input = await this.Http.GetStringAsync("Default.crd");
		}

		this.ConvertInput();
	}

	#endregion

	#region Private Methods

	private void ConvertInput()
	{
		if (string.IsNullOrWhiteSpace(this.input))
		{
			this.outputDocument = null;
			this.output = string.Empty;
		}
		else
		{
			DocumentParser parser = new(this.fromType == Parser.ChordPro
				? DocumentParser.ChordProLineParsers
				: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Parse(this.input, parser);
			DocumentTransformer transformer = this.toType switch
			{
				Transformer.MobileSheets => new MobileSheetsTransformer(inputDocument, this.longNames),
				Transformer.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
				_ => new ChordProTransformer(inputDocument, this.longNames),
			};
			this.outputDocument = transformer.Transform().Document;
			TextFormatter formatter = new(this.outputDocument);
			this.output = formatter.ToString();
		}

		this.StateHasChanged();
	}

	private async Task CopyToClipboardAsync(string text, string? elementId)
	{
		// Writing to the clipboard may be denied, so we must handle the exception
		var temp = this.copyState;
		try
		{
			this.copyState = new("Copied", IconName.Success, "btn-success", IsDisabled: true);
			await this.JavaScript.InvokeVoidAsync("CopyToClipboard", text, elementId);
		}
		catch (JSException ex)
		{
			Console.WriteLine($"Cannot write text to clipboard: {ex}");
			this.copyState = new("Failed", IconName.Warning, "btn-danger", IsDisabled: true);

			// Blazor seems to call StateHasChanged implicitly before invoking the JavaScript,
			// so if we don't do this here, then only the successful "Copied" state will show
			// until the state changes again after the Task.Delay. This forces "Failed" to show.
			this.StateHasChanged();
		}
		finally
		{
			await Task.Delay(TimeSpan.FromSeconds(1), this.cts.Token);
			this.copyState = temp;
		}
	}

	private Task CopyOutputToClipboardAsync()
		=> this.CopyToClipboardAsync(this.output, "output");

	private Task CopyFileNameToClipboardAsync()
		=> this.CopyToClipboardAsync(this.GetFileName(), null);

	private async Task SaveAsync()
	{
		// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
		byte[] fileBytes = UTF8.GetBytes(this.output);
		await this.JavaScript.InvokeVoidAsync("BlazorDownloadFile", this.GetFileName(), "text/plain", fileBytes);
	}

	private string GetFileName()
	{
		StringBuilder sb = new();
		if (this.outputDocument != null)
		{
			IReadOnlyList<Entry> flattenedOutputEntries = DocumentTransformer.Flatten(this.outputDocument.Entries);
			List<ChordProDirectiveLine> directives = [.. flattenedOutputEntries.OfType<ChordProDirectiveLine>()];
			const StringComparison Comparison = ChordParser.Comparison;

			string? inputFileName = directives.Select(d => MetadataEntry.TryParse(d) is MetadataEntry meta
					&& meta.Name.Equals(MetaFileName, Comparison) ? meta.Argument : null).FirstOrDefault();
			if (!string.IsNullOrEmpty(inputFileName) && Path.GetFileNameWithoutExtension(inputFileName) is string nameOnly)
			{
				sb.Append(nameOnly);
			}

			if (sb.Length == 0)
			{
				static string? TryGetDirectiveArgument(List<ChordProDirectiveLine> directives, string longName)
					=> directives.FirstOrDefault(directive => directive.LongName.Equals(longName, Comparison))?.Argument;

				string? title = TryGetDirectiveArgument(directives, nameof(title));
				sb.Append(title);

				string? artist = TryGetDirectiveArgument(directives, nameof(artist));
				if (!string.IsNullOrEmpty(artist))
				{
					if (sb.Length > 0)
					{
						sb.Append(" - ");
					}

					sb.Append(artist);
				}
			}

			if (sb.Length == 0 && flattenedOutputEntries.Count > 0)
			{
				// If there was a usable DirectiveLine or TitleLine, the logic above would have used it.
				string? firstLyrics = flattenedOutputEntries.Select(entry => entry switch
				{
					ChordProLyricLine chordProLyricLine => chordProLyricLine.Split().Lyrics,
					LyricLine lyricLine => lyricLine,
					_ => null,
				}).FirstOrDefault(line => line is not null)?.Text.Trim();

				sb.Append(firstLyrics);
			}
		}

		if (sb.Length == 0)
		{
			sb.Append(this.toType);
		}

		sb.Append(this.toType == Transformer.ChordOverLyric ? ".txt" : ".cho");

		string result = sb.ToString();
		return result;
	}

	private async Task CleanInputAsync()
	{
		Cleaner cleaner = new(this.Input);
		this.Input = cleaner.CleanText;
		if (this.inputElement != null)
		{
			await this.inputElement.Value.FocusAsync();
		}
	}

	private async Task OpenAsync(InputFileChangeEventArgs e)
	{
		IBrowserFile file = e.File;
		string fileName = file.Name;
		string newLine = Environment.NewLine;

		try
		{
			// Enforce a max size to avoid out-of-memory on WASM.
			const long MaxFileBytes = 128 * 1024;
			using var stream = file.OpenReadStream(MaxFileBytes);
			using var reader = new StreamReader(stream, UTF8, detectEncodingFromByteOrderMarks: true);
			string text = await reader.ReadToEndAsync();
			this.Input = string.IsNullOrWhiteSpace(fileName)
				? text
				: $"{{meta: {MetaFileName} {fileName}}}{newLine}{text}";
		}
		catch (IOException ex)
		{
			this.Input = $"Error uploading {fileName}:{newLine}{ex.Message}{newLine}({ex.GetType().Name})";
		}

		this.StateHasChanged();
	}

	#endregion

	#region Private Types

	private sealed record CopyState(string Text, IconName IconName, string ButtonClass, bool IsDisabled = false);

	#endregion
}