param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 on Windows x64.' }
$root = $PSScriptRoot
$source = Join-Path $root 'src\ChudoObuv'
$references = @('System.dll','System.Core.dll','System.Configuration.dll','System.Data.dll','System.Xaml.dll','System.Xml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:anycpu','/main:ChudoObuv.App',('/out:' + (Join-Path $root 'ChudoObuv.exe')),('/win32icon:' + (Join-Path $root 'Assets\app.ico')))
foreach ($name in $references) {
    $path = Join-Path $framework $name
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path (Join-Path $framework 'WPF') $name }
    $arguments += '/reference:' + $path
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source 'Views') -Filter '*.xaml') {
    $arguments += '/resource:' + $file.FullName + ',ChudoObuv.Views.' + $file.Name
}
$arguments += @(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Copy-Item -LiteralPath (Join-Path $source 'App.config') -Destination (Join-Path $root 'ChudoObuv.exe.config') -Force
Write-Host 'Build succeeded: ChudoObuv.exe'
if ($Tests) {
    $testArguments = @('/nologo','/target:exe','/optimize+','/main:ChudoObuv.Tests.IntegrationTests',('/out:' + (Join-Path $root 'ChudoObuv.Tests.exe')),('/reference:' + (Join-Path $root 'ChudoObuv.exe')))
    foreach ($name in $references) {
        $path = Join-Path $framework $name
        if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path (Join-Path $framework 'WPF') $name }
        $testArguments += '/reference:' + $path
    }
    $testArguments += Join-Path $root 'tests\IntegrationTests.cs'
    & $compiler @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item -LiteralPath (Join-Path $root 'src\ChudoObuv\App.config') -Destination (Join-Path $root 'ChudoObuv.Tests.exe.config') -Force
}
