#Requires -Version 7
<#
.SYNOPSIS
    Fails if the packed SwissEphNet library ever gains a package dependency or framework
    reference.

.DESCRIPTION
    SwissEphNet.csproj has no PackageReference today, and the packed nuspec has an empty
    dependency group for every target framework it ships (netstandard2.0, net8.0, net10.0) and
    no framework references. That is a choice, not an accident: the 2.8.1.0 release dropped
    System.Text.Encoding.CodePages and made UTF-8 the explicit default instead of pulling in a
    package to keep Windows-1252 support (see the release notes in SwissEphNet.csproj and
    docs/known-issues.md). A pure-managed library with zero dependencies can run anywhere .NET
    runs, which is a large part of why this port exists rather than a P/Invoke wrapper.

    Nothing enforced that property before this script, and roughly 300 hunks of 2.10.03 porting
    work are about to land. This repo already pins its other invariants externally rather than
    trusting a contributor to remember them -- scripts/freeze-manifest.tsv for the transliteration
    freeze, scripts/gen-delta-hunk-counts.tsv for the delta renderer, Tests/baseline/row-counts.tsv
    for the characterization baseline. This script is the same idea applied to the dependency
    list: pack the library the way it actually ships, read the manifest NuGet itself produced,
    and fail if anything shows up in it.

    It packs SwissEphNet/SwissEphNet.csproj into a temporary directory, opens the resulting
    .nupkg as a zip, reads the embedded .nuspec, and checks it directly rather than trusting the
    csproj source to say the same thing -- an MSBuild target could add a dependency without a
    visible PackageReference, and the packed manifest is what NuGet consumers actually see.

    The target frameworks are read from the csproj by asking MSBuild to evaluate them (`dotnet
    msbuild -getProperty:TargetFrameworks`), the same way `dotnet pack` itself resolves them,
    rather than hardcoded or parsed from the csproj's raw XML. Adding a TFM without updating the
    packed nuspec shows up as a real check -- the nuspec's dependency-group names no longer match
    the csproj's target frameworks, once both are normalized to the same short form -- instead of
    silently going unchecked.

    Several checks exist only to stop this script from passing by accident: no .nupkg produced,
    no <dependencies> element in the nuspec at all, or zero dependency groups found are all
    treated as failures, not as "nothing to report." A gate that passes because it looked at
    nothing is worse than no gate -- see CONTRIBUTING.md's account of scripts/verify-freeze.ps1's
    own history with exactly this mistake.

.PARAMETER Configuration
    Build configuration to pack. Defaults to Release, the configuration actually shipped.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Matches Tools/CReference/build-c.ps1's own setting, for the same reason: `dotnet msbuild` and
# `dotnet pack` below are native commands. $PSNativeCommandUseErrorActionPreference defaults to
# $false today (pwsh 7.6.3), so this line changes nothing yet -- it is future-proofing. If it
# ever defaults to $true, a non-zero exit from either command would throw immediately under
# $ErrorActionPreference = 'Stop', and every `if ($LASTEXITCODE -ne 0)` check below would become
# unreachable dead code instead of doing anything.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot 'SwissEphNet/SwissEphNet.csproj'

if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
    Write-Host "FAIL: csproj not found at $csprojPath."
    exit 1
}

# Read the declared target frameworks the authoritative way MSBuild itself resolves them, rather
# than parsing the csproj's raw XML. The XML-walking version this replaced read
# $propertyGroup.TargetFrameworks on an XmlElement, which throws under Set-StrictMode when a
# PropertyGroup lacks that child -- it survived only because SwissEphNet.csproj's first
# PropertyGroup happens to declare TargetFrameworks, so the loop's `break` fired on the very first
# iteration and the TargetFramework (singular) fallback below it was dead code, never exercised.
# It also read raw XML text, ignoring any Condition attribute, so a conditional TFM list would be
# read regardless of whether MSBuild would ever actually evaluate it. `-getProperty` runs the real
# MSBuild evaluation -- Condition attributes included -- and returns the same value `dotnet
# pack`/`dotnet build` themselves would use.
function Get-MsBuildProperty {
    param([string] $CsprojPath, [string] $PropertyName)
    $output = & dotnet msbuild $CsprojPath "-getProperty:$PropertyName" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: dotnet msbuild -getProperty:$PropertyName exited $LASTEXITCODE for $CsprojPath."
        $output | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    return ($output | Select-Object -Last 1).ToString().Trim()
}

$tfmText = Get-MsBuildProperty -CsprojPath $csprojPath -PropertyName 'TargetFrameworks'
if (-not $tfmText) {
    # Falls back to the singular property for a csproj that targets exactly one framework and so
    # never declares the plural TargetFrameworks at all.
    $tfmText = Get-MsBuildProperty -CsprojPath $csprojPath -PropertyName 'TargetFramework'
}
if (-not $tfmText) {
    Write-Host "FAIL: dotnet msbuild resolved neither TargetFrameworks nor TargetFramework for $csprojPath."
    exit 1
}
$expectedTfms = @($tfmText -split ';' | Where-Object { $_.Trim() -ne '' })
if ($expectedTfms.Count -eq 0) {
    Write-Host "FAIL: TargetFrameworks in $csprojPath resolved to zero entries."
    exit 1
}
Write-Host "Target frameworks declared in csproj: $($expectedTfms -join ', ')"

# Namespace-agnostic nuspec traversal. NuGet has changed the nuspec XSD namespace URI across
# schema versions, and PowerShell's dotted XML property access on a namespaced document can
# silently return $null instead of erroring -- which, unchecked, is exactly the "found nothing,
# so nothing to report" mistake this script exists to avoid. local-name() matching sidesteps the
# namespace question entirely rather than depending on a specific URI staying correct.
function Get-ChildElement {
    param([System.Xml.XmlNode] $Node, [string] $LocalName)
    return $Node.SelectSingleNode("*[local-name()='$LocalName']")
}
function Get-ChildElements {
    param([System.Xml.XmlNode] $Node, [string] $LocalName)
    # The leading comma stops PowerShell's automatic pipeline unrolling from flattening a
    # single-element (or empty) result back into a scalar/$null on return -- without it, a
    # dependency group list with exactly one group came back as an XmlElement instead of an
    # array, and .Count failed on it.
    return , @($Node.SelectNodes("*[local-name()='$LocalName']"))
}

# A nuspec dependency group can name its TFM either in NuGet's short folder-name form (net8.0,
# net10.0 -- what dotnet msbuild -getProperty:TargetFrameworks returns) or its older, longer
# .NETFramework-style display name. Verified against this project's own packed output: net10.0
# and net8.0 arrive in short form already; netstandard2.0 arrives as .NETStandard2.0. Comparing
# the group count against the csproj's TFM count (the old check) missed a real name mismatch
# entirely -- both counts move together whenever a TFM is added, since `dotnet pack` always emits
# one group per TFM, so the count alone can never disagree. Comparing the raw strings would
# instead treat 'netstandard2.0' and '.NETStandard2.0' as two different, unrelated frameworks.
# This normalizes both sides to the short form so a set comparison actually compares like with
# like.
function Get-NormalizedTfm {
    param([string] $Tfm)
    $t = $Tfm.Trim()
    if ($t -match '^\.NETStandard(?<v>[\d.]+)$') { return "netstandard$($Matches['v'])" }
    if ($t -match '^\.NETCoreApp(?<v>[\d.]+)$') { return "netcoreapp$($Matches['v'])" }
    if ($t -match '^\.NETFramework(?<v>[\d.]+)$') { return 'net' + ($Matches['v'] -replace '\.', '') }
    return $t.ToLowerInvariant()
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("swisseph-nodeps-" + [System.Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDir -Force)

$failures = [System.Collections.Generic.List[string]]::new()
$inspectedNupkgName = $null

try {
    Write-Host "Packing $csprojPath ($Configuration) into $tempDir ..."
    & dotnet pack $csprojPath -c $Configuration -o $tempDir | Write-Host
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("dotnet pack exited $LASTEXITCODE.")
    }
    else {
        # Directory.Build.props pins SymbolPackageFormat=snupkg, so today's symbol package is
        # named '*.snupkg', which -Filter '*.nupkg' already excludes on its own -- this -notlike
        # is a no-op against that setting, verified against the real packed output. Kept as a
        # defensive fallback in case that setting is ever reverted to the older '.symbols.nupkg'
        # format, where it would start mattering again.
        $nupkgs = @(Get-ChildItem -LiteralPath $tempDir -Filter '*.nupkg' |
            Where-Object { $_.Name -notlike '*.symbols.nupkg' })

        if ($nupkgs.Count -eq 0) {
            $failures.Add("dotnet pack produced no .nupkg under $tempDir.")
        }
        else {
            $inspectedNupkgName = $nupkgs[0].Name
            Write-Host "Inspecting $inspectedNupkgName ..."

            $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgs[0].FullName)
            try {
                $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
                if (-not $nuspecEntry) {
                    $failures.Add("no .nuspec entry found inside $inspectedNupkgName.")
                }
                else {
                    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
                    try { $nuspecText = $reader.ReadToEnd() } finally { $reader.Dispose() }

                    [xml] $nuspecXml = $nuspecText
                    $packageNode = Get-ChildElement -Node $nuspecXml -LocalName 'package'
                    $metadataNode = if ($packageNode) { Get-ChildElement -Node $packageNode -LocalName 'metadata' } else { $null }

                    if (-not $metadataNode) {
                        $failures.Add("$inspectedNupkgName : nuspec has no <package><metadata> element -- not a real package manifest.")
                    }
                    else {
                        $dependenciesNode = Get-ChildElement -Node $metadataNode -LocalName 'dependencies'
                        if (-not $dependenciesNode) {
                            $failures.Add("$inspectedNupkgName : nuspec has no <dependencies> element at all.")
                        }
                        else {
                            $groups = Get-ChildElements -Node $dependenciesNode -LocalName 'group'
                            if ($groups.Count -eq 0) {
                                $failures.Add("$inspectedNupkgName : <dependencies> has zero <group> elements; nothing was examined.")
                            }
                            else {
                                $groupFrameworks = @($groups | ForEach-Object { $_.GetAttribute('targetFramework') })
                                $normalizedGroupTfms = [System.Collections.Generic.HashSet[string]]::new(
                                    [string[]] ($groupFrameworks | ForEach-Object { Get-NormalizedTfm $_ }),
                                    [System.StringComparer]::OrdinalIgnoreCase)
                                $normalizedExpectedTfms = [System.Collections.Generic.HashSet[string]]::new(
                                    [string[]] ($expectedTfms | ForEach-Object { Get-NormalizedTfm $_ }),
                                    [System.StringComparer]::OrdinalIgnoreCase)

                                if ($groups.Count -ne $normalizedGroupTfms.Count) {
                                    $failures.Add(
                                        "$inspectedNupkgName : nuspec has $($groups.Count) dependency group(s) [$($groupFrameworks -join ', ')] " +
                                        "but only $($normalizedGroupTfms.Count) distinct target framework(s) once normalized -- a duplicate group.")
                                }
                                elseif (-not $normalizedGroupTfms.SetEquals($normalizedExpectedTfms)) {
                                    $failures.Add(
                                        "$inspectedNupkgName : nuspec dependency groups [$($groupFrameworks -join ', ')] do not match " +
                                        "the csproj's target framework(s) [$($expectedTfms -join ', ')] once normalized.")
                                }
                                else {
                                    foreach ($group in $groups) {
                                        $framework = $group.GetAttribute('targetFramework')
                                        $deps = Get-ChildElements -Node $group -LocalName 'dependency'
                                        if ($deps.Count -eq 0) {
                                            Write-Host "  $framework : no dependencies"
                                        }
                                        else {
                                            foreach ($dep in $deps) {
                                                $failures.Add(
                                                    "$inspectedNupkgName : target framework '$framework' depends on package " +
                                                    "'$($dep.GetAttribute('id'))' $($dep.GetAttribute('version')).")
                                            }
                                        }
                                    }
                                }
                            }

                            # A nuspec can also carry bare <dependency> elements directly under
                            # <dependencies>, with no <group> wrapper -- the shape NuGet emits for
                            # a single-TFM package, or a hand-authored nuspec. Not reachable from
                            # this csproj today (it always packs multiple TFMs, so NuGet always
                            # emits groups), but looked at anyway for the same reason the
                            # frameworkReference check below already does: a gate that passes
                            # because it looked at nothing is worse than no gate (see
                            # CONTRIBUTING.md's account of scripts/verify-freeze.ps1's own history
                            # with exactly this mistake).
                            $flatDeps = Get-ChildElements -Node $dependenciesNode -LocalName 'dependency'
                            foreach ($dep in $flatDeps) {
                                $failures.Add(
                                    "$inspectedNupkgName : ungrouped dependency '$($dep.GetAttribute('id'))' " +
                                    "$($dep.GetAttribute('version')) is present directly under <dependencies>.")
                            }
                            if ($flatDeps.Count -eq 0) {
                                Write-Host '  no ungrouped <dependency> entries'
                            }
                        }

                        # frameworkReference can appear grouped by TFM, same as dependency, so this
                        # looks anywhere under <metadata> rather than assuming a shape.
                        $frameworkRefs = @($metadataNode.SelectNodes(".//*[local-name()='frameworkReference']"))
                        foreach ($frameworkRef in $frameworkRefs) {
                            $failures.Add("$inspectedNupkgName : framework reference '$($frameworkRef.GetAttribute('name'))' is present in the nuspec.")
                        }
                        if ($frameworkRefs.Count -eq 0) {
                            Write-Host '  no <frameworkReference> entries'
                        }

                        # frameworkAssembly is the older, pre-PackageReference shape for the same
                        # idea -- a dependency on a framework assembly rather than a NuGet package.
                        # Same defensive reasoning as frameworkReference above.
                        $frameworkAssemblies = @($metadataNode.SelectNodes(".//*[local-name()='frameworkAssembly']"))
                        foreach ($frameworkAssembly in $frameworkAssemblies) {
                            $failures.Add("$inspectedNupkgName : framework assembly reference '$($frameworkAssembly.GetAttribute('assemblyName'))' is present in the nuspec.")
                        }
                        if ($frameworkAssemblies.Count -eq 0) {
                            Write-Host '  no <frameworkAssembly> entries'
                        }
                    }
                }
            }
            finally {
                $zip.Dispose()
            }
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) { Write-Host "  $failure" }
    Write-Host ''
    Write-Host 'FAIL: the packed SwissEphNet library has a dependency or framework reference it should not have.'
    Write-Host 'If this is intended, it is a real behavior change to a deliberately zero-dependency library --'
    Write-Host 'update the release notes in SwissEphNet.csproj and docs/known-issues.md alongside it, and get review.'
    exit 1
}

Write-Host ''
Write-Host "PASS: $inspectedNupkgName has zero package dependencies and zero framework references across all $($expectedTfms.Count) target framework(s) ($($expectedTfms -join ', '))."
exit 0
