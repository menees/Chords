param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory)
{
    $OutputDirectory = Join-Path $repoDirectory ('artifacts\ChordBook-' + $RuntimeIdentifier + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath)
{
    throw 'Choose a new output directory so an older build cannot leave stale files in this distribution.'
}

Push-Location $repoDirectory
try
{
    $frameworkOutput = & dotnet msbuild src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj -getProperty:TargetFramework
    if ($LASTEXITCODE -ne 0 -or !$frameworkOutput) { throw 'Could not resolve the Windows target framework.' }
    $framework = ($frameworkOutput -join [Environment]::NewLine).Trim()
    dotnet publish src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj -c Release -f $framework `
        -r $RuntimeIdentifier --self-contained true -p:IsPublishable=true -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'ChordBook publish failed. Do not distribute the incomplete output.' }
    foreach ($required in @('Menees.Chords.Book.Maui.exe', 'Editor\editor.html', 'Viewer\viewer.html', 'Viewer\pdfjs\build\pdf.worker.mjs'))
    {
        if (!(Test-Path -LiteralPath (Join-Path $outputPath $required))) { throw "Published asset is missing: $required" }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ONEDRIVE.md') -Destination (Join-Path $outputPath 'ONEDRIVE.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WINDOWS_DEPLOYMENT.md') -Destination (Join-Path $outputPath 'WINDOWS_DEPLOYMENT.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RECOVERY.md') -Destination (Join-Path $outputPath 'RECOVERY.md')
    & (Join-Path $PSScriptRoot 'Copy-ChordBookAcceptance.ps1') -SourceDirectory $outputPath
    Write-Output "Published ChordBook to $outputPath and replaced Maui.Acceptance. WebView2 must be available on the destination PC."
}
finally
{
    Pop-Location
}
