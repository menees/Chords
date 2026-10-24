param([Parameter(Mandatory=$true)][string] $PublishedDirectory)
# Run in a separate PowerShell process: this read-only probe sets that process thread's UI preference.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NativeMuiProbe
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool SetThreadPreferredUILanguages(uint flags, string languages, out uint count);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool GetFileMUIPath(uint flags, string source, StringBuilder language, ref uint languageSize,
        StringBuilder path, ref uint pathSize, ref ulong enumerator);
    public static string Find(string source, string preferred)
    {
        uint count;
        if (!SetThreadPreferredUILanguages(8, preferred + "\0\0", out count)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        uint languageSize = 85;
        uint pathSize = 1024;
        ulong enumerator = 0;
        var language = new StringBuilder((int)languageSize);
        var path = new StringBuilder((int)pathSize);
        if (!GetFileMUIPath(8, source, language, ref languageSize, path, ref pathSize, ref enumerator))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }
}
"@
foreach ($preference in @('en-US','fr-FR'))
{
    foreach ($name in @('Microsoft.ui.xaml.dll', 'Microsoft.UI.Xaml.Phone.dll'))
    {
        $path = [NativeMuiProbe]::Find((Join-Path $published $name), $preference)
        if (!(Test-Path -LiteralPath $path) -or [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($path)) -notin @('en-US','en-GB'))
        {
            throw "Unexpected MUI fallback: $path"
        }
        Write-Output "PASS: $preference preference resolves $name to $path"
    }
}
