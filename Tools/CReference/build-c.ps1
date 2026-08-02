#Requires -Version 7.3
<#
.SYNOPSIS
    Builds Astrodienst's own C with MSVC, so the port can be compared against it.

.DESCRIPTION
    Produces five artifacts under -OutputDir:

        build-2.10.03/libswe-2.10.03.lib   from external/swisseph            (the port's target)
        build-2.08/libswe-2.08.lib         from external/pyswisseph-2.08     (the port's current version)
        swetest.exe                        from external/swisseph/swetest.c  (linked against 2.10.03)
        sedump-2.08.exe                    from Tools/CReference/sedump.c    (linked against 2.08)
        toolchain.txt                      compiler, flags and provenance the four artifacts above were built with

    Two libraries, not one, because they answer different questions. Comparing the port
    against 2.08 C isolates transliteration defects; comparing it against 2.10.03 C is the
    porting work queue. Conflating the two is how a porting bug gets filed as version drift.

    sedump-2.08.exe is the same driver scripts/run-oracle-dump.ps1 already builds against
    libswe-2.10.03.lib, compiled here a second time against libswe-2.08.lib instead. It is built
    here, not by run-oracle-dump.ps1, specifically so it is compiled against
    external/pyswisseph-2.08's own swephexp.h rather than external/swisseph's -- this script
    already has both source trees and the toolchain set up, and mixing a 2.10.03 header with the
    2.08 library (or vice versa) is a real hazard: the struct layouts and prototypes usually
    agree, so a mismatched pairing would still link and run, and would silently produce wrong
    values rather than fail to build. Every function sedump.c calls unconditionally (swe_calc,
    swe_calc_ut, swe_houses, swe_houses_armc, swe_close, swe_set_ephe_path, swe_set_topo,
    swe_set_sid_mode, swe_get_planet_name, swe_fixstar, swe_fixstar_ut, swe_fixstar2,
    swe_fixstar2_ut, swe_fixstar_mag, swe_fixstar2_mag, swe_get_ayanamsa, swe_get_ayanamsa_ex,
    swe_get_ayanamsa_ex_ut, swe_get_ayanamsa_ut, swe_houses_ex, swe_sidtime, swe_azalt,
    swe_house_name, swe_nod_aps_ut) is declared in external/pyswisseph-2.08/swephexp.h with the
    same signature it has in 2.10.03, and swe_fixstar2/swe_fixstar2_ut/swe_fixstar2_mag -- the
    ones most likely to be 2.10-only, since Astrodienst kept extending the fixed-star API across
    releases -- are all implemented in external/pyswisseph-2.08/sweph.c, not just declared.

    Four exceptions, all guarded the same way: swe_solcross, swe_mooncross, swe_mooncross_node,
    swe_helio_cross and their _ut variants; swe_houses_ex2/swe_houses_armc_ex2; swe_calc_pctr; and
    swe_get_current_file_data do not exist in 2.08 at all (verified: zero matches anywhere under
    external/pyswisseph-2.08/ for any of the four), so sedump.c guards every call to each group
    behind its own #ifdef SWISSEPH_HAS_CROSSING / SWISSEPH_HAS_HOUSES_EX2 /
    SWISSEPH_HAS_CALC_PCTR / SWISSEPH_HAS_GET_CURRENT_FILE_DATA -- see that file's own top-of-file
    comment for the full reasoning. scripts/run-oracle-dump.ps1 defines all four
    macros when it compiles sedump.exe against 2.10.03; this script's own $compile command below
    for sedump-2.08.exe deliberately defines none of the four, so the 2.08 build takes sedump.c's
    #else branch for all four groups (a fixed sentinel row per case) without needing a flag added
    here.

    The compiler identity is part of the result, not an incidental detail: reference values
    depend on it. Every run writes toolchain.txt recording the compiler and linker versions, the
    exact flags, the source commit, and the sha256 of every artifact, and a comparison against
    these artifacts is only valid for the toolchain recorded there.

    Flags are this repo's own choice, not Astrodienst's: /O2 /fp:precise /MD.
    external/swisseph/Makefile ships `-g -Wall -fPIC`, no optimization flag and no fp mode --
    Astrodienst's own reference build is unoptimized debug code, which is not what a bit-exact
    oracle needs. /fp:precise performs no reassociation and, from Visual Studio 2022 onward,
    defaults to fp_contract(off) (earlier versions defaulted to on). Rather than trust that
    default, the build disassembles libswe-2.10.03.lib, libswe-2.08.lib and swetest_patched.obj
    and fails if any of the three contains an FMA instruction, and separately compiles a probe
    whose codegen only matches /fp:precise when reassociation is genuinely off (see
    Assert-NoFmaContraction and Assert-FpPrecise below -- the probe was verified against this
    toolchain before being trusted, not assumed to work). sedump.obj and the probe object itself
    are not scanned this way; see the comment at sedump.obj's own build step for why.

    The CRT is linked /MD, not /MT, because the whole point of this build is bit-exactness
    against CoreCLR, and CoreCLR's own arithmetic runs through the shared CRT. On Windows x64,
    Math.Sin/Cos/Tan/Atan2/Pow/Exp/Log are internal-call FCALLs whose bodies are `return sin(x);`
    and so on, from the C runtime, and CoreCLR binds ucrtbase.dll for that: the one DLL in
    System32, serviced by Windows Update, not a copy pinned to whichever SDK happened to be
    installed when this repo was built. /MD makes cl.exe bind that same ucrtbase.dll, so both
    sides of the comparison run through one implementation. /MT would link libucrt.lib instead, a
    static copy frozen at the installed SDK's version -- if ucrtbase.dll is later serviced, the
    /MT build stays put while .NET moves with the DLL, and a correct port starts failing the
    oracle for a reason that has nothing to do with sweph.c or the C# it is compared against.
    Measured on this machine: /MT and /MD produce bit-identical output today across 200 values
    spanning sin/cos/tan/atan/exp/log/pow/atan2/sqrt/fmod, and so does MSVC /MD against .NET on
    the same 200 values. /MD is not fixing a difference that exists now; it is what keeps the two
    sides on the same footing once the shared CRT is next serviced. toolchain.txt's
    ucrtbase_dll_version row exists so that servicing shows up there instead of nowhere.

    The final, linked swetest.exe is still not disassembled for FMA -- the object file it links
    from is, before the link step runs, because that object file is the code this build actually
    compiled (see swetest_patched.obj below). That distinction mattered concretely under the
    former /MT link: Microsoft's own prebuilt static UCRT lib was pulled directly into the .exe,
    and part of what it contributed -- printf's floating-point-to-string conversion -- contained
    FMA regardless of any flag this script passed, verified empirically at the time: a one-line
    `printf("%.9f\n", x)` compiled /MT /fp:precise disassembled to 9 FMA instructions from the
    UCRT alone, 0 under /MD with identical source and flags. Under /MD the linked .exe no longer
    carries UCRT code, but the object file remains the simpler and more directly justified thing
    to scan, so the check still runs there.

.PARAMETER OutputDir
    Where to write the artifacts. Defaults to external/.c-reference, which is gitignored: these
    are build outputs of vendored source, not source themselves. Must resolve to a path
    .gitignore actually excludes -- git check-ignore is asked directly, rather than assuming
    anything under external/ qualifies, because .gitignore excludes only three specific
    subdirectories there (external/pyswisseph-2.08/, external/.pyswisseph-2.08-download/,
    external/.c-reference/), not external/ itself. This script refuses to write multi-megabyte
    binaries anywhere .gitignore would not catch.

.NOTES
    setest is deliberately not built. Its Makefile needs generated_tests.c, which upstream does
    not commit and which is produced from testsuite.m4 by m4 plus perl. Nothing here needs it:
    libswe and swetest compile from plain .c with no generator step. The build is validated
    instead by scripts/validate-c-reference.ps1, which checks this build against pyswisseph, an
    independently packaged build of the same 2.10.03 libswe (see that script's own header for
    what "independent" does and does not mean here).

    Both this script and scripts/validate-c-reference.ps1 now run in CI. .github/workflows/
    oracle.yml's own header comment predicted this note would go stale ("Tools/CReference/
    build-c.ps1 and scripts/validate-c-reference.ps1 each say so in their own .NOTES section,
    written when nothing here exercised them. This workflow is what makes that statement go
    stale."), and oracle.yml is that workflow. Its c-reference-validate job runs this script and
    then scripts/validate-c-reference.ps1, in that order; oracle-dump and swetest-diff each also
    run this script (they need the C reference build but not the pyswisseph cross-check).
    windows-latest carries a preinstalled Visual Studio with the C++ toolset, and a Python with
    pyswisseph is installed as its own step in c-reference-validate, so neither local-tooling
    requirement below is a reason to still call this a local-only pair. Running both by hand
    after a change that could affect the C reference build remains useful for faster iteration
    than waiting on a CI run, just no longer the only way either one runs.
#>
[CmdletBinding()]
param(
    [string] $OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# $PSNativeCommandUseErrorActionPreference defaults to $false on pwsh 7.6.3 (the version this was
# measured against), so this line changes nothing today. It is future-proofing: if that default
# ever flips to $true, a non-zero exit from a native command (cl, link, dumpbin, git, all invoked
# below) would throw immediately under $ErrorActionPreference = 'Stop', before the diagnostic
# Write-Host lines that print the tool's own output ever run, and the failure would be reported
# with no evidence of what failed. Three-arg Join-Path (used a few lines below) needs PS6+; this
# variable needs PS7.3+ -- both are why #Requires -Version 7.3 is pinned.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'external/.c-reference' }

# Resolve to an absolute path and require .gitignore to actually exclude it. .gitignore excludes
# three specific subdirectories under external/ (pyswisseph-2.08/, .pyswisseph-2.08-download/,
# .c-reference/), not external/ itself, so a relative or arbitrary -OutputDir landing anywhere
# else under external/ -- e.g. external/scratch -- would pass a plain "is it under external/"
# check and still leave multi-megabyte .lib/.exe build products somewhere git will happily track
# them. git check-ignore is asked directly instead of re-deriving the rule here, so this check
# tracks .gitignore's actual content rather than a hardcoded assumption about it.
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
# Probe a path *inside* the directory rather than the directory itself. The .gitignore entries
# for these three locations end in a slash, so they match directories only, and git can only
# apply a directory-only pattern to a path it can see is a directory. On a fresh clone the
# output directory does not exist yet, so check-ignore reads it as a file, no pattern matches,
# and the guard rejects its own default. That is what happened on the first CI run: all three
# jobs that build the C reference failed here, on a checkout where external/.c-reference/ had
# never been created. A path under the directory is unambiguous whether or not anything exists.
& git -C $repoRoot check-ignore --quiet -- (Join-Path $OutputDir '.probe')
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: -OutputDir resolves to '$OutputDir', which .gitignore does not exclude." -ForegroundColor Red
    Write-Host '.gitignore excludes external/pyswisseph-2.08/, external/.pyswisseph-2.08-download/ and external/.c-reference/ -- not external/ itself. Refusing to write build products anywhere else.'
    exit 1
}

$src210 = Join-Path $repoRoot 'external/swisseph'
$src208 = Join-Path $repoRoot 'external/pyswisseph-2.08'
$manifest208Path = Join-Path $repoRoot 'scripts/pyswisseph-2.08.manifest.tsv'

# The 2.10.03 translation units, from external/swisseph/Makefile. swetest.c is a program, not
# part of the library, and is built separately below.
$LibSources210 = @(
    'swedate.c', 'swehel.c', 'swehouse.c', 'swejpl.c', 'swemmoon.c',
    'swemplan.c', 'sweph.c', 'swephlib.c', 'swecl.c'
)

# The 2.08 translation units, from external/pyswisseph-2.08/Makefile:26-27 (the SWEOBJ list).
# 2.08 splits swepcalc.c and swepdate.c out as their own files; 2.10.03 folded both back into
# sweph.c/swephlib.c. The two trees do not share one translation-unit set, so building 2.08 from
# the 2.10.03 list silently drops two files' worth of code from libswe-2.08.lib.
$LibSources208 = @(
    'swedate.c', 'swehel.c', 'swehouse.c', 'swejpl.c', 'swemmoon.c',
    'swemplan.c', 'sweph.c', 'swephlib.c', 'swecl.c', 'swepcalc.c', 'swepdate.c'
)

function Fail($message) {
    # Thrown, not exited: a bare `exit` here would skip the try/finally further down that cleans
    # up the temp build directory on failure. See the top-level try/catch/finally.
    throw $message
}

# ---------------------------------------------------------------------------------------
# Locate the toolchain
# ---------------------------------------------------------------------------------------

function Get-VcVarsPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        Fail "vswhere.exe not found at $vswhere. Visual Studio with the C++ toolset is required."
    }
    $install = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if (-not $install) {
        Fail 'No Visual Studio installation with the x64 C++ toolset was found.'
    }
    $vcvars = Join-Path $install 'VC/Auxiliary/Build/vcvars64.bat'
    if (-not (Test-Path -LiteralPath $vcvars)) { Fail "vcvars64.bat not found under $install." }
    return $vcvars
}

# Runs a command inside the MSVC x64 environment. vcvars64.bat only sets variables for the
# shell it runs in, so the compiler invocation has to happen in that same cmd process.
function Invoke-InVcEnv {
    param([string] $VcVars, [string] $WorkingDir, [string] $Command)
    $full = "`"$VcVars`" >nul 2>&1 && cd /d `"$WorkingDir`" && $Command"
    $output = cmd /c $full 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Reads an environment variable vcvars64.bat sets, e.g. WindowsSDKVersion. cmd expands %VAR%
# at parse time, before vcvars64.bat has run and set it, so a plain `echo %WindowsSDKVersion%`
# on the same command line prints the literal, unexpanded text -- not the value, and not an
# error either, so a naive version of this silently records the wrong string. /v:on plus !VAR!
# defers expansion to execution time, after vcvars64.bat has run. Verified against this
# toolchain: %WindowsSDKVersion% printed literally; !WindowsSDKVersion! printed 10.0.26100.0\.
function Get-VcVarsEnvValue {
    param([string] $VcVars, [string] $VarName)
    # `&&` and not `&`: `&` runs echo regardless of whether vcvars64.bat succeeded, so a vcvars
    # failure would still print !VarName! (unexpanded, since the batch file never ran) instead of
    # stopping there. `&&` only runs echo after vcvars64.bat exits 0.
    $full = "`"$VcVars`" >nul 2>&1 && echo !$VarName!"
    $output = cmd /v:on /c $full 2>&1
    return ($output | Select-Object -Last 1).ToString().Trim()
}

# A real WindowsSDKVersion looks like 10.0.26100.0\ (vcvars64.bat sets it with a trailing
# backslash; verified against this toolchain). If cmd's delayed expansion never actually kicks in
# -- /v:on missing, or the variable not yet set at echo time -- !WindowsSDKVersion! comes back as
# that literal text, unexpanded. That string is non-empty, so `if (-not $windowsSdkVersion)` let
# it straight through and toolchain.txt recorded "!WindowsSDKVersion!" as if it were a real
# version. This checks the shape instead of just checking for emptiness.
function Test-WindowsSdkVersionShape {
    param([string] $Value)
    return $Value -match '^\d+(\.\d+){2,3}\\?$'
}

function Get-Sha256Hex {
    param([string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# toolchain.txt's "arch" row used to just print "x64", which names the target platform, not the
# ISA baseline the compiler actually generated for. $commonFlags carries no /arch flag today, so
# the effective baseline is x64's own default (SSE2, no FMA encoding at all); if /arch is ever
# added to $commonFlags this reads it back out instead of the row silently going stale.
function Get-ArchBaseline {
    param([string] $Flags)
    if ($Flags -match '/arch:(\S+)') { return $Matches[1] }
    return 'SSE2 (x64 default -- no /arch flag among the flags above)'
}

# ---------------------------------------------------------------------------------------
# swetest.c does not compile as shipped in v2.10.3final
# ---------------------------------------------------------------------------------------

<#
    Two defects, both upstream, both in the tagged release:

      1. spmoon is written at swetest.c:1139-1140 and read at :1621, and is declared nowhere
         in the entire source tree. It arrived with -xv (planetary moons), new in 2.10, and
         its declaration did not. This is a hard compile error for any C compiler on any
         platform, so the shipped swetest.c cannot ever have been built from this tag.

      2. hostname is declared under `#if HPUNIX` (:826-828) but gethostname() is called
         unconditionally at :1282. The call only exists to raise line_limit on a host named
         "as80", which is Astrodienst's own machine, so guarding it changes nothing anywhere
         else.

    The fixes are applied to a copy. The submodule stays pristine -- gen-delta.ps1 asserts the
    gitlink matches the pinned commit, and a modified working tree would break the delta the
    whole port is derived from.

    Every replacement asserts both that it found exactly one place to anchor to, and that the
    defect it is patching is still present -- so a future upstream tag that declares spmoon (or
    guards gethostname) itself fails loudly here instead of compiling something other than what
    this comment describes.
#>
function New-PatchedSwetestSource {
    param([string] $SourcePath, [string] $DestinationPath)

    # Strict UTF-8, not the default replacement-char decoder: ReadAllText(path) silently maps
    # any invalid byte to U+FFFD instead of failing, which would let a future encoding change in
    # swetest.c corrupt the patched source with no error anywhere in this script.
    $text = [System.IO.File]::ReadAllText($SourcePath, [System.Text.UTF8Encoding]::new($false, $true))

    $spmoonDeclPattern = '(?m)^\s*static\s+char\s+spmoon\b'
    if ($text -match $spmoonDeclPattern) {
        Fail 'swetest.c: spmoon is already declared. The upstream compile defect this patch exists for may no longer apply -- check whether this tag declares it, and drop this patch if so instead of applying it on top.'
    }

    $sastnoDecl = 'static char sastno[AS_MAXCH] = "433";'
    $sastnoCount = ([regex]::Matches($text, [regex]::Escape($sastnoDecl))).Count
    if ($sastnoCount -ne 1) {
        Fail "swetest.c: expected exactly one sastno declaration to anchor the spmoon fix to, found $sastnoCount."
    }
    # Same shape as its siblings sastno ("433", a real asteroid number) and shyp ("1", a real
    # hypothetical-body number): file-scope, AS_MAXCH, and a real id rather than blank.
    # swetest.c:1619-1621 runs `ipl = atoi(spmoon)` unconditionally whenever 'v' appears in
    # plsel, with no offset added -- unlike the 's' and 'z' selectors, which add
    # SE_AST_OFFSET/SE_FICT_OFFSET_1 inline, so spmoon has to hold a full id already. A blank
    # default made atoi("") == 0 == SE_SUN, so `swetest -pv` with no `-xv` silently printed the
    # Sun labelled as a planetary moon -- a defect in a reference artifact this build feeds an
    # oracle. "9501" is Io: SE_PLMOON_OFFSET (9000) plus the host planet's number times 100, so
    # 94xx is a Mars moon, 95xx a Jupiter moon (Io is Jupiter's first), 96xx a Saturn moon, and
    # so on -- 9001 is not a moon of anything. Confirmed against upstream's own later fix for
    # this same missing-declaration defect: swetest.c on the aloistr/swisseph master branch (past
    # the v2.10.3final tag this build otherwise pins to) declares
    # `static char spmoon[AS_MAXCH] = "9501";  // Jupiter Moon Io`, matching the usage text's own
    # `v -xv9501 Io/Jupiter` example. This patch adopts that value rather than inventing one. This
    # repo's ephemeris checkout does not carry the sepm9*.se1 files planetary-moon calc needs (see
    # CONTRIBUTING.md's required-ephemeris-files.tsv list), so the untested default now fails
    # visibly with a data-missing error instead of quietly returning a wrong body's position.
    $text = $text.Replace($sastnoDecl, $sastnoDecl + "`nstatic char spmoon[AS_MAXCH] = `"9501`";")

    $gethostnamePattern = '(?m)^  gethostname \(hostname, 80\);\r?\n  if \(strstr\(hostname, "as80"\) != NULL\) *\r?\n    line_limit = 2 \* 36525;'
    $gethostnameCount = ([regex]::Matches($text, $gethostnamePattern)).Count
    if ($gethostnameCount -ne 1) {
        Fail "swetest.c: expected exactly one unguarded gethostname block to guard, found $gethostnameCount. If upstream now guards it itself, drop this patch instead of applying it on top."
    }

    # The count above cannot detect upstream guarding the call, despite what that message says.
    # $gethostnamePattern anchors on the three body lines only, so it matches whether or not those
    # lines sit inside a preprocessor conditional: measured against a copy carrying upstream's own
    # fix, the count is 1 exactly as it is for the unguarded original. Without the check below, a
    # future tag that fixes this would be patched anyway, nesting our #if HPUNIX inside upstream's
    # guard. That nests harmlessly rather than miscompiling, which is worse for our purposes than
    # breaking, because the whole reason this assertion exists is to notice when upstream moves.
    #
    # Upstream's fix is #ifndef _WINDOWS around the call plus #define _WINDOWS under #ifdef _WIN32
    # in sweodef.h -- read directly off aloistr/swisseph master, not taken on description. Only the
    # first is visible in this file, so that is what this looks for: any preprocessor conditional
    # immediately above the call.
    #
    # WHY THIS PATCH USES #if HPUNIX AND NOT UPSTREAM'S OWN #ifndef _WINDOWS. Taking their guard
    # verbatim would mean taking both halves, and the second half cannot be retrofitted onto
    # v2.10.3final. _WINDOWS does not appear anywhere in the tag's sweodef.h (zero matches), so the
    # guard alone leaves it undefined, #ifndef is true, the block compiles and the build breaks
    # exactly as it does today. Defining it to compensate reaches two other sites in the tag:
    #
    #   swephexp.h:615  #if defined(MAKE_DLL) || defined(USE_DLL) || defined(_WINDOWS)
    #                   pulls in <windows.h> and declares `extern HANDLE dllhandle`, which the
    #                   comment beside it says is set by swedllst::DllMain. This is a static-lib
    #                   build with no DllMain, and the header is included by every translation unit
    #                   of libswe, so this changes how the reference LIBRARY compiles, not just
    #                   swetest.
    #   swetest.c:3944  do_printf switches from fputs(info, stdout) to fprintf(fp, info) -- a
    #                   different stream, and a non-literal format string. Every line swetest
    #                   prints would stop going to stdout, which is precisely what
    #                   scripts/verify-swetest-diff.ps1 captures and compares.
    #
    # Upstream's fix is coherent on master because master's tree is consistent with _WINDOWS being
    # defined on Windows. On the pinned tag it is not, and adopting it here would quietly change
    # the C reference this whole harness is measured against. #if HPUNIX reaches the same place by
    # the tag's own existing machinery: sweodef.h:96-98 defines MSDOS MY_TRUE under _WIN32, and
    # :143-144 derives HPUNIX MY_FALSE from MSDOS, so the block compiles out on Windows and on Unix
    # stays exactly where upstream's own guard would leave it,
    # which is the entire intent. The spmoon half of the workaround IS taken from upstream verbatim
    # (see the "9501" note above); only this half has a reason not to be.
    $gethostnameGuardedPattern = '(?m)^#\s*(if|ifdef|ifndef)\b[^\r\n]*\r?\n  gethostname \(hostname, 80\);'
    if ($text -match $gethostnameGuardedPattern) {
        Fail 'swetest.c: the gethostname call is already inside a preprocessor conditional, so upstream has fixed this itself. Drop this patch rather than applying it on top, and re-check whether the spmoon patch above is still needed too.'
    }
    $text = [regex]::Replace($text, $gethostnamePattern,
        "#if HPUNIX`n  gethostname (hostname, 80);`n  if (strstr(hostname, `"as80`") != NULL)`n    line_limit = 2 * 36525;`n#endif")

    [System.IO.File]::WriteAllText($DestinationPath, $text)
}

# ---------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------

function Build-Libswe {
    param([string] $VcVars, [string] $SourceDir, [string] $BuildDir, [string] $LibName, [string[]] $Sources)

    foreach ($file in $Sources) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceDir $file))) {
            Fail "$SourceDir is missing $file. Run 'git submodule update --init' (2.10.03) or scripts/fetch-2.08-baseline.ps1 (2.08)."
        }
    }

    New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
    # -LiteralPath, not -Path: an -OutputDir containing `[` or `]` would otherwise have $BuildDir
    # wildcard-interpreted, matching nothing, and -ErrorAction SilentlyContinue would swallow
    # that silently -- leaving whatever .obj files a previous run left behind for `lib` to
    # archive alongside (or instead of) this run's output.
    Get-ChildItem -LiteralPath $BuildDir -Filter *.obj -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

    $quoted = ($Sources | ForEach-Object { "`"$(Join-Path $SourceDir $_)`"" }) -join ' '
    $compile = "cl /nologo /c /TC $commonFlags /I`"$SourceDir`" $quoted"
    $result = Invoke-InVcEnv -VcVars $VcVars -WorkingDir $BuildDir -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail "Compiling libswe from $SourceDir failed."
    }

    $result = Invoke-InVcEnv -VcVars $VcVars -WorkingDir $BuildDir -Command "lib /nologo /OUT:`"$LibName`" *.obj"
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail "Archiving $LibName failed."
    }
    return (Join-Path $BuildDir $LibName)
}

# A contraction fuses a multiply and an add and skips the intermediate rounding, so a
# contracted expression can differ from the same expression evaluated in two steps. The port
# cannot contract (RyuJIT emits FMA only for an explicit Math.FusedMultiplyAdd), so a
# contraction on the C side would be a divergence with no counterpart to match it.
#
# Call this on an object file or a .lib this script itself compiled -- never on the final,
# linked swetest.exe. That .exe is not guaranteed to be only this build's own code (see the CRT
# discussion in the header's .DESCRIPTION), and this assertion exists to catch contraction in
# sweph.c/swetest.c's own arithmetic, not to characterize whatever else got linked in.
function Assert-NoFmaContraction {
    param([string] $VcVars, [string] $BuildDir, [string] $ArtifactPath)

    $result = Invoke-InVcEnv -VcVars $VcVars -WorkingDir $BuildDir -Command "dumpbin /nologo /disasm:nobytes `"$ArtifactPath`""
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail "dumpbin failed on $ArtifactPath."
    }

    # Select-String yields nothing at all when there is no match, and Set-StrictMode rejects
    # .Count on that, so both results are forced to arrays before being counted.
    $fma = @($result.Output | Select-String -Pattern 'vfmadd|vfmsub|vfnmadd|vfnmsub')
    if ($fma.Count -ne 0) {
        $fma | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
        Fail "$ArtifactPath contains $($fma.Count) FMA instruction(s). /fp:precise should not contract. Add /fp:strict or pin fp_contract(off)."
    }

    # A disassembly with no floating-point multiplies at all would pass the check above for the
    # wrong reason -- dumpbin silently producing nothing, say. The pattern covers both scalar and
    # packed forms, and both the legacy SSE2 mnemonics and their VEX-encoded equivalents: under
    # /arch:AVX2 the compiler emits vmulsd, not mulsd, for the exact same source, so a plain
    # \bmulsd\b count silently reads as zero and this guard would fire on a correct build.
    $mulCount = @($result.Output | Select-String -Pattern '\b(v?mulsd|v?mulpd)\b').Count
    if ($mulCount -lt 100) {
        Fail "$ArtifactPath disassembled to only $mulCount multiply instruction(s), which is too few to be a real build. The FMA check would have passed vacuously."
    }
    Write-Host "  no FMA contraction ($mulCount multiply instructions scanned)" -ForegroundColor DarkGray
}

# The FMA check above proves nothing about /fp:precise on its own: at the default x64 /arch
# (SSE2, which has no FMA encoding at all), it passes identically under /fp:precise and
# /fp:fast, and /fp:fast is the flag that matters more to catch -- it permits reassociation,
# which the FMA check cannot see at all. (x - x) + 1.0 tells the two apart directly. IEEE-754
# requires x - x == 0 for every finite x, but a NaN or infinite x breaks that (NaN - NaN and
# Inf - Inf are both NaN), so /fp:precise cannot assume x is finite and must emit the
# subtraction. /fp:fast is allowed to make that assumption and folds the whole expression to a
# constant. Verified against this toolchain before being trusted: /fp:precise emits subsd
# (vsubsd under /arch:AVX2); /fp:fast emits neither, at every /arch tested.
function Assert-FpPrecise {
    param([string] $VcVars, [string] $BuildDir, [string] $Flags)

    $probeSource = Join-Path $BuildDir 'fp_precise_probe.c'
    $probeObj = Join-Path $BuildDir 'fp_precise_probe.obj'
    [System.IO.File]::WriteAllText($probeSource, "double fp_precise_probe(double x) {`n    return (x - x) + 1.0;`n}`n")

    $compile = "cl /nologo /c /TC $Flags /Fo:`"$probeObj`" `"$probeSource`""
    $result = Invoke-InVcEnv -VcVars $VcVars -WorkingDir $BuildDir -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Compiling the /fp:precise probe failed.'
    }

    $result = Invoke-InVcEnv -VcVars $VcVars -WorkingDir $BuildDir -Command "dumpbin /nologo /disasm:nobytes `"$probeObj`""
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail "dumpbin failed on $probeObj."
    }

    $subsd = @($result.Output | Select-String -Pattern '\bv?subsd\b')
    if ($subsd.Count -eq 0) {
        $result.Output | Write-Host
        Fail "The /fp:precise probe compiled with no subsd/vsubsd instruction -- these flags ($Flags) do not behave as /fp:precise. Under /fp:fast the compiler folds (x - x) + 1.0 to a constant load, which is exactly what this probe exists to catch."
    }
    Write-Host "  /fp:precise confirmed (probe emitted $($subsd.Count) subsd/vsubsd instruction(s))" -ForegroundColor DarkGray
}

# swetest.exe is the actual artifact scripts/validate-c-reference.ps1 runs and a porter compares
# by hand -- checking only the two .lib files and never running swetest.exe at all would leave
# the thing everyone actually looks at unverified.
#
# A wrong -edir does NOT show up as a parse failure. swe_calc silently falls back to Moshier and
# swetest still prints a well-formed line and exits 0. Measured against this build at JD
# 2451545.0 TT (J2000): real ephemeris data gives Sun 280.3681656; a nonexistent -edir gives
# 280.3681666 -- the two differ only in the last printed digit, so this pins the full string
# rather than a prefix or a parse-only check, and also requires every file
# Tests/conformance/required-ephemeris-files.tsv declares to be present in $EpheDir before
# running at all, so a build missing an ephemeris file fails here instead of quietly comparing
# against Moshier output.
function Invoke-SwetestSmoke {
    param([string] $ExePath, [string] $EpheDir)

    $requiredFilesPath = Join-Path $repoRoot 'Tests/conformance/required-ephemeris-files.tsv'
    if (-not (Test-Path -LiteralPath $requiredFilesPath -PathType Leaf)) {
        Fail "Required ephemeris file list not found at $requiredFilesPath."
    }
    $requiredFiles = @(Get-Content -LiteralPath $requiredFilesPath |
        Where-Object { $_.Trim() -ne '' -and -not $_.StartsWith('#') })
    if ($requiredFiles.Count -eq 0) {
        Fail "$requiredFilesPath parsed to zero required files."
    }
    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $EpheDir $_) -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        Fail "$EpheDir is missing required ephemeris file(s): $($missingFiles -join ', '). A missing file falls back to Moshier silently -- see this function's header comment -- and must not reach the smoke run."
    }

    # PowerShell's argument parser splits an argument like -bj2451545.0 on the dots, so the
    # command has to be built as one string and run through cmd /c, which does not.
    #
    # -fPl selects two output fields: P (planet name) and l (ecliptic longitude) -- one name and
    # one decimal number per body, not the three-column l/b/r shape validate-c-reference.ps1
    # asks for with -fPlbR. Verified against this build's own output before pinning the pattern.
    $full = "`"$ExePath`" -bj2451545.0 -p0 -fPl -eswe -edir`"$EpheDir`""
    $output = @((cmd /c $full 2>&1) | Where-Object { $_ -ne '' })
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Host
        Fail "swetest.exe smoke run exited $LASTEXITCODE."
    }
    $sunLine = $output | Where-Object { $_ -match '^Sun\s+-?\d+\.\d+\s*$' } | Select-Object -First 1
    if (-not $sunLine) {
        $output | Write-Host
        Fail 'swetest.exe smoke run did not print a recognizable Sun longitude line.'
    }
    $sunMatch = [regex]::Match($sunLine, '^Sun\s+(-?\d+\.\d+)\s*$')
    $actualSun = $sunMatch.Groups[1].Value
    $expectedSun = '280.3681656'
    if ($actualSun -ne $expectedSun) {
        $output | Write-Host
        Fail "swetest.exe smoke run printed Sun $actualSun at JD 2451545.0 TT, expected $expectedSun. A wrong -edir falls back to Moshier silently and prints 280.3681666 instead of failing -- see this function's header comment."
    }
    Write-Host "  smoke run OK: Sun $actualSun" -ForegroundColor DarkGray
}

# =========================================================================================
# Everything below -- provenance checks, toolchain discovery, and the build itself -- runs
# inside one try/catch/finally, so a failure anywhere in it (not just inside the build proper)
# gets the same "FAIL: ..." banner and the same temp-directory cleanup. An earlier version of
# this script only wrapped the build, leaving the provenance and toolchain-discovery sections
# above the try -- a failure there (e.g. $verResult carrying no matching cl.exe banner line)
# threw a raw, unformatted PowerShell error instead of the FAIL: path every other failure here
# produces.
# =========================================================================================

$tempRoot = $null
$exitCode = 0
try {
    # =====================================================================================
    # Provenance -- checked before anything is built, so a wrong input is never silently
    # compiled into an artifact that then looks like a normal, trustworthy build.
    # =====================================================================================

    # The gitlink the superproject pins external/swisseph to, versus what is actually checked out.
    # Checked before building (not after, as an earlier version of this script did) because a
    # mismatched submodule commit would otherwise compile cleanly, pass every assertion below, and
    # leave a toolchain.txt whose swisseph_commit row names the wrong C entirely.
    $gitlink = (& git -C $repoRoot rev-parse 'HEAD:external/swisseph' 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $gitlink) {
        Fail 'Could not resolve the pinned commit for external/swisseph (git rev-parse HEAD:external/swisseph).'
    }
    $gitlink = $gitlink.Trim()
    $submoduleHead = (& git -C $src210 rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $submoduleHead) {
        Fail "Could not resolve external/swisseph's checked-out commit (git -C external/swisseph rev-parse HEAD). Is it a valid git checkout?"
    }
    $submoduleHead = $submoduleHead.Trim()
    if ($gitlink -ne $submoduleHead) {
        Fail "external/swisseph is at $submoduleHead but the superproject pins $gitlink. Run 'git submodule update'."
    }

    # The 2.08 tree has no compiler-visible signal that it is the wrong one -- CONTRIBUTING.md's
    # "2.08 baseline trap" is exactly this: a 2.08 tree fetched from the wrong place still has .c
    # files, still compiles, and still links, and a bad diff work queue from it looks like nothing
    # at all. scripts/fetch-2.08-baseline.ps1 stamps external/pyswisseph-2.08/.manifest-sha256 with
    # the manifest's own sha256 only once every file has passed verification; this checks that stamp
    # the same way scripts/gen-delta.ps1 does, rather than trusting that a directory with files in
    # it is the directory that was actually verified.
    function Test-Pyswisseph208Verified {
        param([string] $BaselineDir, [string] $ManifestPath)
        $stampPath = Join-Path $BaselineDir '.manifest-sha256'
        if (-not (Test-Path -LiteralPath $stampPath -PathType Leaf) -or -not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
            return $false
        }
        $stamped = (Get-Content -LiteralPath $stampPath -Raw).Trim()
        $current = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        return $stamped -eq $current
    }
    if (-not (Test-Pyswisseph208Verified -BaselineDir $src208 -ManifestPath $manifest208Path)) {
        Fail "$src208 has not been verified against $manifest208Path (missing or stale .manifest-sha256 stamp). Run scripts/fetch-2.08-baseline.ps1 first."
    }
    $manifest208Sha256 = (Get-FileHash -LiteralPath $manifest208Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "PASS: external/swisseph is at the pinned commit ($submoduleHead) and external/pyswisseph-2.08 is verified against the manifest."

    # =====================================================================================
    # Toolchain
    # =====================================================================================

    $vcvars = Get-VcVarsPath
    Write-Host "Toolchain: $vcvars"

    # cl.exe silently prepends %CL% and appends %_CL_% to every invocation, and vcvars64.bat does
    # not clear either. Measured: CL=/arch:AVX2 flips mulsd to vmulsd; CL=/fp:fast at the default
    # arch produces 0 FMA and 6 mulsd -- passes every assertion above while toolchain.txt still
    # says /fp:precise, because the flags that were actually compiled were never the flags recorded.
    # Cleared here, in this process, before any child cmd is spawned, so no invocation below can
    # inherit either one; recorded in toolchain.txt so a caller who had them set can see that.
    $envClOriginal = $env:CL
    $envClUnderscoreOriginal = $env:_CL_
    if ($envClOriginal) { Write-Host "NOTE: clearing inherited CL='$envClOriginal' before compiling." -ForegroundColor Yellow }
    if ($envClUnderscoreOriginal) { Write-Host "NOTE: clearing inherited _CL_='$envClUnderscoreOriginal' before compiling." -ForegroundColor Yellow }
    Remove-Item Env:\CL -ErrorAction SilentlyContinue
    Remove-Item Env:\_CL_ -ErrorAction SilentlyContinue

    $verResult = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $repoRoot -Command 'cl 2>&1'
    $clMatch = @($verResult.Output | Select-String -Pattern 'Microsoft .* Compiler Version')
    if ($clMatch.Count -eq 0) { Fail 'Could not determine the cl.exe version.' }
    $clBanner = $clMatch[0].ToString().Trim()
    Write-Host "  $clBanner"

    $linkResult = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $repoRoot -Command 'link 2>&1'
    $linkMatch = @($linkResult.Output | Select-String -Pattern 'Microsoft .* Linker Version')
    if ($linkMatch.Count -eq 0) { Fail 'Could not determine the link.exe version.' }
    $linkBanner = $linkMatch[0].ToString().Trim()
    Write-Host "  $linkBanner"

    $windowsSdkVersion = Get-VcVarsEnvValue -VcVars $vcvars -VarName 'WindowsSDKVersion'
    if (-not (Test-WindowsSdkVersionShape $windowsSdkVersion)) {
        Fail "Could not determine %WindowsSDKVersion% from vcvars64.bat -- got '$windowsSdkVersion', which is not a real SDK version. A literal '!WindowsSDKVersion!' means cmd's delayed expansion never fired."
    }
    Write-Host "  Windows SDK $windowsSdkVersion"

    # Recorded so a later Windows Update to ucrtbase.dll -- the DLL /MD binds and CoreCLR's own
    # trig/exp/log FCALLs bind too -- shows up as a changed row here instead of silently changing
    # what this build's output means to compare against. Guarded rather than left to write an empty
    # row: a missing ucrtbase.dll would otherwise leave toolchain.txt looking complete while recording
    # nothing about the one thing this CRT choice depends on.
    $ucrtbaseDllPath = Join-Path $env:SystemRoot 'System32/ucrtbase.dll'
    if (-not (Test-Path -LiteralPath $ucrtbaseDllPath -PathType Leaf)) {
        Fail "ucrtbase.dll not found at $ucrtbaseDllPath -- cannot record the version this /MD build binds against."
    }
    $ucrtbaseVersion = (Get-Item -LiteralPath $ucrtbaseDllPath).VersionInfo.FileVersion
    if (-not $ucrtbaseVersion) {
        Fail "Could not read a FileVersion from $ucrtbaseDllPath."
    }
    Write-Host "  ucrtbase.dll $ucrtbaseVersion"

    $commonFlags = '/O2 /fp:precise /D_CRT_SECURE_NO_WARNINGS /MD'

    # =====================================================================================
    # Build into a fresh temp directory, promoted into -OutputDir only on success. Building
    # straight into -OutputDir left a failed run's toolchain.txt sitting next to whatever a
    # previous, unrelated run had produced -- a coherent-looking, wrong ledger, with no artifact
    # in it dated to the run that actually failed.
    # =====================================================================================

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "swisseph-c-reference-$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    Write-Host 'Checking /fp:precise is actually in effect for these flags...'
    Assert-FpPrecise -VcVars $vcvars -BuildDir $tempRoot -Flags $commonFlags
    Remove-Item -LiteralPath (Join-Path $tempRoot 'fp_precise_probe.c'), (Join-Path $tempRoot 'fp_precise_probe.obj') -Force -ErrorAction SilentlyContinue

    Write-Host 'Building libswe 2.10.03...'
    $build210 = Join-Path $tempRoot 'build-2.10.03'
    $lib210 = Build-Libswe -VcVars $vcvars -SourceDir $src210 -BuildDir $build210 -LibName 'libswe-2.10.03.lib' -Sources $LibSources210
    Assert-NoFmaContraction -VcVars $vcvars -BuildDir $build210 -ArtifactPath $lib210

    Write-Host 'Building libswe 2.08...'
    $build208 = Join-Path $tempRoot 'build-2.08'
    $lib208 = Build-Libswe -VcVars $vcvars -SourceDir $src208 -BuildDir $build208 -LibName 'libswe-2.08.lib' -Sources $LibSources208
    Assert-NoFmaContraction -VcVars $vcvars -BuildDir $build208 -ArtifactPath $lib208

    # sedump.c is this repo's own file, not upstream, so unlike swetest.c below it needs no patch
    # for either C version -- see the header's .DESCRIPTION for why it is compiled against the
    # 2.08 headers here rather than repointed at the 2.10.03 driver run-oracle-dump.ps1 already
    # builds.
    #
    # Not FMA-checked, unlike lib208/lib210/swetest_patched.obj above and below: sedump.c parses
    # doubles with strtod, hands them straight to whichever swe_* function the grid row names, and
    # prints the bits that function returns -- it performs no floating-point arithmetic of its own
    # for a contraction to apply to. Assert-NoFmaContraction's own vacuous-build guard (at least
    # 100 multiply instructions) confirms this: sedump.obj disassembles to 0. The FMA risk this
    # build cares about lives entirely inside libswe-2.08.lib, already checked above.
    Write-Host 'Building sedump 2.08...'
    $sedumpSource = Join-Path $repoRoot 'Tools/CReference/sedump.c'
    $sedump208Obj = Join-Path $build208 'sedump.obj'
    $compile = "cl /nologo /c /TC $commonFlags /I`"$src208`" /Fo:`"$sedump208Obj`" `"$sedumpSource`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $build208 -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Compiling sedump.c against the 2.08 headers failed.'
    }

    $sedump208Exe = Join-Path $tempRoot 'sedump-2.08.exe'
    $link = "cl /nologo /Fe:`"$sedump208Exe`" `"$sedump208Obj`" /link `"$lib208`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $build208 -Command $link
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Linking sedump-2.08.exe failed.'
    }

    Write-Host 'Building swetest 2.10.03...'
    $patched = Join-Path $build210 'swetest_patched.c'
    New-PatchedSwetestSource -SourcePath (Join-Path $src210 'swetest.c') -DestinationPath $patched

    # Compiled to an object and FMA-checked BEFORE linking, not after -- see the header's
    # .DESCRIPTION and Assert-NoFmaContraction's own comment for why the linked .exe is not the
    # thing to scan. Checking the object file this compile step produces keeps the assertion
    # scoped to sweph.c/swetest.c's own arithmetic, the only thing it was written to prove.
    $swetestObj = Join-Path $build210 'swetest_patched.obj'
    $compile = "cl /nologo /c /TC $commonFlags /I`"$src210`" /Fo:`"$swetestObj`" `"$patched`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $build210 -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Compiling swetest_patched.c failed.'
    }
    Assert-NoFmaContraction -VcVars $vcvars -BuildDir $build210 -ArtifactPath $swetestObj

    $swetestExe = Join-Path $tempRoot 'swetest.exe'
    $link = "cl /nologo /Fe:`"$swetestExe`" `"$swetestObj`" /link `"$lib210`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $build210 -Command $link
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Linking swetest.exe failed.'
    }

    Write-Host 'Smoke-running swetest.exe...'
    Invoke-SwetestSmoke -ExePath $swetestExe -EpheDir (Join-Path $src210 'ephe')

    # ---------------------------------------------------------------------------------------
    # Record the toolchain the artifacts were produced by
    # ---------------------------------------------------------------------------------------

    $patchedSha256 = Get-Sha256Hex -Path $patched
    $lib210Sha256 = Get-Sha256Hex -Path $lib210
    $lib208Sha256 = Get-Sha256Hex -Path $lib208
    $swetestSha256 = Get-Sha256Hex -Path $swetestExe
    $sedump208Sha256 = Get-Sha256Hex -Path $sedump208Exe

    $lines = @(
        "vcvars                    $vcvars"
        "cl                        $clBanner"
        "link                      $linkBanner"
        "windows_sdk_version       $windowsSdkVersion"
        "flags                     $commonFlags"
        "arch                      $(Get-ArchBaseline $commonFlags)"
        "crt                       /MD (dynamic) -- binds the same ucrtbase.dll CoreCLR's Math.Sin/Cos/Tan/Atan2/Pow/Exp/Log FCALLs call into, so C and .NET run through one implementation; measured bit-identical against /MT and against .NET today (see the header's .DESCRIPTION), so this keeps both sides aligned under future CRT servicing rather than fixing a present difference"
        "ucrtbase_dll_version      $ucrtbaseVersion ($ucrtbaseDllPath -- watch this row for drift after a Windows Update)"
        "fp_contraction            none (asserted by disassembly of both .lib files and swetest_patched.obj -- not the linked swetest.exe, see Assert-NoFmaContraction)"
        "fp_precise_probe          confirmed -- (x - x) + 1.0 compiled to subsd/vsubsd, not folded to a constant"
        "env_CL                    $(if ($envClOriginal) { $envClOriginal } else { 'not set' })"
        "env__CL_                  $(if ($envClUnderscoreOriginal) { $envClUnderscoreOriginal } else { 'not set' })"
        "pyswisseph_2_08_manifest  $manifest208Sha256 (scripts/pyswisseph-2.08.manifest.tsv)"
        "swisseph_commit           $submoduleHead"
        "swetest_patches           spmoon declaration (default `"9501`"); gethostname guarded by HPUNIX"
        "swetest_patched_sha256    $patchedSha256"
        "libswe_2_10_03_sha256     $lib210Sha256"
        "libswe_2_08_sha256        $lib208Sha256"
        "swetest_sha256            $swetestSha256"
        "sedump_2_08_sha256        $sedump208Sha256 (Tools/CReference/sedump.c, compiled against external/pyswisseph-2.08/swephexp.h, linked against libswe-2.08.lib)"
        "built_utc                 $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    )
    $toolchainPath = Join-Path $tempRoot 'toolchain.txt'
    [System.IO.File]::WriteAllText($toolchainPath, ($lines -join "`n") + "`n")

    # ---------------------------------------------------------------------------------------
    # Promote: only reached once every build step and every assertion above has passed.
    # ---------------------------------------------------------------------------------------

    if (Test-Path -LiteralPath $OutputDir) {
        Remove-Item -LiteralPath $OutputDir -Recurse -Force
    }
    $outputParent = Split-Path -Parent $OutputDir
    if ($outputParent -and -not (Test-Path -LiteralPath $outputParent)) {
        New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
    }
    Move-Item -LiteralPath $tempRoot -Destination $OutputDir
    $tempRoot = $null

    Write-Host ''
    Write-Host 'Built:' -ForegroundColor Green
    Write-Host "  $(Join-Path $OutputDir 'build-2.10.03/libswe-2.10.03.lib')"
    Write-Host "  $(Join-Path $OutputDir 'build-2.08/libswe-2.08.lib')"
    Write-Host "  $(Join-Path $OutputDir 'swetest.exe')"
    Write-Host "  $(Join-Path $OutputDir 'sedump-2.08.exe')"
    Write-Host "  $(Join-Path $OutputDir 'toolchain.txt')"
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $exitCode
