using System.Runtime.InteropServices.WindowsRuntime;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Button = Microsoft.UI.Xaml.Controls.Button;
using CheckBox = Microsoft.UI.Xaml.Controls.CheckBox;

internal static class NativeSavedTabSmoke
{
	public static async Task RunAsync(Microsoft.Maui.Controls.ContentPage host)
	{
		var root = (FrameworkElement)host.Handler!.PlatformView!;
		Guid id = Guid.NewGuid();
		int attempts = 0;
		CustomTabEdit? received = null;
		Task<Guid?> pending = WindowsSavedTabDialog.ShowAsync(host, CustomTabEdit.Create("artist:Eagles"), (draft, _) =>
		{
			received = draft;
			if (++attempts == 1) throw new IOException("Disposable save failure");
			return Task.FromResult(id);
		});
		ContentDialog dialog = await FindDialogAsync(root);
		var fields = (StackPanel)((ScrollViewer)dialog.Content).Content;
		TextBox name = fields.Children.OfType<TextBox>().Single(box => Equals(box.Header, "Tab name"));
		TextBox search = fields.Children.OfType<TextBox>().Single(box => Equals(box.Header, "Search filter"));
		ComboBox grouping = fields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Group by (optional)"));
		ComboBox sorting = fields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Sort songs by"));
		CheckBox descending = fields.Children.OfType<CheckBox>().Single();
		if (dialog.IsPrimaryButtonEnabled || name.Text != "" || search.Text != "artist:Eagles"
			|| grouping.SelectedIndex != 0 || SelectedField(sorting) != "title" || descending.IsChecked != false)
			throw new Exception("New saved-tab defaults or name validation are wrong.");
		name.Text = "Fast practice";
		search.Text = "tag:practice";
		sorting.SelectedItem = sorting.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "tempo");
		descending.IsChecked = true;
		if (!dialog.IsPrimaryButtonEnabled) throw new Exception("Named saved-tab draft cannot be saved.");
		await CaptureAsync(dialog, "saved-tab-dark.png");
		Invoke(dialog, "Save");
		await WaitAsync(() => attempts == 1 && dialog.IsPrimaryButtonEnabled);
		if (name.Text != "Fast practice" || search.Text != "tag:practice" || SelectedField(sorting) != "tempo"
			|| descending.IsChecked != true || !fields.Children.OfType<TextBlock>().Any(block => block.Text == "Disposable save failure"))
			throw new Exception("Failed save lost fields or did not show the error.");
		Invoke(dialog, "Save");
		if (await pending != id || attempts != 2 || received != new CustomTabEdit(null, "Fast practice", "tag:practice", null, "tempo", true))
			throw new Exception("Saved dialog did not return the complete sort draft.");

		root.RequestedTheme = ElementTheme.Light;
		host.Window.Width = 800;
		await Task.Delay(100);
		CustomTabCatalogItem existing = new(id, "Saved", "key:C", "artist", true, "capo", true);
		pending = WindowsSavedTabDialog.ShowAsync(host, CustomTabEdit.Create("temporary search", existing), (_, _) =>
			throw new Exception("Cancel attempted to save."));
		dialog = await FindDialogAsync(root);
		fields = (StackPanel)((ScrollViewer)dialog.Content).Content;
		if (fields.Children.OfType<TextBox>().Single(box => Equals(box.Header, "Search filter")).Text != "key:C"
			|| SelectedField(fields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Sort songs by"))) != "capo"
			|| SelectedField(fields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Group by (optional)"))) != "artist"
			|| fields.Children.OfType<CheckBox>().Single().IsChecked != true)
			throw new Exception("Edit lost saved grouping or sort settings.");
		if (((Microsoft.UI.Xaml.GridLength)dialog.Resources["ContentDialogButtonSpacing"]).Value != 8)
			throw new Exception("Saved-tab action spacing is not 8 DIP.");
		await CaptureAsync(dialog, "saved-tab-light-narrow.png");
		Invoke(dialog, "Cancel");
		if (await pending is not null) throw new Exception("Cancel returned a saved identity.");
		root.RequestedTheme = ElementTheme.Dark;
	}

	private static string? SelectedField(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

	private static async Task<ContentDialog> FindDialogAsync(FrameworkElement root)
	{
		ContentDialog? dialog = null;
		await WaitAsync(() =>
		{
			dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
				.SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().SingleOrDefault();
			return dialog is { ActualWidth: > 0 };
		});
		return dialog!;
	}

	private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
	{
		yield return parent;
		for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(parent, index))) yield return child;
	}

	private static void Invoke(ContentDialog dialog, string caption)
	{
		Button button = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, caption));
		var peer = new ButtonAutomationPeer(button);
		((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
	}

	private static async Task WaitAsync(Func<bool> ready)
	{
		for (int attempt = 0; attempt < 100; attempt++)
		{
			if (ready()) return;
			await Task.Delay(20);
		}

		throw new Exception("Saved-tab dialog did not reach its expected state.");
	}

	private static async Task CaptureAsync(UIElement view, string name)
	{
		Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap bitmap = new();
		await bitmap.RenderAsync(view);
		byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
		if (pixels.Length == 0) throw new Exception("Saved-tab dialog capture is empty.");
		using FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, name));
		var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(global::Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
		encoder.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
			(uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
		await encoder.FlushAsync();
	}
}
