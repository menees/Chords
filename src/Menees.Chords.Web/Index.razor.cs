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

	private static readonly Encoding UTF8 = Encoding.UTF8;

	private readonly CancellationTokenSource cts = new();

	private string fromType = "General";
	private Transformer toType = Transformer.ChordPro;
	private string input = string.Empty;
	private string output = string.Empty;
	private bool whenTyping = true;
	private CopyState copyState = new("Copy", IconName.Copy, "btn-secondary");
	private ElementReference? inputElement;
	private Document? outputDocument;

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

	public string FromType
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
			this.fromType = this.Storage.GetItem<string>(nameof(this.fromType)) ?? this.fromType;
		}

		if (this.Storage.ContainKey(nameof(this.toType)))
		{
			this.toType = this.Storage.GetItem<Transformer>(nameof(this.toType));
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
			this.output = string.Empty;
		}
		else
		{
			DocumentParser parser = new(this.fromType == "ChordPro"
				? DocumentParser.ChordProLineParsers
				: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Parse(this.input, parser);
			DocumentTransformer transformer = this.toType switch
			{
				Transformer.MobileSheets => new MobileSheetsTransformer(inputDocument),
				Transformer.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
				_ => new ChordProTransformer(inputDocument),
			};
			this.outputDocument = transformer.Transform().Document;
			TextFormatter formatter = new(this.outputDocument);
			this.output = formatter.ToString();
			this.StateHasChanged();
		}
	}

	private async Task CopyToClipboardAsync()
	{
		// Writing to the clipboard may be denied, so you must handle the exception
		var temp = this.copyState;
		try
		{
			this.copyState = new("Copied", IconName.Success, "btn-success", IsDisabled: true);
			await this.JavaScript.InvokeVoidAsync("CopyOutputToClipboard", this.output);
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

			string? title = TryGetDirectiveArgument(nameof(title));
			string? artist = TryGetDirectiveArgument(nameof(artist));

			string? TryGetDirectiveArgument(string longName)
				=> directives.FirstOrDefault(directive => directive.LongName.Equals(longName, ChordParser.Comparison))?.Argument;

			sb.Append(title);

			if (sb.Length > 0 && !string.IsNullOrEmpty(artist))
			{
				sb.Append(" - ");
			}

			sb.Append(artist);

			if (sb.Length == 0 && flattenedOutputEntries.Count > 0)
			{
				sb.Append(flattenedOutputEntries[0]);
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
				: $"# {fileName}{newLine}{text}";
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