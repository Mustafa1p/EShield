[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$data = Join-Path $repo 'src/EShield.App/Data'
$database = Join-Path $data 'dbip-city-lite.mmdb'
New-Item -ItemType Directory -Force -Path $data | Out-Null
if ((Test-Path -LiteralPath $database) -and (Get-Item -LiteralPath $database).Length -gt 10000000 -and -not $Force) {
    Write-Host 'Using the existing local DB-IP Lite database.'
    exit 0
}
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(10)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('EShield-Build/0.1')
$temporary = "$database.download"
try {
    $downloaded = $false
    for ($offset = 0; $offset -lt 3; $offset++) {
        $month = [DateTime]::UtcNow.AddMonths(-$offset).ToString('yyyy-MM')
        $url = "https://download.db-ip.com/free/dbip-city-lite-$month.mmdb.gz"
        Write-Host "Downloading DB-IP Lite $month (local country, region and city data)..."
        $response = $client.GetAsync($url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if ($response.StatusCode -eq [Net.HttpStatusCode]::NotFound) { $response.Dispose(); continue }
        $response.EnsureSuccessStatusCode() | Out-Null
        $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
        $output = [IO.File]::Create($temporary)
        try { $gzip.CopyTo($output) } finally { $output.Dispose(); $gzip.Dispose(); $inputStream.Dispose(); $response.Dispose() }
        if ((Get-Item -LiteralPath $temporary).Length -lt 10000000) { throw 'The database download is unexpectedly small.' }
        Move-Item -LiteralPath $temporary -Destination $database -Force
        @{ release = $month; source = $url; license = 'CC-BY-4.0'; sha256 = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'geoip-release.json')
        $downloaded = $true
        break
    }
    if (-not $downloaded) { throw 'No recent DB-IP Lite release could be downloaded.' }
} finally {
    $client.Dispose()
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
