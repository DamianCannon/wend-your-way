#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs everything Wend Your Way needs on a Windows laptop. Safe to re-run.

.DESCRIPTION
  1. .NET 10 SDK                       (winget)
  2. PostgreSQL 17                     (winget, EDB installer, unattended)
  3. PostGIS                           (OSGeo Windows bundle, copied into PostgreSQL)
  4. osm2pgsql                         (official Windows zip, into tools\osm2pgsql)
  5. The `cutthrough` role and database with PostGIS enabled, plus a `cutthrough_test`
     database (also PostGIS) that the integration specs wipe and rebuild.
     And a `cutthrough_practice` database (also PostGIS) for the practice map.
     The role's password is random and stored only in your libpq password file
     (%APPDATA%\postgresql\pgpass.conf), which psql, osm2pgsql and Npgsql all
     read, so no password ever appears in the repository or in connection strings.

  Each step checks whether it is already done and skips if so.

.PARAMETER PostgresPassword
  The password for the PostgreSQL superuser `postgres`. On a fresh install it
  becomes that password; on an existing install it must match the current one.

.EXAMPLE
  # From an elevated PowerShell in the repository root:
  .\scripts\setup.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [SecureString] $PostgresPassword
)

$ErrorActionPreference = 'Stop'
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$PgVersion  = '17'
$PgRoot     = "C:\Program Files\PostgreSQL\$PgVersion"
$PgBin      = Join-Path $PgRoot 'bin'
$Psql       = Join-Path $PgBin 'psql.exe'
$ToolsDir   = Join-Path $RepoRoot 'tools'
$Osm2pgsqlDir = Join-Path $ToolsDir 'osm2pgsql'
$DbName     = 'cutthrough'
$DbRole     = 'cutthrough'
$PgPassFile = Join-Path $env:APPDATA 'postgresql\pgpass.conf'

$summary = [System.Collections.Generic.List[string]]::new()
function Step([string] $name) { Write-Host "`n== $name" -ForegroundColor Cyan }
function Done([string] $line) { Write-Host "   $line" -ForegroundColor Green; $summary.Add($line) }

function ConvertTo-PlainText([SecureString] $secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Invoke-Winget([string] $id, [string] $override) {
    $wingetArgs = @('install', '--id', $id, '--exact', '--silent',
                    '--accept-package-agreements', '--accept-source-agreements')
    if ($override) { $wingetArgs += @('--override', $override) }
    & winget @wingetArgs
    if ($LASTEXITCODE -ne 0) { throw "winget install $id failed with exit code $LASTEXITCODE" }
}

# Runs a native program and returns its combined output. Windows PowerShell 5.1
# turns anything a native program writes to stderr into an error, which would
# stop the script under ErrorActionPreference=Stop even when the program
# succeeded (osm2pgsql prints its version there, psql its NOTICEs). Success is
# judged by the exit code instead.
function Invoke-Native([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $command 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n").Trim() }
    }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Download([string] $url, [string] $path) {
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing -UserAgent 'Wend Your Way-setup'
}

$superPassword = ConvertTo-PlainText $PostgresPassword

# ---------------------------------------------------------------------------
Step '.NET 10 SDK'
$sdks = (& dotnet --list-sdks 2>$null) -join "`n"
if ($sdks -match '(?m)^10\.') {
    Done ".NET 10 SDK already installed"
} else {
    Invoke-Winget 'Microsoft.DotNet.SDK.10' $null
    Done ".NET 10 SDK installed"
}

# ---------------------------------------------------------------------------
Step "PostgreSQL $PgVersion"
if (Test-Path $Psql) {
    Done "PostgreSQL $PgVersion already installed at $PgRoot"
} else {
    # The superuser password goes to the installer in an option file only this user can read,
    # deleted straight after, never on a command line (which the process list and winget's
    # logs would show).
    $optionFile = Join-Path $env:TEMP "cutthrough-pg-options-$([guid]::NewGuid().ToString('N')).txt"
    try {
        Set-Content -Path $optionFile -Value "superpassword=$superPassword" -Encoding ascii
        icacls $optionFile /inheritance:r /grant:r "${env:USERNAME}:(R,W,D)" | Out-Null
        $override = "--mode unattended --unattendedmodeui none --optionfile `"$optionFile`" " +
                    "--serverport 5432 --servicename postgresql-$PgVersion --disable-components stackbuilder"
        Invoke-Winget "PostgreSQL.PostgreSQL.$PgVersion" $override
    }
    finally {
        Remove-Item $optionFile -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path $Psql)) { throw "PostgreSQL installed but $Psql was not found" }
    Done "PostgreSQL $PgVersion installed at $PgRoot (service postgresql-$PgVersion, port 5432)"
}

# ---------------------------------------------------------------------------
Step 'PostGIS'
$postgisControl = Join-Path $PgRoot 'share\extension\postgis.control'
if (Test-Path $postgisControl) {
    Done "PostGIS already present in $PgRoot"
} else {
    $listing = "https://download.osgeo.org/postgis/windows/pg$PgVersion/"
    $page = Invoke-WebRequest -Uri $listing -UseBasicParsing -UserAgent 'Wend Your Way-setup'
    $bundle = $page.Links.href |
        Where-Object { $_ -match "^postgis-bundle-pg$PgVersion-[\d.]+x64\.zip$" } |
        Sort-Object { [version](($_ -replace "^postgis-bundle-pg$PgVersion-", '') -replace 'x64\.zip$', '') } |
        Select-Object -Last 1
    if (-not $bundle) { throw "No PostGIS bundle found at $listing" }

    $zip = Join-Path $env:TEMP $bundle
    $unpacked = Join-Path $env:TEMP ($bundle -replace '\.zip$', '')
    Invoke-Download "$listing$bundle" $zip
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $unpacked

    # The zip holds one top-level folder whose bin/lib/share mirror PostgreSQL's.
    $top = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    $source = if ($top -and (Test-Path (Join-Path $top.FullName 'share'))) { $top.FullName } else { $unpacked }

    # PostgreSQL holds its DLLs open while running, so stop it for the copy.
    # Files PostgreSQL already ships (e.g. libcrypto) are left alone: only
    # PostGIS's own files and the libraries it adds (GEOS, PROJ, GDAL…) are copied.
    $service = Get-Service -Name "postgresql*$PgVersion*" | Select-Object -First 1
    if (-not $service) { throw "No PostgreSQL $PgVersion service found" }
    Stop-Service -Name $service.Name -Force
    try {
        $copied = 0; $skipped = 0
        Get-ChildItem -Path $source -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($source.Length).TrimStart('\')
            $target = Join-Path $PgRoot $relative
            if (Test-Path $target) { $skipped++; return }
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -Path $_.FullName -Destination $target
            $copied++
        }
    }
    finally {
        Start-Service -Name $service.Name
    }
    if (-not (Test-Path $postgisControl)) { throw "PostGIS copied but $postgisControl is missing" }
    Done "PostGIS installed from $bundle ($copied files added, $skipped already present and kept)"
}

# ---------------------------------------------------------------------------
Step 'osm2pgsql'
$osm2pgsqlExe = Get-ChildItem $Osm2pgsqlDir -Filter 'osm2pgsql.exe' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($osm2pgsqlExe) {
    Done "osm2pgsql already present at $($osm2pgsqlExe.FullName)"
} else {
    New-Item -ItemType Directory -Force -Path $Osm2pgsqlDir | Out-Null
    $zip = Join-Path $env:TEMP 'osm2pgsql-latest-x64.zip'
    Invoke-Download 'https://osm2pgsql.org/download/windows/osm2pgsql-latest-x64.zip' $zip
    Expand-Archive -Path $zip -DestinationPath $Osm2pgsqlDir -Force
    $osm2pgsqlExe = Get-ChildItem $Osm2pgsqlDir -Filter 'osm2pgsql.exe' -Recurse | Select-Object -First 1
    if (-not $osm2pgsqlExe) { throw "osm2pgsql.exe not found after unpacking into $Osm2pgsqlDir" }
    Done "osm2pgsql installed at $($osm2pgsqlExe.FullName)"
}
$exe = $osm2pgsqlExe.FullName
$result = Invoke-Native { & $exe --version }
if ($result.ExitCode -ne 0) { throw "osm2pgsql --version failed: $($result.Output)" }
Done "osm2pgsql reports: $(($result.Output -split "`n")[0])"

# ---------------------------------------------------------------------------
Step 'Database, role and PostGIS extension'
$env:PGPASSWORD = $superPassword
try {
    function Sql([string] $database, [string] $statement) {
        # -q silences NOTICEs; stdout and stderr are kept apart so only real rows are returned.
        # The statement goes in on stdin, never on the command line, and the session never logs
        # a failing statement, so a password in it can't reach the process list or the server log.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $errFile = [System.IO.Path]::GetTempFileName()
            $rows = "set log_min_error_statement = panic;`n$statement;" |
                & $Psql -h localhost -p 5432 -U postgres -d $database -q -v ON_ERROR_STOP=1 -tA -f - 2> $errFile
            $code = $LASTEXITCODE
            $err = (Get-Content $errFile -Raw)
            Remove-Item $errFile -ErrorAction SilentlyContinue
        }
        finally { $ErrorActionPreference = $previous }
        if ($code -ne 0) { throw "psql failed ($code): $err" }
        return (($rows | Out-String).Trim())
    }

    # Role, with a random password kept only in pgpass.conf.
    $hasPassEntry = (Test-Path $PgPassFile) -and
        (Select-String -Path $PgPassFile -SimpleMatch "localhost:5432:*:${DbRole}:" -Quiet)
    $roleExists = (Sql 'postgres' "select 1 from pg_roles where rolname = '$DbRole'") -eq '1'

    if ($roleExists -and $hasPassEntry) {
        Done "Role '$DbRole' already exists with a pgpass.conf entry"
    } else {
        $bytes = New-Object byte[] 24
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $rolePassword = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
        if ($roleExists) {
            Sql 'postgres' "alter role $DbRole with login password '$rolePassword'" | Out-Null
        } else {
            Sql 'postgres' "create role $DbRole with login password '$rolePassword'" | Out-Null
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $PgPassFile) | Out-Null
        $kept = @()
        if (Test-Path $PgPassFile) {
            $kept = Get-Content $PgPassFile | Where-Object { $_ -notlike "localhost:5432:*:${DbRole}:*" }
        }
        Set-Content -Path $PgPassFile -Value ($kept + "localhost:5432:*:${DbRole}:$rolePassword") -Encoding ascii
        Done "Role '$DbRole' ready; its password is in $PgPassFile"
    }

    if ((Sql 'postgres' "select 1 from pg_database where datname = '$DbName'") -eq '1') {
        Done "Database '$DbName' already exists"
    } else {
        Sql 'postgres' "create database $DbName owner $DbRole" | Out-Null
        Done "Database '$DbName' created, owned by '$DbRole'"
    }

    Sql $DbName 'create extension if not exists postgis' | Out-Null
    $postgisVersion = (Sql $DbName 'select postgis_lib_version()')
    Done "PostGIS $postgisVersion enabled in '$DbName'"

    # A separate database for the integration specs, which wipe and rebuild it on every run.
    $TestDbName = "${DbName}_test"
    if ((Sql 'postgres' "select 1 from pg_database where datname = '$TestDbName'") -eq '1') {
        Done "Database '$TestDbName' already exists"
    } else {
        Sql 'postgres' "create database $TestDbName owner $DbRole" | Out-Null
        Done "Database '$TestDbName' created for the integration specs, owned by '$DbRole'"
    }
    Sql $TestDbName 'create extension if not exists postgis' | Out-Null
    Done "PostGIS enabled in '$TestDbName'"

    # The practice map's database (T161): Richmond Park and around, to try Wend Your Way on first
    # (`wend practice` builds it; Help's Switch to the practice map opens it).
    $PracticeDbName = "${DbName}_practice"
    if ((Sql 'postgres' "select 1 from pg_database where datname = '$PracticeDbName'") -eq '1') {
        Done "Database '$PracticeDbName' already exists"
    } else {
        Sql 'postgres' "create database $PracticeDbName owner $DbRole" | Out-Null
        Done "Database '$PracticeDbName' created for the practice map, owned by '$DbRole'"
    }
    Sql $PracticeDbName 'create extension if not exists postgis' | Out-Null
    Done "PostGIS enabled in '$PracticeDbName'"
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
Write-Host "`n== Summary" -ForegroundColor Cyan
$summary | ForEach-Object { Write-Host "   - $_" }
Write-Host "`nConnection string for the app (no password; it comes from pgpass.conf):"
Write-Host "   Host=localhost;Port=5432;Database=$DbName;Username=$DbRole" -ForegroundColor Yellow
