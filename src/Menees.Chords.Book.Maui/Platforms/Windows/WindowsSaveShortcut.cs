using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using KeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Routes Ctrl+S from native fields on an editor page to its Save action.</summary>
internal sealed partial class WindowsSaveShortcut : IDisposable
{
	private readonly VisualElement owner;
	private readonly Action save;
	private readonly KeyboardAccelerator accelerator = new() { Key = VirtualKey.S, Modifiers = VirtualKeyModifiers.Control };
	private FrameworkElement? element;

	public WindowsSaveShortcut(VisualElement owner, Action save)
	{
		this.owner = owner;
		this.save = save;
		this.accelerator.Invoked += this.HandleInvoked;
		this.owner.HandlerChanged += this.HandleHandlerChanged;
		this.Attach();
	}

	public void Dispose()
	{
		this.owner.HandlerChanged -= this.HandleHandlerChanged;
		this.accelerator.Invoked -= this.HandleInvoked;
		this.element?.KeyboardAccelerators.Remove(this.accelerator);
		this.element = null;
	}

	private void HandleHandlerChanged(object? sender, EventArgs args) => this.Attach();

	private void Attach()
	{
		this.element?.KeyboardAccelerators.Remove(this.accelerator);
		this.element = this.owner.Handler?.PlatformView as FrameworkElement;
		this.accelerator.ScopeOwner = this.element;
		if (this.element is not null)
		{
			// The page owns the shortcut scope, not a page-wide Ctrl+S hover tooltip.
			this.element.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
			this.element.KeyboardAccelerators.Add(this.accelerator);
		}
	}

	private void HandleInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		this.save();
	}
}
