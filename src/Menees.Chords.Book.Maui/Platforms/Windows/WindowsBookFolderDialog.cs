#region Using Directives

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>The Windows common dialog supports a title, unlike the older WinRT folder picker.</summary>
internal static partial class WindowsBookFolderDialog
{
	#region Private Data

	private const int Canceled = unchecked((int)0x8007_04C7);
	private const uint PickFolders = 0x20;
	private const uint ForceFileSystem = 0x40;
	private const uint PathMustExist = 0x800;
	private const uint FileSystemPath = 0x8005_8000;

	private static readonly StrategyBasedComWrappers Wrappers = new();

	#endregion

	#region Internal Interfaces

	// COM declarations retain native vtable order, including unused methods before GetResult/GetDisplayName.
	[GeneratedComInterface]
	[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
	internal partial interface IFileDialog
	{
		[PreserveSig]
		int Show(IntPtr owner);

		void SetFileTypes(uint count, IntPtr filters);

		void SetFileTypeIndex(uint index);

		void GetFileTypeIndex(out uint index);

		void Advise(IntPtr events, out uint cookie);

		void Unadvise(uint cookie);

		void SetOptions(uint options);

		void GetOptions(out uint options);

		void SetDefaultFolder(IShellItem folder);

		void SetFolder(IShellItem folder);

		void GetFolder(out IShellItem folder);

		void GetCurrentSelection(out IShellItem item);

		void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

		void GetFileName(out IntPtr name);

		void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

		void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

		void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

		void GetResult(out IntPtr item);
	}

	[GeneratedComInterface]
	[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
	internal partial interface IShellItem
	{
		void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);

		void GetParent(out IShellItem parent);

		void GetDisplayName(uint kind, out IntPtr name);
	}

	#endregion

	#region Public Methods

	public static string? Show(IntPtr owner, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Guid classId = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
		Guid interfaceId = typeof(IFileDialog).GUID;
		Marshal.ThrowExceptionForHR(CoCreateInstance(in classId, IntPtr.Zero, 1, in interfaceId, out IntPtr pointer));
		IFileDialog dialog = Wrap<IFileDialog>(pointer);
		string? result = null;
		try
		{
			dialog.GetOptions(out uint options);
			dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
			dialog.SetTitle("Select Book Folder");
			dialog.SetOkButtonLabel("Open Book");
			int status = dialog.Show(owner);
			cancellationToken.ThrowIfCancellationRequested();
			if (status != Canceled)
			{
				Marshal.ThrowExceptionForHR(status);
				dialog.GetResult(out IntPtr itemPointer);
				IShellItem item = Wrap<IShellItem>(itemPointer);
				try
				{
					item.GetDisplayName(FileSystemPath, out IntPtr path);
					try
					{
						result = Marshal.PtrToStringUni(path);
					}
					finally
					{
						Marshal.FreeCoTaskMem(path);
					}
				}
				finally
				{
					((ComObject)(object)item).FinalRelease();
				}
			}
		}
		finally
		{
			((ComObject)(object)dialog).FinalRelease();
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static T Wrap<T>(IntPtr pointer)
	{
		try
		{
			return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
		}
		finally
		{
			Marshal.Release(pointer);
		}
	}

	[LibraryImport("ole32.dll")]
	private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

	#endregion
}
