#requires -Version 7.4
#requires -PSEdition Core
[CmdletBinding()]
param([switch] $Check)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (![OperatingSystem]::IsWindows()) { throw 'Icon export currently requires Windows and PowerShell 7.4 or later.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'src\Menees.Chords.Book.Maui'
$assets = Join-Path $project 'Resources\AppIcon'
$output = Join-Path $assets 'Generated'
$sizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256)

# Use the renderer version selected by the existing MAUI project, not another tool/package.
$restorePath = Join-Path $project 'obj\project.assets.json'
if (!(Test-Path -LiteralPath $restorePath))
{
    throw 'Restore the Windows MAUI project with dotnet restore before exporting icons.'
}
$restore = Get-Content -Raw -LiteralPath $restorePath | ConvertFrom-Json
$rendererPackage = @($restore.libraries.PSObject.Properties | Where-Object { $_.Name.StartsWith('Microsoft.Maui.Resizetizer/') })
if ($rendererPackage.Count -ne 1) { throw 'The restored project must select one Microsoft.Maui.Resizetizer package.' }
$renderer = $null
foreach ($packageFolder in $restore.packageFolders.PSObject.Properties.Name)
{
    $candidate = Join-Path $packageFolder ($rendererPackage[0].Value.path + '/buildTransitive')
    if (Test-Path -LiteralPath (Join-Path $candidate 'Svg.Skia.dll')) { $renderer = $candidate; break }
}
if (!$renderer) { throw 'MAUI SVG renderer is missing. Restore the Windows MAUI project.' }
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$nativeLibrary = Join-Path $renderer "$architecture\libSkiaSharp.dll"
if (!(Test-Path -LiteralPath $nativeLibrary)) { throw "No MAUI SVG renderer for this PowerShell process architecture: $architecture" }
# Keep the native library loaded for this process; freeing it while managed Skia objects exist is unsafe.
[Runtime.InteropServices.NativeLibrary]::Load($nativeLibrary) | Out-Null
foreach ($assembly in @('SkiaSharp', 'ExCSS', 'Fizzler', 'Svg.Custom', 'ShimSkiaSharp', 'Svg.Model', 'Svg.Skia'))
{
    Add-Type -Path (Join-Path $renderer ($assembly + '.dll'))
}

function Get-IconTier([int] $Size)
{
    if ($Size -ge 48) { return 'full' }
    if ($Size -ge 30) { return 'standard' }
    return 'micro'
}

function Render-Icon([Svg.Skia.SKSvg] $Svg, [int] $Size)
{
    $surface = [SkiaSharp.SKSurface]::Create([SkiaSharp.SKImageInfo]::new(
        $Size, $Size, [SkiaSharp.SKColorType]::Rgba8888, [SkiaSharp.SKAlphaType]::Premul))
    if ($null -eq $surface) { throw "Cannot create $Size pixel icon surface." }
    try
    {
        $canvas = $surface.Canvas
        $canvas.Clear([SkiaSharp.SKColors]::Transparent)
        $bounds = $Svg.Picture.CullRect
        $canvas.Scale([single]($Size / $bounds.Width), [single]($Size / $bounds.Height))
        $canvas.Translate(-$bounds.Left, -$bounds.Top)
        $canvas.DrawPicture($Svg.Picture)
        $image = $surface.Snapshot()
        try
        {
            $data = $image.Encode([SkiaSharp.SKEncodedImageFormat]::Png, 100)
            if ($null -eq $data) { throw 'Cannot encode icon PNG.' }
            try { return ,$data.ToArray() }
            finally { $data.Dispose() }
        }
        finally { $image.Dispose() }
    }
    finally { $surface.Dispose() }
}

function Save-Icon([string] $Name, [byte[]] $Bytes)
{
    $destination = Join-Path $output $Name
    if (Test-Path -LiteralPath $destination)
    {
        $previous = [IO.File]::ReadAllBytes($destination)
        if ([Linq.Enumerable]::SequenceEqual[byte]($previous, $Bytes)) { return $false }
    }
    if ($Check) { throw "Missing or stale icon: $destination. Run eng/Export-ChordBookIcons.ps1." }
    [IO.File]::WriteAllBytes($destination, $Bytes)
    return $true
}

function New-MultiSizeIcon($Images)
{
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    try
    {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        foreach ($size in $sizes)
        {
            # Zero denotes a 256-pixel dimension in ICONDIRENTRY.
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$Images[$size].Length)
            $writer.Write([uint32]$offset)
            $offset += $Images[$size].Length
        }
        foreach ($size in $sizes) { $writer.Write([byte[]]$Images[$size]) }
        $writer.Flush()
        return ,$stream.ToArray()
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

function New-IconPreview($Images, $Sources)
{
    $surface = [SkiaSharp.SKSurface]::Create([SkiaSharp.SKImageInfo]::new(1100, 680))
    $paint = [SkiaSharp.SKPaint]::new()
    $typeface = [SkiaSharp.SKTypeface]::FromFamilyName('Segoe UI')
    try
    {
        $canvas = $surface.Canvas
        $canvas.Clear([SkiaSharp.SKColor]::Parse('#edf3f7'))
        $paint.Color = [SkiaSharp.SKColor]::Parse('#202b32')
        $canvas.DrawRect([SkiaSharp.SKRect]::new(0, 535, 1100, 680), $paint)
        $paint.Color = [SkiaSharp.SKColor]::Parse('#061117')
        $paint.Typeface = $typeface
        $paint.TextSize = 18
        $paint.IsAntialias = $true
        foreach ($sample in @(@(256, 24, 256), @(32, 430, 224), @(16, 830, 128)))
        {
            $size, $x, $displaySize = $sample
            $tier = Get-IconTier $size
            $canvas.DrawText("$tier ($($size)px)", [single]$x, [single]35, $paint)
            $bytes = Render-Icon $Sources[$tier] $displaySize
            $image = [SkiaSharp.SKImage]::FromEncodedData([byte[]]$bytes)
            try { $canvas.DrawImage($image, [single]$x, [single]55) }
            finally { $image.Dispose() }
        }
        $canvas.DrawText('Native pixel sizes on light and dark backgrounds', [single]24, [single]350, $paint)
        $x = 24
        foreach ($size in $sizes | Where-Object { $_ -le 96 })
        {
            $canvas.DrawText([string]$size, [single]$x, [single]380, $paint)
            $image = [SkiaSharp.SKImage]::FromEncodedData([byte[]]$Images[$size])
            try
            {
                $canvas.DrawImage($image, [single]$x, [single]400)
                $canvas.DrawImage($image, [single]$x, [single]555)
            }
            finally { $image.Dispose() }
            $x += $size + 24
        }
        $previewDirectory = Join-Path $repo 'artifacts\branding'
        [IO.Directory]::CreateDirectory($previewDirectory) | Out-Null
        $snapshot = $surface.Snapshot()
        try
        {
            $data = $snapshot.Encode([SkiaSharp.SKEncodedImageFormat]::Png, 100)
            try { [IO.File]::WriteAllBytes((Join-Path $previewDirectory 'icons.png'), $data.ToArray()) }
            finally { $data.Dispose() }
        }
        finally { $snapshot.Dispose() }
    }
    finally { $typeface.Dispose(); $paint.Dispose(); $surface.Dispose() }
}

$sources = @{}
$images = @{}
$changed = 0
try
{
    foreach ($tier in @('full', 'standard', 'micro'))
    {
        $svg = [Svg.Skia.SKSvg]::new()
        $sources[$tier] = $svg
        $picture = $svg.Load((Join-Path $assets "Source\chordbook-$tier.svg"))
        if ($null -eq $picture -or $picture.CullRect.IsEmpty) { throw "Cannot load $tier SVG." }
    }
    # Complete rendering before writing any output; a bad source cannot leave a partial set.
    foreach ($size in $sizes) { $images[$size] = Render-Icon $sources[(Get-IconTier $size)] $size }
    $ico = New-MultiSizeIcon $images
    if (!$Check) { [IO.Directory]::CreateDirectory($output) | Out-Null }
    foreach ($size in $sizes) { if (Save-Icon "chordbook-$size.png" $images[$size]) { $changed++ } }
    if (Save-Icon 'chordbook.ico' $ico) { $changed++ }
    if (!$Check) { New-IconPreview $images $sources }
    $verb = if ($Check) { 'Verified' } else { 'Exported' }
    Write-Output "$verb $($sizes.Count) PNGs and one multi-resolution ICO; $changed files changed. Renderer: $($rendererPackage[0].Name)."
}
finally { foreach ($svg in $sources.Values) { $svg.Dispose() } }
