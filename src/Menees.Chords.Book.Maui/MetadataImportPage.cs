#region Using Directives

using System.Text;
using Menees.Chords.Book.Application;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Reviews selected metadata edits before applying one undoable buffer change.</summary>
public sealed partial class MetadataImportPage : ContentPage
{
	#region Private Data

	private readonly MetadataDirectiveImport import;
	private readonly List<(MetadataDirectiveProposal Proposal, CheckBox Selected, Picker Target)> rows = [];
	private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Label diff = new() { FontFamily = "Consolas" };
	private readonly Label status = new();
	private readonly FluentIconButton apply = new() { Icon = "ArrowImport", Description = "Apply to Buffer" };

	#endregion

	#region Public API

	public MetadataImportPage(MetadataDirectiveImport import)
	{
		this.import = import;
		this.Title = "Import Metadata as ChordPro Directives";
		const int Spacing = 8;
		const int DiffRow = 3;
		const int ActionRow = 4;
		const int PaddingSize = 16;
		Grid layout = new()
		{
			Padding = PaddingSize, RowSpacing = Spacing,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		layout.Add(new Label
		{
			Text = "Choose metadata to add or update. Review the exact text changes below. Only the editor buffer changes; Save writes the song. "
				+ "For duplicates, choose a line explicitly. Other occurrences remain untouched. Conditional/structured directives must be edited manually.",
		});
		VerticalStackLayout fields = new() { Spacing = Spacing };
		foreach (MetadataDirectiveProposal proposal in import.Proposals)
		{
			CheckBox selected = new()
			{
				IsChecked = proposal.Occurrences.Count == 0 || (proposal.Occurrences.Count == 1 && proposal.Occurrences[0].CanReplace),
			};
			SemanticProperties.SetDescription(selected, "Import " + proposal.Name);
			Picker target = new() { Title = "Choose a directive to update" };
			foreach (MetadataDirectiveOccurrence occurrence in proposal.Occurrences)
			{
				target.Items.Add($"Line {occurrence.LineNumber}: {occurrence.SourceLine}");
			}

			target.IsVisible = proposal.Occurrences.Count > 0;
			target.SelectedIndex = proposal.Occurrences.Count == 1 ? 0 : -1;
			Grid row = new() { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = Spacing };
			row.Add(selected);
			row.Add(new Label { Text = proposal.Name + ": " + string.Join("; ", proposal.Values), VerticalOptions = LayoutOptions.Center }, 1);
			row.Add(target, 2);
			fields.Children.Add(row);
			this.rows.Add((proposal, selected, target));
			selected.CheckedChanged += (_, _) => this.UpdatePreview();
			target.SelectedIndexChanged += (_, _) =>
			{
				selected.IsChecked = target.SelectedIndex >= 0;
				this.UpdatePreview();
			};
		}

		layout.Add(new ScrollView { Content = fields }, 0, 1);
		layout.Add(new Label { Text = "Text changes (− before, + after)" }, 0, 2);
		layout.Add(new ScrollView { Content = this.diff }, 0, DiffRow);
		FluentIconButton cancel = new() { Icon = "Dismiss", Description = "Cancel" };
		cancel.Clicked += async (_, _) => await this.CloseAsync(null).ConfigureAwait(true);
		this.apply.Clicked += this.HandleApply;
		VerticalStackLayout actions = new()
		{
			Children = { this.status },
		};
		layout.Add(actions, 0, ActionRow);
		this.Content = DialogLayout.Create(this.Title, layout, this.apply, cancel);
		this.UpdatePreview();
	}

	public Task<string?> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.CloseFromBack();
		return true;
	}

	#endregion

	#region Private Methods

	private List<MetadataDirectiveSelection> GetSelections() => [.. this.rows.Where(row => row.Selected.IsChecked)
		.Select(row => new MetadataDirectiveSelection(row.Proposal.Name, row.Target.SelectedIndex < 0 ? null : row.Target.SelectedIndex))];

	private void UpdatePreview()
	{
		try
		{
			IReadOnlyList<EditorTextChange> changes = this.import.Preview(this.GetSelections());
			StringBuilder text = new();
			foreach (EditorTextChange change in changes)
			{
				text.AppendLine($"Line {change.LineNumber}");
				text.AppendLine("− " + change.Before);
				text.AppendLine("+ " + change.After);
				text.AppendLine();
			}

			this.diff.Text = text.ToString();
			this.status.Text = changes.Count == 0 ? "No text changes selected." : string.Empty;
			this.apply.IsEnabled = changes.Count > 0;
		}
		catch (InvalidOperationException exception)
		{
			this.status.Text = exception.Message;
			this.diff.Text = string.Empty;
			this.apply.IsEnabled = false;
		}
	}

	private async void HandleApply(object? sender, EventArgs e)
		=> await this.CloseAsync(this.import.Apply(this.GetSelections())).ConfigureAwait(true);

	private async void CloseFromBack() => await this.CloseAsync(null).ConfigureAwait(true);

	private async Task CloseAsync(string? result)
	{
		if (!this.completion.Task.IsCompleted)
		{
			this.IsEnabled = false;
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(result);
		}
	}

	#endregion
}
