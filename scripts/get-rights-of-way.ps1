# The highway authorities' Definitive Map byways, for Trail Rides: now `wend byways get` (T142), which
# runs on any machine. Kept so the old instructions still work.
param([string]$Folder = "data/rights-of-way")
$ErrorActionPreference = "Stop"
dotnet run -c Release --project src/WendYourWay.Cli -- byways get --folder $Folder
exit $LASTEXITCODE
