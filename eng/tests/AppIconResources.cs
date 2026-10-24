using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

// Read-only Win32 probe, compiled by Test-AppIcons.ps1 rather than by the app.
public static class AppIconResources
{
    public static string Check(string executable, string iconPath)
    {
        int[] sizes = { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256 };
        byte[] ico = File.ReadAllBytes(iconPath);
        Require(BitConverter.ToUInt16(ico, 0) == 0 && BitConverter.ToUInt16(ico, 2) == 1
            && BitConverter.ToUInt16(ico, 4) == sizes.Length, "Invalid ICO directory.");
        IntPtr module = LoadLibraryEx(executable, IntPtr.Zero, 2); // LOAD_LIBRARY_AS_DATAFILE: never execute the app.
        Require(module != IntPtr.Zero, "Cannot read executable resources.");
        try
        {
            List<IntPtr> names = new List<IntPtr>();
            EnumResourceName callback = (m, t, name, p) => { names.Add(name); return true; };
            Require(EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero), "Executable has no icon group.");
            byte[] group = null;
            foreach (IntPtr name in names)
            {
                byte[] candidate = ReadResource(module, 14, name);
                if (BitConverter.ToUInt16(candidate, 4) == sizes.Length) { group = candidate; break; }
            }
            Require(group != null, "Executable does not contain the 15-size ChordBook icon group.");
            for (int index = 0; index < sizes.Length; index++)
            {
                int size = sizes[index], entry = 6 + index * 16, groupEntry = 6 + index * 14;
                Require((ico[entry] == 0 ? 256 : ico[entry]) == size && ico[entry] == ico[entry + 1], "Wrong ICO dimensions.");
                Require(group[groupEntry] == ico[entry] && group[groupEntry + 1] == ico[entry + 1], "Wrong embedded dimensions.");
                int count = BitConverter.ToInt32(ico, entry + 8), offset = BitConverter.ToInt32(ico, entry + 12);
                byte[] embedded = ReadResource(module, 3, new IntPtr(BitConverter.ToUInt16(group, groupEntry + 12)));
                Require(embedded.Length == count, "Embedded icon length differs.");
                for (int pixelByte = 0; pixelByte < count; pixelByte++)
                    Require(embedded[pixelByte] == ico[offset + pixelByte], "Embedded icon pixels differ from window ICO.");
                IntPtr icon = LoadImage(IntPtr.Zero, iconPath, 1, size, size, 16);
                Require(icon != IntPtr.Zero, "Windows cannot load the " + size + "px icon.");
                try
                {
                    IconInfo info;
                    Require(GetIconInfo(icon, out info), "Cannot inspect loaded icon.");
                    try
                    {
                        BitmapInfo bitmap;
                        Require(GetObject(info.Color, Marshal.SizeOf(typeof(BitmapInfo)), out bitmap) != 0
                            && bitmap.Width == size && bitmap.Height == size, "Windows loaded the wrong icon size.");
                    }
                    finally
                    {
                        if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
                        if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
                    }
                }
                finally { DestroyIcon(icon); }
            }
        }
        finally { FreeLibrary(module); }
        return "PASS: 15 embedded icon sizes match the deployed ICO; Windows loads every requested size.";
    }

    private static byte[] ReadResource(IntPtr module, int type, IntPtr name)
    {
        IntPtr resource = FindResource(module, name, new IntPtr(type));
        Require(resource != IntPtr.Zero, "Missing icon resource.");
        uint length = SizeofResource(module, resource);
        IntPtr data = LockResource(LoadResource(module, resource));
        Require(data != IntPtr.Zero && length > 0, "Unreadable icon resource.");
        byte[] result = new byte[length];
        Marshal.Copy(data, result, 0, result.Length);
        return result;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint XHotspot, YHotspot;
        public IntPtr Mask, Color;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr bitmap, int size, out BitmapInfo info);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
}
