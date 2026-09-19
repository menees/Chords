# Exercises the production authentication adapter without signing in or contacting a cloud account.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookAuthSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OneDriveAuthenticationSmoke.cs') -Destination (Join-Path $fixture 'Program.cs')
Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $fixture 'global.json')
$source = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Maui/Platforms/Windows/WindowsOneDriveTokenProvider.cs'))
$contract = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Sync.OneDrive/IOneDriveTokenProvider.cs'))
[xml]$packages = Get-Content -LiteralPath (Join-Path $repo 'Directory.Packages.props')
$msal = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Identity.Client').Version
$cache = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Identity.Client.Extensions.Msal').Version
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.Identity.Client" Version="$msal" /><PackageReference Include="Microsoft.Identity.Client.Extensions.Msal" Version="$cache" /><Compile Include="$source" /><Compile Include="$contract" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixture 'Smoke.csproj'), $project)
dotnet run --project (Join-Path $fixture 'Smoke.csproj') -- $fixture
if ($LASTEXITCODE -ne 0) { throw 'OneDrive authentication smoke failed.' }
"Disposable fixture: $fixture"
