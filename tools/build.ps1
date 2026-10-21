[CmdletBinding()]
param([switch]$Launch, [switch]$SelfContained, [string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'fetch-geoip.ps1')
$output = Join-Path $repo 'artifacts/app'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$arguments = @('publish', (Join-Path $repo 'src/EShield.App/EShield.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-o', $output, "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'), (Join-Path $repo 'Readme.md'), (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $repo 'CONTRIBUTING.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $output -Recurse -Force
Write-Host "Published to $output"
if ($Launch) {
    Start-Process -FilePath (Join-Path $output 'EShield.exe') -ArgumentList '--replace' -WorkingDirectory $output -Verb RunAs -WindowStyle Normal
}
