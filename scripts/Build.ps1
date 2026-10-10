#requires -Version 7.0
[CmdletBinding()]
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$references = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$arguments = @('/nologo','/optimize+','/checked+','/langversion:5','/platform:anycpu','/target:winexe',"/out:$out\AI-Mascot-Icons.exe", "/win32manifest:$root\src\app.manifest", "/win32icon:$root\assets\icons\ChatGPT.ico")
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $framework $reference) }
foreach ($asset in Get-ChildItem -LiteralPath "$root\assets\icons" -File) { $arguments += "/resource:$($asset.FullName),Icons.$($asset.Name)" }
$sources = @(Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs' -File | ForEach-Object FullName)
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath "$root\src\App.config" -Destination "$out\AI-Mascot-Icons.exe.config" -Force
if ($Test) {
    $testArguments = @($arguments | Where-Object { $_ -notlike '/out:*' -and $_ -notlike '/target:*' -and $_ -notlike '/win32icon:*' })
    $testArguments += @('/target:exe', '/main:MascotIcons.Tests', "/out:$out\AI-Mascot-Icons.Tests.exe")
    & $compiler @testArguments @sources "$root\tests\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item -LiteralPath "$root\src\App.config" -Destination "$out\AI-Mascot-Icons.Tests.exe.config" -Force
    & "$out\AI-Mascot-Icons.Tests.exe" "$root\artifacts\tests"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Get-Item -LiteralPath "$out\AI-Mascot-Icons.exe" | Select-Object FullName,Length
