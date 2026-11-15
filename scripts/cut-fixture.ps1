# Cuts a square of the built Network (wend network build) into a gzipped OSM XML fixture for the
# integration specs (PLAN T013). Run from the repository root after a Greater London build:
#   powershell -File .\scripts\cut-fixture.ps1 -Latitude 51.4436 -Longitude -0.2963 -HalfMetres 3500 `
#     -Out tests\WendYourWay.Integration.Specs\Fixtures\richmond-park.osm.gz
param(
    [Parameter(Mandatory)] [double] $Latitude,
    [Parameter(Mandatory)] [double] $Longitude,
    [Parameter(Mandatory)] [int] $HalfMetres,
    [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
$psql = Join-Path $env:ProgramFiles 'PostgreSQL\17\bin\psql.exe'
$xml = [System.IO.Path]::GetTempFileName()
try {
    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    & $psql -h localhost -U cutthrough -d cutthrough -At -v ON_ERROR_STOP=1 `
        -v "lat=$Latitude" -v "lon=$Longitude" -v "half_m=$HalfMetres" -f (Join-Path $PSScriptRoot 'cut-fixture.sql') -o $xml
    if ($LASTEXITCODE -ne 0) { throw "psql failed ($LASTEXITCODE)" }
    $source = [System.IO.File]::OpenRead($xml)
    $target = [System.IO.File]::Create($Out)
    $gzip = New-Object System.IO.Compression.GZipStream($target, [System.IO.Compression.CompressionLevel]::Optimal)
    try { $source.CopyTo($gzip) } finally { $gzip.Dispose(); $target.Dispose(); $source.Dispose() }
    "{0}: {1:N0} bytes" -f $Out, (Get-Item $Out).Length
} finally {
    Remove-Item $xml -ErrorAction SilentlyContinue
}
