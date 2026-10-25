using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

/// <summary>Page navigation with a single guarded exit policy for pending edits.</summary>
internal static class ScreenNavigation
{
	public static Task PushAsync(Page parent, Page child, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		NavigationPage.SetHasNavigationBar(child, false);
		NavigationPage.SetHasBackButton(child, false);
		return parent.Navigation.PushAsync(child);
	}

	public static Task LeaveEditorAsync(Page page, bool changed, Func<Task> saveAndLeave, Func<Task> leave, CancellationToken cancellationToken = default)
		=> EditorExit.RequestAsync(
			changed,
			async () =>
			{
				string? action = await page.DisplayActionSheetAsync(
					"Unsaved changes", "Keep Editing", null, "Save Changes", "Discard Changes").ConfigureAwait(true);
				return action switch
				{
					"Save Changes" => EditorExitChoice.SaveChanges,
					"Discard Changes" => EditorExitChoice.DiscardChanges,
					_ => EditorExitChoice.KeepEditing,
				};
			},
			saveAndLeave,
			leave,
			cancellationToken);
}
