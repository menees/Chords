using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Microsoft.Maui;
using static SetlistSmoke.SmokeMauiApp;

namespace SetlistSmoke;

internal static class NativeMetadataImportSmoke
{
	public static async Task RunAsync(MainPage owner)
	{
		Program.Trace("Verify metadata import tag syntax, matches and unchanged rows");
		const string Source = "{title: Sound Of Heaven}\r\n{key: Ab}\r\n[C]Synthetic body";
		SongEditMetadata metadata = new("Sound Of Heaven", [], ["Christian"])
		{
			Scalars = new Dictionary<string, IReadOnlyList<string>> { ["key"] = ["Ab"] },
		};
		MetadataImportPage review = await OpenAsync(owner, Source, metadata);
		Check(Elements(review).OfType<CheckBox>().Count() == 1, "Unchanged title/key offered as imports");
		Check(!Elements(review).OfType<Picker>().Any(), "An insertion offered a target selector");
		Check(Diff(review).Contains("+ {tag: Christian}") && !Diff(review).Contains("meta: tag"), "Tag insertion uses wrong syntax");
		Click(review, "Apply to Buffer");
		string? changed = await review.Completion;
		Check(changed == Source.Replace("[C]", "{tag: Christian}\r\n[C]"), "Apply did not produce the reviewed tag insertion");

		review = await OpenAsync(owner, "{tag: Old}\n[C]Synthetic body", new(string.Empty, [], ["Christian"]));
		Check(!Elements(review).OfType<Picker>().Any(), "Single match offered a redundant picker");
		Check(Elements(review).OfType<Label>().Any(label => label.Text == "Line 1: {tag: Old}"), "Single match line reference missing");
		Check(Apply(review).IsEnabled, "Unambiguous match not selected automatically");
		CheckBox selected = Elements(review).OfType<CheckBox>().Single();
		selected.IsChecked = false;
		Check(!Apply(review).IsEnabled, "Unchecked match can still apply");
		selected.IsChecked = true;
		Check(Apply(review).IsEnabled && Diff(review).Contains("+ Christian"), "Checked match did not restore exact preview");
		await CaptureAsync(review, "metadata-import-single-match.png");
		Click(review, "Back");
		Check(await review.Completion is null, "Back applied buffer edits");

		review = await OpenAsync(owner, "{tag: Old}\n{tag: Other}\n[C]Synthetic body", new(string.Empty, [], ["Christian"]));
		Picker target = Elements(review).OfType<Picker>().Single();
		Check(target.Items.Count == 2 && target.SelectedIndex == -1 && !Apply(review).IsEnabled, "Ambiguous target was selected implicitly");
		target.SelectedIndex = 1;
		Check(Apply(review).IsEnabled, "Explicit duplicate choice did not enable import");
		Click(review, "Apply to Buffer");
		Check(await review.Completion == "{tag: Old}\n{tag: Christian}\n[C]Synthetic body", "Duplicate choice changed another line");

		review = await OpenAsync(owner, changed!, metadata);
		Check(!Elements(review).OfType<CheckBox>().Any() && !Elements(review).OfType<Picker>().Any(), "Unchanged metadata is selectable");
		Check(!Apply(review).IsEnabled && Elements(review).OfType<Label>().Any(label => label.Text == "No metadata changes to import."),
			"All-matched state is not clear or allows Apply");
		await CaptureAsync(review, "metadata-import-no-changes.png");
		Click(review, "Back");
		Check(await review.Completion is null, "Unchanged review returned edits");
		Program.Trace("Native metadata insertion/single match/duplicates/no-op/checkbox/Back checks passed");
	}

	private static async Task<MetadataImportPage> OpenAsync(MainPage owner, string source, SongEditMetadata metadata)
	{
		MetadataImportPage page = new(new MetadataDirectiveImport(source, metadata));
		NavigationPage.SetHasNavigationBar(page, false);
		await owner.Navigation.PushAsync(page, animated: false);
		await WaitAsync(() => page.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement { IsLoaded: true });
		return page;
	}
	private static IEnumerable<IVisualTreeElement> Elements(IVisualTreeElement root)
	{
		yield return root;
		foreach (var element in root.GetVisualChildren())
			foreach (var descendant in Elements(element)) yield return descendant;
	}
	private static FluentIconButton Apply(MetadataImportPage page) => Elements(page).OfType<FluentIconButton>().Single(button => button.Description == "Apply to Buffer");
	private static string Diff(MetadataImportPage page) => Elements(page).OfType<Label>().Single(label => label.FontFamily == "Consolas").Text;
	private static void Click(MetadataImportPage page, string description)
		=> ((IButtonController)Elements(page).OfType<FluentIconButton>().Single(button => button.Description == description)).SendClicked();
}
