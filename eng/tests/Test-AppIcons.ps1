param(
    [Parameter(Mandatory)]
    [string] $Executable
)

# Read-only resource checks. Does not launch ChordBook, open books, or modify shell caches.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$iconPath = Join-Path ([IO.Path]::GetDirectoryName($executablePath)) 'Assets\ChordBook.ico'
$sourcePath = Join-Path $repo 'src\Menees.Chords.Book.Maui\Resources\AppIcon\Generated\chordbook.ico'
if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $iconPath).Hash)
{
    throw 'The window icon differs from the checked-in executable icon.'
}
Add-Type -Path (Join-Path $PSScriptRoot 'AppIconResources.cs')
[AppIconResources]::Check($executablePath, $iconPath)
