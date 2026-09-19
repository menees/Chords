using Menees.Chords.Book.Application;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Grid = Microsoft.UI.Xaml.Controls.Grid;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class TransposePickerHandler : ViewHandler<TransposePicker, ContentControl>
{
	private const int ButtonHeight = 32;
	private const int ChoiceWidth = 108;
	private const int ColumnGap = 6;
	private const int ButtonGap = 4;
	private const int MaximumOffset = 11;
	private const int ColumnCount = 3;
	private const int FlyoutChoiceWidth = 90;
	private const int MiddleRow = 0;

	private static readonly IPropertyMapper<TransposePicker, TransposePickerHandler> TransposeMapper
		= new PropertyMapper<TransposePicker, TransposePickerHandler>(ViewMapper)
		{
			[nameof(TransposePicker.OriginalKey)] = (handler, _) => handler.Update(),
			[nameof(TransposePicker.Offset)] = (handler, _) => handler.Update(),
			[nameof(TransposePicker.IsEnabled)] = (handler, _) => handler.Update(),
		};

	private readonly DropDownButton choose = new()
	{
		MinHeight = ButtonHeight, MinWidth = ChoiceWidth, Padding = new(ColumnGap, 0, ColumnGap, 0),
	};

	private readonly Button down = new() { Content = "−", Width = ButtonHeight, Height = ButtonHeight, Padding = new(0) };

	private readonly Button up = new() { Content = "+", Width = ButtonHeight, Height = ButtonHeight, Padding = new(0) };

	public TransposePickerHandler()
		: base(TransposeMapper)
	{
	}

	protected override ContentControl CreatePlatformView()
	{
		StackPanel panel = new() { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = ButtonGap };
		panel.Children.Add(this.choose);
		panel.Children.Add(this.down);
		panel.Children.Add(this.up);
		this.down.Click += (_, _) => this.VirtualView.Offset--;
		this.up.Click += (_, _) => this.VirtualView.Offset++;
		ToolTipService.SetToolTip(this.choose, "Transpose relative to the current song/entry settings");
		ToolTipService.SetToolTip(this.down, "Down one semitone");
		ToolTipService.SetToolTip(this.up, "Up one semitone");
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this.choose, "Transpose");
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this.down, "Down one semitone");
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this.up, "Up one semitone");
		return new() { Content = panel, HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
	}

	private void Update()
	{
		this.choose.Content = TransposeChoice.GetLabel(this.VirtualView.OriginalKey, this.VirtualView.Offset);
		this.choose.IsEnabled = this.VirtualView.IsEnabled;
		this.down.IsEnabled = this.VirtualView.IsEnabled && this.VirtualView.Offset > -MaximumOffset;
		this.up.IsEnabled = this.VirtualView.IsEnabled && this.VirtualView.Offset < MaximumOffset;
		Grid grid = new() { ColumnSpacing = ColumnGap, RowSpacing = 2 };
		for (int column = 0; column < ColumnCount; column++)
		{
			grid.ColumnDefinitions.Add(new() { Width = Microsoft.UI.Xaml.GridLength.Auto });
		}

		for (int row = 0; row < MaximumOffset; row++)
		{
			grid.RowDefinitions.Add(new() { Height = Microsoft.UI.Xaml.GridLength.Auto });
		}

		Flyout flyout = new() { Content = grid };
		for (int offset = -MaximumOffset; offset <= MaximumOffset; offset++)
		{
			int selected = offset;
			Button button = new()
			{
				Content = TransposeChoice.GetLabel(this.VirtualView.OriginalKey, offset), MinWidth = FlyoutChoiceWidth, Height = ButtonHeight,
			};
			button.Click += (_, _) =>
			{
				flyout.Hide();
				this.VirtualView.Offset = selected;
			};
			Grid.SetColumn(button, offset < 0 ? 0 : offset == 0 ? 1 : 2);
			Grid.SetRow(button, offset == 0 ? MiddleRow : Math.Abs(offset) - 1);
			grid.Children.Add(button);
		}

		this.choose.Flyout = flyout;
	}
}
