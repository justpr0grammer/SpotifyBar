param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$referenceRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$facades = Join-Path $referenceRoot 'Facades'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
foreach ($required in @($compiler, $facades, (Join-Path $metadata 'Windows.Media.winmd'))) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Missing build dependency: $required. Install .NET Framework 4.8 Developer Pack and build on Windows 10/11 x64."
    }
}
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null
if ($Test) {
    $testExe = Join-Path $output 'ClockTest.exe'
    & $compiler /nologo /target:exe "/out:$testExe" (Join-Path $PSScriptRoot 'src\PlaybackClock.cs') (Join-Path $PSScriptRoot 'src\MediaSelection.cs') (Join-Path $PSScriptRoot 'tests\ClockTest.cs') (Join-Path $PSScriptRoot 'tests\MediaSelectionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Clock tests failed.' }
}
$refs = @('System.Drawing.dll', 'System.Windows.Forms.dll', "$framework\System.Runtime.WindowsRuntime.dll", "$facades\System.Runtime.dll", "$facades\System.Threading.Tasks.dll", "$facades\System.Runtime.InteropServices.WindowsRuntime.dll", "$facades\System.ObjectModel.dll")
$refs += Get-ChildItem -LiteralPath $metadata -Filter *.winmd | ForEach-Object FullName
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', "/out:$output\SpotifyBar.exe")
$arguments += $refs | ForEach-Object { '/r:' + $_ }
$arguments += @('Audio.cs','Media.cs','MediaSelection.cs','PlaybackClock.cs','Program.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close the running dist\SpotifyBar.exe before rebuilding.' }
Write-Host "Built: $output\SpotifyBar.exe"
