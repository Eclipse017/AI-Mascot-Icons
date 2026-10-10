#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = '1.0.0'
$exe = Join-Path $root 'artifacts\build\AI-Mascot-Icons.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the application first: ./scripts/Build.ps1 -Test' }
$releases = Join-Path $root 'artifacts\releases'
New-Item -ItemType Directory -Path $releases -Force | Out-Null
$stage = Join-Path $root ('artifacts\package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$stage\docs" -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination "$stage\AI-Mascot-Icons.exe"
Copy-Item -LiteralPath "$exe.config" -Destination "$stage\AI-Mascot-Icons.exe.config"
foreach ($file in @('README.md','LICENSE','ASSETS.md','VALIDATION.md')) { Copy-Item -LiteralPath "$root\$file" -Destination "$stage\$file" }
Copy-Item -LiteralPath "$root\docs\QUICK-START.txt" -Destination "$stage\使用说明.txt"
foreach ($file in @('preview.png','QUICK-START.txt','XIAOHONGSHU.md','RELEASE-v1.0.0.md')) {
    Copy-Item -LiteralPath "$root\docs\$file" -Destination "$stage\docs\$file"
}
$manifest = foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$manifest | Set-Content -LiteralPath "$stage\FILES-SHA256.txt" -Encoding utf8NoBOM
$binaryZip = Join-Path $releases "AI-Mascot-Icons-v$version-windows.zip"
Compress-Archive -Path "$stage\*" -DestinationPath $binaryZip -CompressionLevel Optimal -Force

# Explicit source allowlist: never package local settings, tests' .lnk files or history.
$sourceStage = Join-Path $root ('artifacts\source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
foreach ($name in @('src','scripts','tests','assets','docs','.gitignore','README.md','LICENSE','ASSETS.md','VALIDATION.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $sourceStage -Recurse
}
$sourceZip = Join-Path $releases "AI-Mascot-Icons-v$version-source.zip"
Compress-Archive -Path "$sourceStage\*" -DestinationPath $sourceZip -CompressionLevel Optimal -Force
$checksums = foreach ($path in @($binaryZip,$sourceZip)) { '{0}  {1}' -f (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($path) }
$checksums | Set-Content -LiteralPath "$releases\SHA256SUMS.txt" -Encoding utf8NoBOM

$verify = Join-Path $root ('artifacts\verify-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $binaryZip -DestinationPath $verify
foreach ($line in Get-Content -LiteralPath "$verify\FILES-SHA256.txt") {
    $hash, $relative = $line -split '  ',2
    if ((Get-FileHash -LiteralPath (Join-Path $verify $relative) -Algorithm SHA256).Hash -ne $hash) { throw "ZIP verification failed: $relative" }
}
if (@(Get-ChildItem -LiteralPath $verify -Recurse -File).Count -ne @($manifest).Count + 1) { throw 'Unexpected ZIP entry count.' }
Get-Item -LiteralPath $binaryZip,$sourceZip | Select-Object Name,Length,FullName
Write-Output 'PASS: distribution ZIP extracted and every file matched its SHA-256 manifest.'
