namespace Menees.Chords.Web;

using Microsoft.AspNetCore.Components;

public partial class Icon
{
	[Parameter]
	[EditorRequired]
	public IconName Name { get; set; }

	[Parameter]
	public byte Size { get; set; } = 20;
}