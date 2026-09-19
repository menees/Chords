#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class PerformanceInputPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 16;
	private const int ControlSpacing = 8;
	private const int CaptureHeight = 140;
	private readonly BookSession session;
	private readonly PerformanceBindingsSnapshot original;
	private readonly List<PerformanceKeyBinding> bindings;
	private readonly Picker commands = new() { Title = "Command", ItemsSource = Enum.GetNames<PerformanceCommand>(), SelectedIndex = 0 };
	private readonly Picker existing = new() { Title = "Assigned keys" };
	private readonly Label status = new();
	private readonly WebView capture = new() { HeightRequest = CaptureHeight };
	private readonly TaskCompletionSource<bool> completion = new();
	private PerformanceKeyGesture? learned;
	private bool listening;
	private bool busy;

	#endregion

	#region Constructors

	public PerformanceInputPage(BookSession session)
	{
		this.session = session;
		this.original = session.Input.GetBindings();
		this.bindings = [.. this.original.Bindings];
		this.Title = "Keyboard and Pedals";
		Button learn = new() { Text = "Learn a Key / Pedal…" };
		learn.Clicked += (_, _) => this.BeginLearning();
		Button assign = new() { Text = "Assign Learned Key" };
		assign.Clicked += (_, _) => this.Assign();
		Button remove = new() { Text = "Remove Selected Binding" };
		remove.Clicked += (_, _) =>
		{
			if (this.existing.SelectedIndex >= 0 && !this.busy)
			{
				this.bindings.RemoveAt(this.existing.SelectedIndex);
				this.RefreshBindings();
			}
		};
		Button reset = new() { Text = "Restore Default Bindings" };
		reset.Clicked += (_, _) =>
		{
			if (!this.busy)
			{
				this.bindings.Clear();
				this.bindings.AddRange(PerformanceKeyBinding.CreateDefaults());
				this.RefreshBindings();
			}
		};
		FluentIconButton save = new() { Icon = "Save", Description = "Save" };
		save.Clicked += async (_, _) => await this.SaveAsync().ConfigureAwait(true);
		FluentIconButton cancel = new() { Icon = "Dismiss", Description = "Cancel" };
		cancel.Clicked += async (_, _) => await this.CloseAsync(false).ConfigureAwait(true);
		this.capture.Navigating += this.HandleNavigating;
		this.capture.Navigated += (_, _) => this.capture.Focus();
		ScrollView body = new()
		{
			Content = new VerticalStackLayout
			{
				Padding = PagePadding,
				Spacing = ControlSpacing,
				Children =
				{
					new Label { Text = "Bindings apply while the chart has focus. Modifiers work; held-key repeats are ignored." },
					this.existing, remove, reset, this.commands, learn, this.capture, this.status, assign,
				},
			},
		};
		this.Content = DialogLayout.Create("Keyboard and Pedal Commands", body, save, cancel);
		this.RefreshBindings();
	}

	#endregion

	#region Public Properties

	public Task<bool> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		_ = this.CloseAsync(false);
		return true;
	}

	#endregion

	#region Private Methods

	private static string Describe(PerformanceKeyGesture gesture)
		=> (gesture.Control ? "Ctrl+" : string.Empty) + (gesture.Alt ? "Alt+" : string.Empty)
			+ (gesture.Shift ? "Shift+" : string.Empty) + (gesture.Meta ? "Meta+" : string.Empty)
			+ (gesture.Key == " " ? "Space" : gesture.Key);

	private void RefreshBindings()
	{
		this.existing.ItemsSource = this.bindings.Select(binding => Describe(binding.Gesture) + " → " + binding.Command).ToArray();
		this.existing.SelectedIndex = this.bindings.Count > 0 ? 0 : -1;
	}

	private void BeginLearning()
	{
		if (!this.busy)
		{
			this.listening = true;
			this.learned = null;
			this.status.Text = "Click the area below, then press one key or pedal.";
			this.capture.Source = new HtmlWebViewSource
			{
				Html = """
					<html><head><meta name="viewport" content="width=device-width, initial-scale=1"></head>
					<body tabindex="0" style="font:18px system-ui;padding:20px;background:#eee;color:#111">
					Click here, then press the key or pedal to learn.
					<script>
					window.addEventListener('keydown', e => {
						e.preventDefault(); e.stopImmediatePropagation();
						if (!e.repeat) location.href = 'chordbook://learn/' + encodeURIComponent(JSON.stringify({
							key:e.key,control:e.ctrlKey,alt:e.altKey,shift:e.shiftKey,meta:e.metaKey
						}));
					}, true);
					document.body.focus();
					</script></body></html>
					""",
			};
		}
	}

	private void HandleNavigating(object? sender, WebNavigatingEventArgs args)
	{
		if (args.Url.StartsWith("chordbook:", StringComparison.OrdinalIgnoreCase))
		{
			args.Cancel = true;
			if (this.listening && PerformanceInputService.TryReadLearnedGesture(args.Url, out PerformanceKeyGesture? gesture))
			{
				this.learned = gesture;
				this.listening = false;
				this.status.Text = "Learned " + Describe(gesture!) + ". Choose a command and assign it.";
			}
		}
	}

	private void Assign()
	{
		if (!this.busy && this.learned is not null && this.commands.SelectedIndex >= 0)
		{
			PerformanceKeyBinding binding = new(this.learned, (PerformanceCommand)this.commands.SelectedIndex);
			List<PerformanceKeyBinding> next = [.. this.bindings.Where(item => item.Gesture != this.learned), binding];
			try
			{
				PerformanceInputService.Validate(next);
				this.bindings.Clear();
				this.bindings.AddRange(next);
				this.RefreshBindings();
				this.status.Text = "Assigned " + Describe(this.learned) + ". Save to apply.";
			}
			catch (ArgumentException exception)
			{
				this.status.Text = exception.Message;
			}
		}
	}

	private async Task SaveAsync()
	{
		if (!this.busy)
		{
			this.busy = true;
			try
			{
				await Task.Run(() => this.session.Input.SaveBindingsAsync(this.original, this.bindings, this.session.DeviceId)).ConfigureAwait(true);
				await this.Navigation.PopModalAsync().ConfigureAwait(true);
				this.completion.TrySetResult(true);
			}
			catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
			{
				this.status.Text = exception.Message;
			}
			finally
			{
				this.busy = false;
			}
		}
	}

	private async Task CloseAsync(bool saved)
	{
		if (!this.busy)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(saved);
		}
	}

	#endregion
}
