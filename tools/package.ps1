# Builds the release copy of Bloom into dist\app and checks it.
#   framework-dependent win-x64 (needs the .NET 10 Desktop Runtime, already on this PC).
#   -ReadyToRun precompiles the code (faster first start); it needs the crossgen2 pack from NuGet
#   (Microsoft.NETCore.App.Crossgen2.win-x64), which is not in the local cache yet.
param([switch]$ReadyToRun)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Bloom\Bloom.csproj'
$out = Join-Path $root 'dist\app'

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
$r2r = if ($ReadyToRun) { 'true' } else { 'false' }
& dotnet publish $proj -c Release -r win-x64 --self-contained false "-p:PublishReadyToRun=$r2r" -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# what the installed copy needs
foreach ($name in 'Bloom.exe', 'Bloom.dll', 'Bloom.runtimeconfig.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $out $name))) { throw "missing $name" }
}
$rc = Get-Content -LiteralPath (Join-Path $out 'Bloom.runtimeconfig.json') -Raw | ConvertFrom-Json
$gc = $rc.runtimeOptions.configProperties
if ($gc.'System.GC.Concurrent' -ne $false -or $gc.'System.GC.ConserveMemory' -ne 5) { throw 'runtimeconfig lacks the GC settings' }
# no debug leftovers
$pdb = @(Get-ChildItem -LiteralPath $out -Filter *.pdb)
foreach ($f in $pdb) { Remove-Item -LiteralPath $f.FullName }

$files = @(Get-ChildItem -LiteralPath $out -Recurse -File)
$mb = ($files | Measure-Object Length -Sum).Sum / 1MB
$hash = (Get-FileHash -LiteralPath (Join-Path $out 'Bloom.dll') -Algorithm SHA256).Hash
'dist\app: {0} files, {1:0.0} MB, ReadyToRun {2}, Bloom.dll SHA256 {3}' -f $files.Count, $mb, $r2r, $hash
