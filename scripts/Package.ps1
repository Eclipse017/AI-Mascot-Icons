#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = '1.1.0'
$exe = Join-Path $root 'artifacts\build\AI-Mascot-Icons.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the application first: ./scripts/Build.ps1 -Test' }
$expectedVersion = "$version.0"
$fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($fileVersion -ne $expectedVersion) { throw "EXE FileVersion mismatch: expected $expectedVersion, got $fileVersion" }
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString()
if ($assemblyVersion -ne $expectedVersion) { throw "EXE AssemblyVersion mismatch: expected $expectedVersion, got $assemblyVersion" }
$releaseDoc = "RELEASE-v$version.md"
if (!(Test-Path -LiteralPath "$root\docs\$releaseDoc")) { throw "Missing release notes: docs\$releaseDoc" }

$iconIds = @('ChatGPT','Claude','Gemini','Grok','DeepSeek','ZCode','DoubaoClassic','DoubaoWork','MiniMax','WorkBuddy','Kimi')
$sourceFiles = @(
    '.gitignore',
    'ASSETS.md',
    'LICENSE',
    'README.md',
    'VALIDATION.md',
    'docs/QUICK-START.txt',
    'docs/RELEASE-v1.0.0.md',
    "docs/$releaseDoc",
    'docs/XIAOHONGSHU.md',
    'docs/images/download-guide.png',
    'docs/images/tool-intro.png',
    'docs/preview.png',
    'scripts/Build.ps1',
    'scripts/Package.ps1',
    'src/App.config',
    'src/IconStore.cs',
    'src/MainWindow.cs',
    'src/NativeLink.cs',
    'src/Program.cs',
    'src/ShortcutService.cs',
    'src/app.manifest',
    'tests/Tests.cs'
) + ($iconIds | ForEach-Object { @("assets/icons/$_.ico", "assets/icons/$_.png") })

$releases = Join-Path $root 'artifacts\releases'
New-Item -ItemType Directory -Path $releases -Force | Out-Null
$runId = [guid]::NewGuid().ToString('N')
$stage = Join-Path $root ("artifacts\package-$runId")
$sourceStage = Join-Path $root ("artifacts\source-$runId")
$verify = Join-Path $root ("artifacts\verify-$runId")
$verifySource = Join-Path $root ("artifacts\verify-src-$runId")

try {
    New-Item -ItemType Directory -Path "$stage\docs" -Force | Out-Null
    Copy-Item -LiteralPath $exe -Destination "$stage\AI-Mascot-Icons.exe"
    Copy-Item -LiteralPath "$exe.config" -Destination "$stage\AI-Mascot-Icons.exe.config"
    foreach ($file in @('README.md','LICENSE','ASSETS.md','VALIDATION.md')) { Copy-Item -LiteralPath "$root\$file" -Destination "$stage\$file" }
    Copy-Item -LiteralPath "$root\docs\QUICK-START.txt" -Destination "$stage\使用说明.txt"
    foreach ($file in @('preview.png','QUICK-START.txt','XIAOHONGSHU.md',$releaseDoc)) {
        Copy-Item -LiteralPath "$root\docs\$file" -Destination "$stage\docs\$file"
    }
    $manifest = foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\','/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    $manifest | Set-Content -LiteralPath "$stage\FILES-SHA256.txt" -Encoding utf8NoBOM
    $binaryZip = Join-Path $releases "AI-Mascot-Icons-v$version-windows.zip"
    Compress-Archive -Path "$stage\*" -DestinationPath $binaryZip -CompressionLevel Optimal -Force

    # Explicit source release allowlist: never package local settings, tests' .lnk files, or netdisk-only scripts/docs.
    New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
    foreach ($rel in $sourceFiles) {
        $srcPath = Join-Path $root ($rel.Replace('/','\'))
        if (!(Test-Path -LiteralPath $srcPath)) { throw "Missing source release file: $rel" }
        $dstPath = Join-Path $sourceStage ($rel.Replace('/','\'))
        $dstDir = Split-Path $dstPath -Parent
        if (!(Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item -LiteralPath $srcPath -Destination $dstPath
    }
    $sourceZip = Join-Path $releases "AI-Mascot-Icons-v$version-source.zip"
    Compress-Archive -Path "$sourceStage\*" -DestinationPath $sourceZip -CompressionLevel Optimal -Force

    $checksums = foreach ($path in @($binaryZip,$sourceZip)) { '{0}  {1}' -f (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($path) }
    $checksums | Set-Content -LiteralPath "$releases\SHA256SUMS.txt" -Encoding utf8NoBOM

    Expand-Archive -LiteralPath $binaryZip -DestinationPath $verify
    foreach ($line in Get-Content -LiteralPath "$verify\FILES-SHA256.txt") {
        $hash, $relative = $line -split '  ',2
        if ((Get-FileHash -LiteralPath (Join-Path $verify $relative) -Algorithm SHA256).Hash -ne $hash) { throw "ZIP verification failed: $relative" }
    }
    if (@(Get-ChildItem -LiteralPath $verify -Recurse -File).Count -ne @($manifest).Count + 1) { throw 'Unexpected ZIP entry count.' }
    $packagedFileVersion = (Get-Item -LiteralPath "$verify\AI-Mascot-Icons.exe").VersionInfo.FileVersion
    if ($packagedFileVersion -ne $expectedVersion) { throw "Packaged EXE FileVersion mismatch: expected $expectedVersion, got $packagedFileVersion" }
    $packagedAsmVersion = [Reflection.AssemblyName]::GetAssemblyName("$verify\AI-Mascot-Icons.exe").Version.ToString()
    if ($packagedAsmVersion -ne $expectedVersion) { throw "Packaged EXE AssemblyVersion mismatch: expected $expectedVersion, got $packagedAsmVersion" }
    if (!(Test-Path -LiteralPath "$verify\docs\$releaseDoc")) { throw "Missing release notes in distribution ZIP: docs\$releaseDoc" }
    if ($version -ne '1.0.0' -and (Test-Path -LiteralPath "$verify\docs\RELEASE-v1.0.0.md")) { throw 'Legacy v1.0.0 release notes should not be included in newer distribution ZIP.' }

    $readmeRaw = Get-Content -LiteralPath "$verify\README.md" -Raw -Encoding utf8
    foreach ($match in [regex]::Matches($readmeRaw, '\[[^\]]*\]\(([^)]+)\)')) {
        $target = ($match.Groups[1].Value -split '#',2)[0].Trim()
        if ($target -and $target -notmatch '^(?:https?|mailto):') {
            $resolved = Join-Path $verify ($target.Replace('/','\'))
            if (!(Test-Path -LiteralPath $resolved)) { throw "Broken relative link in packaged README.md: $target" }
        }
    }

    Expand-Archive -LiteralPath $sourceZip -DestinationPath $verifySource
    $expectedSource = @($sourceFiles | Sort-Object -Unique)
    $actualSource = @(Get-ChildItem -LiteralPath $verifySource -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($verifySource, $_.FullName).Replace('\','/') } | Sort-Object)
    $diff = Compare-Object -ReferenceObject $expectedSource -DifferenceObject $actualSource
    if ($diff) {
        $details = ($diff | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '
        throw "Source ZIP manifest mismatch: $details"
    }
    foreach ($excluded in @('docs/ICONS-QUICK-START.txt','docs/NETDISK-QUICK-START.txt','scripts/Package-Netdisk.ps1')) {
        if ($actualSource -contains $excluded) { throw "Non-release file leaked into source ZIP: $excluded" }
    }
    foreach ($syncCheck in $expectedSource) {
        $srcHash = (Get-FileHash -LiteralPath (Join-Path $root ($syncCheck.Replace('/','\'))) -Algorithm SHA256).Hash
        $zipHash = (Get-FileHash -LiteralPath (Join-Path $verifySource ($syncCheck.Replace('/','\'))) -Algorithm SHA256).Hash
        if ($srcHash -ne $zipHash) { throw "Source ZIP file is stale vs working tree: $syncCheck" }
    }

    Get-Item -LiteralPath $binaryZip,$sourceZip | Select-Object Name,Length,FullName
    Write-Output ("PASS: distribution ZIP & source ZIP verified against manifests and SHA-256. EXE={0}, BinaryEntries={1}, SourceEntries={2}, ReleaseDoc=docs/{3}" -f $packagedFileVersion, (@($manifest).Count + 1), $actualSource.Count, $releaseDoc)
}
finally {
    $artifactRoot = (Resolve-Path -LiteralPath (Join-Path $root 'artifacts')).ProviderPath
    foreach ($temporary in @($stage,$sourceStage,$verify,$verifySource)) {
        if (!(Test-Path -LiteralPath $temporary)) { continue }
        $target = Get-Item -LiteralPath $temporary
        if ($target.Parent.FullName -ne $artifactRoot -or !$target.Name.EndsWith($runId) -or
            ($target.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unexpected staging directory: $temporary" }
        Remove-Item -LiteralPath $target.FullName -Recurse -Force
    }
}
