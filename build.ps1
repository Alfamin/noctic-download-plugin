# Builds the plugin and writes FreeMusicFinder/bin/Release/FreeMusicFinder-<version>.zip.
# Needs the .NET 10 SDK and git. The Noctis repo is cloned into extern/ for the plugin SDK
# (Noctis.Plugins.Abstractions is not on NuGet yet); pass -NoctisRepo to use your own checkout.
param(
    [string]$NoctisRepo = (Join-Path $PSScriptRoot 'extern\Noctis')
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $NoctisRepo 'src\Noctis.Plugins.Abstractions'))) {
    git clone --depth 1 https://github.com/heartached/Noctis.git $NoctisRepo
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone the Noctis repo.' }
}

dotnet build (Join-Path $PSScriptRoot 'FreeMusicFinder\FreeMusicFinder.csproj') -c Release "-p:NoctisRepo=$NoctisRepo"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
