<#
Build the harness against a Swiss Ephemeris source tree, with MSVC.

    ./build.ps1 -Source C:\path\to\swisseph-sources [-Out .\build]

The source tree is copied first, so applying a patch for one build never
disturbs another and the shipped sources are never touched. /fp:precise matters:
without it the compiler is free to contract multiplies and adds, and the
bit-identical position claim this harness checks would be testing the optimiser
rather than the patch. It is MSVC's equivalent of the -ffp-contract=off that
build.sh passes to gcc and clang, and it is passed to every compile below,
including the harness's own translation units and not just the library's.
#>
param(
  [Parameter(Mandatory = $true)][string]$Source,
  [string]$Out = (Join-Path (Get-Location) 'build')
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found; Visual Studio with the C++ toolset is required." }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found under $vs" }

New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item (Join-Path $Source '*.c') $Out -Force
Copy-Item (Join-Path $Source '*.h') $Out -Force

$bat = @"
@echo off
call "$vcvars" >nul
cd /d "$Out"
del /q *.obj libswe.lib 2>nul
cl /nologo /c /O2 /fp:precise /MD /D_CRT_SECURE_NO_WARNINGS swedate.c swehouse.c swejpl.c swemmoon.c swemplan.c sweph.c swephlib.c swecl.c swehel.c || exit /b 1
lib /nologo /out:libswe.lib swedate.obj swehouse.obj swejpl.obj swemmoon.obj swemplan.obj sweph.obj swephlib.obj swecl.obj swehel.obj || exit /b 1
cl /nologo /O2 /fp:precise /MD /D_CRT_SECURE_NO_WARNINGS /I. /I"$here" /Fehouse-speed-harness.exe "$here\house-speed-harness.c" libswe.lib || exit /b 1
cl /nologo /O2 /fp:precise /MD /D_CRT_SECURE_NO_WARNINGS /I. /I"$here" /Fecompare.exe "$here\compare.c" libswe.lib || exit /b 1
"@
$batPath = Join-Path $Out '_build.bat'
Set-Content -Path $batPath -Value $bat -Encoding ascii
& $batPath
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

Write-Output "built in ${Out}:"
Write-Output "  $Out\house-speed-harness.exe"
Write-Output "  $Out\compare.exe"
