param([string]$Destination = (Join-Path $PSScriptRoot '..\engine'))
$ErrorActionPreference = 'Stop'
$uri = 'https://github.com/streamlink/windows-builds/releases/download/8.6.2-2/streamlink-8.6.2-2-py314-x86_64.zip'
$expected = '12e0c8e43b736f75ec67e44cd375ee05be301d7f25a5e2ec1aaa1f81c67ff2b3'
$archive = Join-Path ([IO.Path]::GetTempPath()) ('twitchgrab-engine-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
 Invoke-WebRequest -Uri $uri -OutFile $archive -UseBasicParsing
 if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Engine SHA256 mismatch; refusing to extract.' }
 New-Item -ItemType Directory -Path $Destination -Force | Out-Null
 Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
 Write-Output 'Verified Streamlink / Python / FFmpeg engine is ready.'
} finally { if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force } }
