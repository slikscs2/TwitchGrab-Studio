$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$payload=Join-Path $repoRoot 'dist\portable'
if(Test-Path -LiteralPath $payload){throw 'dist/portable already exists; choose a clean build directory.'}
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach($name in @('TwitchGrab-Studio.exe','Studio.xaml','progress_bridge.py','THIRD_PARTY_NOTICES.md')){
 Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\README.txt') -Destination $payload
New-Item -ItemType Directory -Path (Join-Path $payload 'assets') | Out-Null
foreach($name in @('logo.png','app.ico')){Copy-Item -LiteralPath (Join-Path $repoRoot ('assets\'+$name)) -Destination (Join-Path $payload 'assets')}
Copy-Item -LiteralPath (Join-Path $repoRoot 'engine') -Destination $payload -Recurse
$archive=Join-Path $repoRoot 'dist\TwitchGrab-Studio-Portable-v7.0.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $payload | Select-Object -ExpandProperty FullName) -DestinationPath $archive
$lines=Get-ChildItem -LiteralPath (Join-Path $repoRoot 'dist') -File | Where-Object { $_.Extension -in '.exe','.zip' } | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name }
$lines | Set-Content -LiteralPath (Join-Path $repoRoot 'dist\SHA256SUMS.txt') -Encoding ASCII
Write-Output 'Portable archive and SHA256SUMS.txt are ready.'
