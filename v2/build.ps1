[CmdletBinding()]
param([switch]$TestOnly)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Join-Path $root 'v2'
$out=Join-Path $root 'dist-v2'
New-Item -ItemType Directory -Force $out | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path "$framework\csc.exe")) { $framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc=Join-Path $framework 'csc.exe'
if(-not (Test-Path $csc)) { throw "C# compiler unavailable." }
$meta=Join-Path $env:WINDIR 'System32\WinMetadata'
$refs=@(
  '-r:System.dll','-r:System.Core.dll','-r:System.Drawing.dll','-r:System.Windows.Forms.dll',
  "-r:$framework\System.Runtime.dll",
  "-r:$framework\System.Runtime.InteropServices.WindowsRuntime.dll",
  "-r:$meta\Windows.Foundation.winmd",
  "-r:$meta\Windows.Devices.winmd"
)
$files=@(Get-ChildItem -Path $source -Filter '*.cs' -File | ForEach-Object { $_.FullName })
if($files.Count -lt 5){throw "Missing V2 sources."}
$common=@('-nologo','-warn:4','-optimize+','-platform:anycpu','-langversion:5')
$testfile=Join-Path $source 'tests\EngineTests.cs'
$testexe=Join-Path $out 'EngineTests.exe'
& $csc @common '-target:exe' "-out:$testexe" '-main:SteadyCuesV2.Tests.Runner' (Join-Path $source 'Core.cs') $testfile
if($LASTEXITCODE -ne 0){throw "Engine tests compilation failed."}
& $testexe
if($LASTEXITCODE -ne 0){throw "Engine tests failed."}
if($TestOnly){return}
# Compile the complete application plus a dedicated WinForms layout test entrypoint.
$layoutExe=Join-Path $out 'LayoutTests.exe'
$layoutSource=Join-Path $source 'tests\LayoutTests.cs'
$icon=Join-Path $root 'src\SteadyCues\Assets\SteadyCues.ico'
$manifest=Join-Path $root 'src\SteadyCues\app.manifest'
& $csc @common @refs '-target:exe' '-main:SteadyCuesV2.Tests.LayoutRunner' "-out:$layoutExe" @files $layoutSource
if($LASTEXITCODE -ne 0){throw "RTL layout tests compilation failed."}
& $layoutExe
if($LASTEXITCODE -ne 0){throw "RTL layout tests failed."}

$exe=Join-Path $out 'SteadyCues2.exe'
$icon=Join-Path $root 'src\SteadyCues\Assets\SteadyCues.ico'
$manifest=Join-Path $root 'src\SteadyCues\app.manifest'
& $csc @common @refs '-target:winexe' "-out:$exe" "-win32icon:$icon" "-win32manifest:$manifest" @files
if($LASTEXITCODE -ne 0){throw "V2 app compilation failed."}
$hash=(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  SteadyCues2.exe" | Set-Content -Encoding ASCII (Join-Path $out 'SteadyCues2.exe.sha256')
Write-Host "V2 built and tested: $exe" -ForegroundColor Green
