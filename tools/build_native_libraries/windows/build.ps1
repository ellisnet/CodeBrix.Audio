<#
================================================================================================
build.ps1 - build + verify codebrix_miniaudio.dll for win-x64 and win-arm64
================================================================================================

USAGE (from a normal PowerShell prompt - no Developer prompt needed)

    cd tools\build_native_libraries\windows
    .\build.ps1 x64          # or arm64 / all

WHAT IT PRODUCES

    ..\output\win-x64\codebrix_miniaudio.dll   (+ build-info.txt)
    ..\output\win-arm64\codebrix_miniaudio.dll (+ build-info.txt)

Both are built on ONE x64 Windows machine: win-arm64 is a cross-compile, which is what the
"MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools" component exists for.

PREREQUISITES - installed by YOU. This script never installs anything; if something is
missing it says what, prints the install command, and stops.

  1. Visual Studio 2022 (or newer), workload "Desktop development with C++",
     plus the ARM64/ARM64EC build tools component for win-arm64.
       winget install Microsoft.VisualStudio.2022.Community
  2. CMake 3.26+
       winget install Kitware.CMake

See ..\README.txt for the full prerequisite list and the verification-gate description.
================================================================================================
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('x64', 'arm64', 'all')]
    [string]$Arch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir  = Split-Path -Parent $ScriptDir
$RepoRoot  = (Resolve-Path (Join-Path $ToolsDir '..\..')).Path
$NativeSrc = Join-Path $RepoRoot 'native\miniaudio'
$LibName   = 'codebrix_miniaudio.dll'

# ------------------------------------------------------------------------------------------------
# pins.env is the single source of truth for every version/commit pin. Parse the KEY=VALUE lines.
# ------------------------------------------------------------------------------------------------
$Pins = @{}
Get-Content (Join-Path $ToolsDir 'pins.env') | ForEach-Object {
    if ($_ -match '^\s*([A-Z0-9_]+)=(.*)$') { $Pins[$Matches[1]] = $Matches[2].Trim() }
}

# ------------------------------------------------------------------------------------------------
# Prerequisites
# ------------------------------------------------------------------------------------------------
function Require-Command {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Host ""
        Write-Host "ERROR: '$Name' is not on PATH." -ForegroundColor Red
        Write-Host ""
        Write-Host "  This script does not install anything. Install it yourself:"
        Write-Host "    $InstallHint"
        Write-Host ""
        exit 1
    }
}

Require-Command 'cmake' 'winget install Kitware.CMake'

# Visual Studio: located through vswhere, which ships with every VS 2017+ installation.
$VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $VsWhere)) {
    Write-Host ""
    Write-Host "ERROR: Visual Studio was not found (no vswhere.exe)." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Install Visual Studio 2022 with the 'Desktop development with C++' workload:"
    Write-Host "    winget install Microsoft.VisualStudio.2022.Community"
    Write-Host "  For win-arm64 also tick 'MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools'."
    Write-Host ""
    exit 1
}

$VsPath = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                     -property installationPath
if (-not $VsPath) {
    Write-Host "ERROR: no Visual Studio installation with the C++ tools was found." -ForegroundColor Red
    Write-Host "  Re-run the Visual Studio Installer and add 'Desktop development with C++'."
    exit 1
}

$VcVarsAll = Join-Path $VsPath 'VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path $VcVarsAll)) {
    Write-Host "ERROR: vcvarsall.bat not found under $VsPath" -ForegroundColor Red
    exit 1
}

# dumpbin lives beside the compiler; it is what the export/dependency checks use.
$DumpBin = Get-ChildItem -Path (Join-Path $VsPath 'VC\Tools\MSVC') -Recurse -Filter 'dumpbin.exe' `
                         -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match 'Hostx64\\x64' } |
           Select-Object -First 1
if (-not $DumpBin) {
    Write-Host "ERROR: dumpbin.exe not found in the Visual Studio installation." -ForegroundColor Red
    exit 1
}

Write-Host "Visual Studio : $VsPath"
Write-Host "CMake         : $((cmake --version | Select-Object -First 1))"
Write-Host "Repository    : $RepoRoot"

# ------------------------------------------------------------------------------------------------
# The verification gate. Same checks as the Linux container build, expressed with Windows tools.
# ------------------------------------------------------------------------------------------------
$RequiredSymbols = @(
    'sf_has_vorbis', 'sf_free', 'sf_allocate_decoder', 'sf_allocate_decoder_config',
    'sf_allocate_encoder', 'sf_allocate_encoder_config', 'sf_allocate_device',
    'sf_allocate_device_config', 'sf_allocate_context', 'sf_get_devices',
    'sf_free_device_infos', 'sf_context_get_backend',
    'ma_decoder_init', 'ma_decoder_init_memory', 'ma_decoder_uninit',
    'ma_decoder_read_pcm_frames', 'ma_decoder_seek_to_pcm_frame',
    'ma_decoder_get_length_in_pcm_frames',
    'ma_encoder_init', 'ma_encoder_uninit', 'ma_encoder_write_pcm_frames',
    'ma_context_init', 'ma_context_uninit',
    'ma_device_init', 'ma_device_uninit', 'ma_device_start', 'ma_device_stop'
)

function Invoke-Verification {
    param([string]$DllPath, [string]$TargetArch)

    $failed = $false
    function Pass($m) { Write-Host "  [ok] $m" }
    function Fail($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:verifyFailed = $true }
    $script:verifyFailed = $false

    # 1 + 2. Exports and codec coverage.
    $exportDump = & $DumpBin.FullName /nologo /exports $DllPath
    $exported = $exportDump |
        ForEach-Object { if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') { $Matches[1] } }

    $missing = $RequiredSymbols | Where-Object { $exported -notcontains $_ }
    if ($missing) { Fail "missing exports: $($missing -join ' ')" }
    else          { Pass "all $($RequiredSymbols.Count) required symbols exported" }

    foreach ($codec in @(
        @{ Prefix = 'ma_stbvorbis_'; Name = 'Ogg Vorbis' },
        @{ Prefix = 'ma_dr_flac_';   Name = 'FLAC' },
        @{ Prefix = 'ma_dr_mp3_';    Name = 'MP3' },
        @{ Prefix = 'ma_dr_wav_';    Name = 'WAV' })) {
        $count = ($exported | Where-Object { $_ -like "$($codec.Prefix)*" }).Count
        if ($count -gt 0) { Pass "$($codec.Name) decoder present ($count $($codec.Prefix)* symbols)" }
        else              { Fail "no $($codec.Name) decoder in this binary" }
    }

    # 3. Dependencies: system DLLs only. miniaudio dlopen's/LoadLibrary's the audio backends at
    #    run time by design, so nothing audio-related may be a link-time dependency.
    $deps = & $DumpBin.FullName /nologo /dependents $DllPath |
        ForEach-Object { if ($_ -match '^\s+(\S+\.dll)$') { $Matches[1] } }
    $forbidden = $deps | Where-Object { $_ -match 'vorbis|ogg|FLAC|libwinpthread|libgcc' }
    if ($forbidden) { Fail "unexpected dynamic dependency: $($forbidden -join ' ')" }
    else            { Pass "dependencies are system-only: $($deps -join ' ')" }

    # 4. Decode smoke test - only meaningful when the DLL matches the host architecture.
    #    A cross-compiled ARM64 DLL cannot be loaded by an x64 process, so it is checked
    #    statically above and exercised on ARM64 hardware instead. This is stated, never silent.
    $hostArch = $env:PROCESSOR_ARCHITECTURE
    $canRun = ($TargetArch -eq 'x64' -and $hostArch -eq 'AMD64') -or
              ($TargetArch -eq 'arm64' -and $hostArch -eq 'ARM64')
    if ($canRun) {
        $oggPath  = Join-Path $RepoRoot ($Pins['SMOKE_TEST_OGG'] -replace '/', '\')
        if (-not (Test-Path $oggPath)) {
            Fail "smoke-test input missing: $oggPath (run tools\make_test_fixtures\make_fixtures.sh)"
        } else {
            $smokeSrc = Join-Path $ToolsDir 'smoke_test.c'
            $smokeDir = Join-Path $env:TEMP 'codebrix_smoke'
            New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
            $vcArch = if ($TargetArch -eq 'x64') { 'x64' } else { 'arm64' }
            $cmd = "call `"$VcVarsAll`" $vcArch >nul && cd /d `"$smokeDir`" && " +
                   "cl /nologo /O2 `"$smokeSrc`" >nul && smoke_test.exe `"$DllPath`" `"$oggPath`""
            $output = & cmd.exe /c $cmd 2>&1
            $output | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -eq 0) { Pass 'smoke test' } else { Fail 'smoke test' }
        }
    } else {
        Write-Host "  [--] decode smoke test not run: a $TargetArch DLL cannot be loaded by this"
        Write-Host "       $hostArch process. The static checks above still applied; run the"
        Write-Host "       managed test suite on $TargetArch hardware to exercise it."
    }

    return -not $script:verifyFailed
}

# ------------------------------------------------------------------------------------------------
# Build one architecture
# ------------------------------------------------------------------------------------------------
function Build-Arch {
    param([string]$TargetArch)

    $rid       = "win-$TargetArch"
    $cmakeArch = if ($TargetArch -eq 'x64') { 'x64' } else { 'ARM64' }
    $buildDir  = Join-Path $env:TEMP "ma-build-$TargetArch"
    $outDir    = Join-Path $ToolsDir "output\$rid"

    Write-Host ""
    Write-Host "=============================================================================="
    Write-Host " BUILD $rid"
    Write-Host "=============================================================================="
    Write-Host "  miniaudio  : $($Pins['MINIAUDIO_VERSION']) ($($Pins['MINIAUDIO_COMMIT']))"
    Write-Host "  stb_vorbis : $($Pins['STB_VORBIS_VERSION']) ($($Pins['STB_VORBIS_COMMIT']))"
    Write-Host ""

    if (Test-Path $buildDir) { Remove-Item -Recurse -Force $buildDir }

    Write-Host "--- building ---"
    # CMakeLists.txt in native\miniaudio is the single source of truth for compiler settings;
    # this script passes only the architecture and the build type.
    & cmake -S $NativeSrc -B $buildDir -A $cmakeArch -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed for $rid" }
    & cmake --build $buildDir --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed for $rid" }

    $built = Join-Path $buildDir "Release\$LibName"
    if (-not (Test-Path $built)) { throw "expected $built, which was not produced" }

    Write-Host ""
    Write-Host "--- verifying ---"
    if (-not (Invoke-Verification -DllPath $built -TargetArch $TargetArch)) {
        Write-Host ""
        Write-Host "VERIFICATION FAILED - nothing written to output\. Fix the build before adopting." -ForegroundColor Red
        exit 1
    }

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Copy-Item $built (Join-Path $outDir $LibName) -Force

    $sha  = (Get-FileHash (Join-Path $outDir $LibName) -Algorithm SHA256).Hash.ToLower()
    $size = (Get-Item (Join-Path $outDir $LibName)).Length
    $deps = & $DumpBin.FullName /nologo /dependents (Join-Path $outDir $LibName) |
        ForEach-Object { if ($_ -match '^\s+(\S+\.dll)$') { $Matches[1] } }

    @"
codebrix_miniaudio - build information
==============================================================================
RID            : $rid
Built          : $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')) UTC
Built by       : tools\build_native_libraries\windows\build.ps1 (host build)
Host           : $([System.Environment]::OSVersion.VersionString) ($env:PROCESSOR_ARCHITECTURE)
Visual Studio  : $VsPath
CMake          : $((cmake --version | Select-Object -First 1))
Build type     : Release

Sources (all vendored in-repo, nothing fetched at build time)
------------------------------------------------------------------------------
miniaudio      : $($Pins['MINIAUDIO_VERSION'])  (mackron/miniaudio @ $($Pins['MINIAUDIO_COMMIT']))
stb_vorbis     : $($Pins['STB_VORBIS_VERSION'])  (nothings/stb @ $($Pins['STB_VORBIS_COMMIT']))
wrapper        : native/miniaudio/library.c + library.h

Result
------------------------------------------------------------------------------
File           : $LibName
Size           : $size bytes
SHA256         : $sha
Dynamic deps   : $($deps -join ' ')
Codecs         : WAV, MP3, FLAC, Ogg Vorbis
"@ | Set-Content -Path (Join-Path $outDir 'build-info.txt') -Encoding UTF8

    Write-Host ""
    Write-Host "--- done ---"
    Write-Host "  $outDir\$LibName"
    Write-Host "  sha256 $sha"
}

if ($Arch -eq 'all') { Build-Arch 'x64'; Build-Arch 'arm64' } else { Build-Arch $Arch }

Write-Host ""
Write-Host "=============================================================================="
Write-Host " Outputs are in tools\build_native_libraries\output\<rid>\"
Write-Host " To adopt them, follow ADOPTING A BUILT BINARY in ..\README.txt."
Write-Host "=============================================================================="
