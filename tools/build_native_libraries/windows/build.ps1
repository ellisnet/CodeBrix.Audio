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

PREREQUISITES - installed by YOU. This script never installs anything.

Everything is checked UP FRONT, for every architecture you asked for, before a single file is
compiled. If anything is missing the script lists ALL of the problems at once, prints the exact
command that fixes each one, and stops - so you learn about a missing ARM64 toolset before
waiting through a successful x64 build, not after.

  1. Visual Studio 2022 or newer, workload "Desktop development with C++".
  2. The ARM64/ARM64EC build tools component - needed ONLY for win-arm64.
  3. CMake 3.26+. A standalone install works, and so does the copy bundled with Visual Studio -
     this script finds the bundled one automatically even when it is not on PATH.

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

$Requested = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }

# ------------------------------------------------------------------------------------------------
# Problem collection. Prerequisite failures accumulate here instead of exiting at the first one,
# so a single run tells you everything you need to install.
# ------------------------------------------------------------------------------------------------
$Problems = New-Object System.Collections.Generic.List[object]

function Add-Problem {
    param([string]$What, [string]$Fix)
    $Problems.Add([pscustomobject]@{ What = $What; Fix = $Fix })
}

function Stop-IfProblems {
    if ($Problems.Count -eq 0) { return }
    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Red
    Write-Host " MISSING PREREQUISITES - nothing was built" -ForegroundColor Red
    Write-Host "==============================================================================" -ForegroundColor Red
    $n = 1
    foreach ($p in $Problems) {
        Write-Host ""
        Write-Host "  $n. $($p.What)" -ForegroundColor Red
        Write-Host ""
        foreach ($line in ($p.Fix -split "`n")) { Write-Host "       $line" -ForegroundColor Yellow }
        $n++
    }
    Write-Host ""
    Write-Host "  This script installs nothing - that is your decision, not the script's."
    Write-Host "  See ..\README.txt (PREREQUISITES) for the complete list."
    Write-Host ""
    exit 1
}

# ------------------------------------------------------------------------------------------------
# pins.env is the single source of truth for every version/commit pin.
#
# NOTE FOR ANYONE WHO FINDS IT MISSING: the repository root .gitignore contains a blanket '*.env'
# rule which has silently excluded this file from a commit before. If pins.env is absent after a
# fresh clone, that rule is the first thing to check - the file is supposed to be tracked.
# ------------------------------------------------------------------------------------------------
$PinsFile = Join-Path $ToolsDir 'pins.env'
if (-not (Test-Path $PinsFile)) {
    Write-Host ""
    Write-Host "ERROR: pins.env was not found at $PinsFile" -ForegroundColor Red
    Write-Host ""
    Write-Host "  All four build scripts read it for the miniaudio / stb_vorbis pins, so none of"
    Write-Host "  them can run without it. It is meant to be committed; if it vanished after a"
    Write-Host "  clone, the root .gitignore '*.env' rule is the likely cause:"
    Write-Host ""
    Write-Host "    git check-ignore -v tools/build_native_libraries/pins.env" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

$Pins = @{}
Get-Content $PinsFile | ForEach-Object {
    if ($_ -match '^\s*([A-Z0-9_]+)=(.*)$') { $Pins[$Matches[1]] = $Matches[2].Trim() }
}

$RequiredPins = @('MINIAUDIO_VERSION', 'MINIAUDIO_COMMIT',
                  'STB_VORBIS_VERSION', 'STB_VORBIS_COMMIT', 'SMOKE_TEST_OGG')
$MissingPins = @($RequiredPins | Where-Object { -not $Pins.ContainsKey($_) -or -not $Pins[$_] })
if ($MissingPins.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: pins.env is missing required keys: $($MissingPins -join ', ')" -ForegroundColor Red
    Write-Host "  Expected KEY=VALUE lines in $PinsFile"
    Write-Host ""
    exit 1
}

# ------------------------------------------------------------------------------------------------
# Visual Studio. Without it nothing else is worth checking, so this one stops immediately.
# ------------------------------------------------------------------------------------------------
$VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$VsSetup = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'

$VsPath = $null
if (Test-Path $VsWhere) {
    $VsPath = & $VsWhere -latest -products * `
                         -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                         -property installationPath | Select-Object -First 1
}
if (-not $VsPath) {
    Add-Problem "Visual Studio with the C++ toolset was not found." `
                ("winget install Microsoft.VisualStudio.2022.Community`n" +
                 "(or add the 'Desktop development with C++' workload to an existing install)")
    Stop-IfProblems
}

$HostArch     = $env:PROCESSOR_ARCHITECTURE
$HostToolsDir = if ($HostArch -eq 'ARM64') { 'HostARM64' } else { 'Hostx64' }

# ------------------------------------------------------------------------------------------------
# CMake. Visual Studio ships a perfectly good CMake but does not put it on PATH, which used to
# make this script stop with "cmake is not on PATH" on machines that could build just fine.
# Look in PATH first, then the VS copy, then the standard standalone locations.
# ------------------------------------------------------------------------------------------------
function Resolve-CMake {
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $bundled = Join-Path $VsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if (Test-Path $bundled) { return $bundled }

    foreach ($p in @((Join-Path $env:ProgramFiles 'CMake\bin\cmake.exe'),
                     (Join-Path ${env:ProgramFiles(x86)} 'CMake\bin\cmake.exe'))) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

$CMakeExe         = Resolve-CMake
$CMakeVersionLine = '(not found)'
if ($CMakeExe) {
    $CMakeVersionLine = & $CMakeExe --version | Select-Object -First 1
    if ($CMakeVersionLine -match '(\d+)\.(\d+)\.(\d+)') {
        $found = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
        if ($found -lt [version]'3.26') {
            Add-Problem "CMake $found is too old - native\miniaudio\CMakeLists.txt requires 3.26 or newer." `
                        "winget install Kitware.CMake"
        }
    }
} else {
    Add-Problem "CMake was not found on PATH, inside the Visual Studio installation, or in the standard install locations." `
                "winget install Kitware.CMake"
}

# ------------------------------------------------------------------------------------------------
# dumpbin - the export and dependency checks are built on it.
# ------------------------------------------------------------------------------------------------
$DumpBin = Get-ChildItem -Path (Join-Path $VsPath 'VC\Tools\MSVC') -Recurse -Filter 'dumpbin.exe' `
                         -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match [regex]::Escape("\$HostToolsDir\") } |
           Select-Object -First 1 -ExpandProperty FullName
if (-not $DumpBin) {
    Add-Problem "dumpbin.exe was not found under $VsPath (the export and dependency checks need it)." `
                "Repair the Visual Studio installation, or re-add 'Desktop development with C++'."
}

# ------------------------------------------------------------------------------------------------
# Per-architecture toolchain check.
#
# THIS DELIBERATELY CHECKS THE FILESYSTEM OF $VsPath, NOT vswhere -requires.
#
# The reason is instance conflation. A machine can easily have several Visual Studio instances -
# e.g. VS 18 Professional plus a VS 2022 Build Tools install - and `vswhere -requires <component>`
# searches ALL of them. So it will happily answer "yes, that component is installed" while
# meaning a DIFFERENT installation from the one $VsPath points at. Asking it about ARM64 on a
# machine like that returns the Build Tools path, which says nothing about whether the VS this
# script is driving can target ARM64.
#
# Believing it produces a raw, near-unreadable failure deep inside cmake's configure step:
#
#     error : The BaseOutputPath/OutputPath property is not set for project 'VCTargetsPath.vcxproj'
#             ... Configuration='Debug'  Platform='ARM64'
#
# So the two things that actually have to exist, in the instance actually being used, are checked
# directly. If another instance does have them, that is reported as a hint - it is useful to know,
# but it is not a substitute for installing them where they are needed.
# ------------------------------------------------------------------------------------------------
function Get-AllVsInstancePaths {
    if (-not (Test-Path $VsWhere)) { return @() }
    @(& $VsWhere -products * -all -property installationPath 2>$null) | Where-Object { $_ }
}
function Get-VcPlatformNames {
    Get-ChildItem -Path (Join-Path $VsPath 'MSBuild\Microsoft\VC') -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'Platforms') -Directory -ErrorAction SilentlyContinue } |
        ForEach-Object { $_.Name } |
        Sort-Object -Unique
}

function Get-AvailableTargetCompilers {
    Get-ChildItem -Path (Join-Path $VsPath "VC\Tools\MSVC\*\bin\$HostToolsDir\*\cl.exe") -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Directory.Name } |
        Sort-Object -Unique
}

$VcPlatforms      = @(Get-VcPlatformNames)
$TargetCompilers  = @(Get-AvailableTargetCompilers)

function Test-ArchToolchain {
    param([string]$TargetArch)

    $platform = if ($TargetArch -eq 'x64') { 'x64' }   else { 'ARM64' }
    $clDir    = if ($TargetArch -eq 'x64') { 'x64' }   else { 'arm64' }

    $havePlatform = $VcPlatforms -contains $platform
    $haveCompiler = $TargetCompilers -contains $clDir
    if ($havePlatform -and $haveCompiler) { return $true }

    $missing = @()
    if (-not $haveCompiler) { $missing += "the $clDir compiler ($HostToolsDir -> $clDir\cl.exe)" }
    if (-not $havePlatform) { $missing += "the MSBuild '$platform' platform targets" }

    $component = if ($TargetArch -eq 'x64') {
        '--add Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
    } else {
        '--add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --add Microsoft.VisualStudio.Component.VC.Tools.ARM64EC'
    }

    # If a DIFFERENT Visual Studio instance on this machine does have the compiler, say so. It
    # explains why vswhere -requires would have claimed the component was installed, and it saves
    # the next person the same confusion.
    $elsewhere = @(Get-AllVsInstancePaths | Where-Object { $_ -ne $VsPath } | Where-Object {
        Get-ChildItem -Path (Join-Path $_ "VC\Tools\MSVC\*\bin\Host*\$clDir\cl.exe") -ErrorAction SilentlyContinue
    })
    $note = ''
    if ($elsewhere.Count -gt 0) {
        $note = "`n     Note: another Visual Studio instance DOES have a $clDir compiler:" +
                ($elsewhere | ForEach-Object { "`n       $_" }) +
                "`n     That is why `vswhere -requires` reports the component as installed. It has to be" +
                "`n     added to the instance above, which is the one this script builds with."
    }

    Add-Problem ("win-$TargetArch cannot be built: $VsPath is missing " + ($missing -join ' and ') + '.' + $note) `
                ("`"$VsSetup`" modify --installPath `"$VsPath`" $component`n" +
                 "or: Visual Studio Installer -> Modify -> Individual components ->`n" +
                 "    tick 'MSVC Build Tools for ARM64/ARM64EC (Latest)'`n" +
                 "    (VS 2022 names it 'MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools')")
    return $false
}

# ------------------------------------------------------------------------------------------------
# The decode smoke-test input. Only fatal when at least one requested architecture can actually
# run on this host - a pure cross-compile could never have run the smoke test anyway.
# ------------------------------------------------------------------------------------------------
function Test-CanRunNatively {
    param([string]$TargetArch)
    ($TargetArch -eq 'x64'   -and $HostArch -eq 'AMD64') -or
    ($TargetArch -eq 'arm64' -and $HostArch -eq 'ARM64')
}

$OggPath = Join-Path $RepoRoot ($Pins['SMOKE_TEST_OGG'] -replace '/', '\')
$HaveOgg = Test-Path $OggPath
$AnyRunnable = @($Requested | Where-Object { Test-CanRunNatively $_ }).Count -gt 0

if (-not $HaveOgg -and $AnyRunnable) {
    Add-Problem "The decode smoke-test input is missing: $OggPath" `
                ("cd `"$RepoRoot\tools\make_test_fixtures`" && ./make_fixtures.sh`n" +
                 "(needs bash + ffmpeg; it does not install anything either)")
}

# ------------------------------------------------------------------------------------------------
# Report what was found, then stop if anything is missing.
# ------------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "=============================================================================="
Write-Host " ENVIRONMENT"
Write-Host "=============================================================================="
Write-Host "  Repository        : $RepoRoot"
Write-Host "  Host              : $HostArch (MSVC host tools: $HostToolsDir)"
Write-Host "  Visual Studio     : $VsPath"
Write-Host "  Target compilers  : $(if ($TargetCompilers.Count) { $TargetCompilers -join ', ' } else { '(none found)' })"
Write-Host "  MSBuild platforms : $(if ($VcPlatforms.Count) { $VcPlatforms -join ', ' } else { '(none found)' })"
Write-Host "  CMake             : $CMakeVersionLine"
Write-Host "                      $(if ($CMakeExe) { $CMakeExe } else { '' })"
Write-Host "  dumpbin           : $(if ($DumpBin) { $DumpBin } else { '(not found)' })"
Write-Host "  Smoke-test input  : $(if ($HaveOgg) { $OggPath } else { "MISSING - $OggPath" })"
Write-Host "  Requested         : $(($Requested | ForEach-Object { "win-$_" }) -join ', ')"
Write-Host ""

foreach ($a in $Requested) {
    $ok = Test-ArchToolchain $a
    Write-Host "  win-$($a.PadRight(5)) toolchain : $(if ($ok) { 'ready' } else { 'NOT AVAILABLE' })" `
               -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

Stop-IfProblems

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

    function Pass($m) { Write-Host "  [ok] $m" }
    function Fail($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:verifyFailed = $true }
    $script:verifyFailed = $false

    # 1 + 2. Exports and codec coverage.
    $exportDump = & $DumpBin /nologo /exports $DllPath
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
    $deps = & $DumpBin /nologo /dependents $DllPath |
        ForEach-Object { if ($_ -match '^\s+(\S+\.dll)$') { $Matches[1] } }
    $forbidden = $deps | Where-Object { $_ -match 'vorbis|ogg|FLAC|libwinpthread|libgcc' }
    if ($forbidden) { Fail "unexpected dynamic dependency: $($forbidden -join ' ')" }
    else            { Pass "dependencies are system-only: $($deps -join ' ')" }

    # 4. Decode smoke test - only meaningful when the DLL matches the host architecture.
    #    A cross-compiled ARM64 DLL cannot be loaded by an x64 process, so it is checked
    #    statically above and exercised on ARM64 hardware instead. This is stated, never silent.
    if (Test-CanRunNatively $TargetArch) {
        if (-not $HaveOgg) {
            Fail "smoke-test input missing: $OggPath"
        } else {
            $smokeSrc = Join-Path $ToolsDir 'smoke_test.c'
            $smokeDir = Join-Path $env:TEMP 'codebrix_smoke'
            New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
            $vcArch = if ($TargetArch -eq 'x64') { 'x64' } else { 'arm64' }
            $vcVarsAll = Join-Path $VsPath 'VC\Auxiliary\Build\vcvarsall.bat'

            # smoke_test.exe is invoked as .\smoke_test.exe deliberately: when
            # NoDefaultCurrentDirectoryInExePath is set (a common security hardening), cmd.exe
            # does not search the current directory, and a bare smoke_test.exe fails with
            # "is not recognized" even though it was just compiled into that directory.
            $cmd = "call `"$vcVarsAll`" $vcArch >nul && cd /d `"$smokeDir`" && " +
                   "cl /nologo /O2 `"$smokeSrc`" >nul && .\smoke_test.exe `"$DllPath`" `"$OggPath`""
            $output = & cmd.exe /c $cmd 2>&1
            # vcvarsall.bat in VS 18 emits a harmless "'vswhere.exe' is not recognized" line on
            # some installs; it still initialises the environment correctly, so it is dropped here
            # rather than left to look like a build failure.
            $output | Where-Object { $_ -notmatch "vswhere\.exe' is not recognized|^operable program" } |
                      ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -eq 0) { Pass 'smoke test' } else { Fail 'smoke test' }
        }
    } else {
        Write-Host "  [--] decode smoke test not run: a $TargetArch DLL cannot be loaded by this"
        Write-Host "       $HostArch process. The static checks above still applied; run the"
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
    & $CMakeExe -S $NativeSrc -B $buildDir -A $cmakeArch -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed for $rid" }
    & $CMakeExe --build $buildDir --config Release
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
    $deps = & $DumpBin /nologo /dependents (Join-Path $outDir $LibName) |
        ForEach-Object { if ($_ -match '^\s+(\S+\.dll)$') { $Matches[1] } }

    @"
codebrix_miniaudio - build information
==============================================================================
RID            : $rid
Built          : $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')) UTC
Built by       : tools\build_native_libraries\windows\build.ps1 (host build)
Host           : $([System.Environment]::OSVersion.VersionString) ($HostArch)
Visual Studio  : $VsPath
CMake          : $CMakeVersionLine
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

foreach ($a in $Requested) { Build-Arch $a }

Write-Host ""
Write-Host "=============================================================================="
Write-Host " Outputs are in tools\build_native_libraries\output\<rid>\"
Write-Host " To adopt them, follow ADOPTING A BUILT BINARY in ..\README.txt."
Write-Host "=============================================================================="
