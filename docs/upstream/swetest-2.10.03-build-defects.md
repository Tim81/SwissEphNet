# Closing out our swetest.c reports, and one request about the release tag

Everything we raised is resolved. One was our mistake and we withdrew it; the other you had already
fixed. This note records where each ended up, so the thread has a clear close, and leaves one
request standing about the tag itself.

This comes from the SwissEphNet fork, a C# port of the library. We found these while diffing our
port's output against a reference `swetest` binary built from your source.

## Status

| Reported | Where it stands | Fixed in |
|---|---|---|
| `dms()` loses the minus sign in the zodiac format | **withdrawn by us** -- you were right | n/a |
| `gethostname` called with a variable declared only under `HPUNIX` | **fixed on `master`** | `#ifndef _WINDOWS` guard |
| `spmoon` used but never declared | **fixed on `master`** | `22cfd73` |

Verified against `master` as fetched on 2 August 2026, and since released in `v2.10.3bfinal`
(`f4dcd18e`) -- see the update at the end of this note.

## The two you fixed

`swetest.c` on `master` now declares the variable at file scope:

```c
static char spmoon[AS_MAXCH] = "9501";	// Jupiter Moon Io
```

and guards the call:

```c
#ifndef _WINDOWS
  gethostname (hostname, 80);
  if (strstr(hostname, "as80") != NULL)
    line_limit = 2 * 36525;
#endif
```

On the `#ifndef _WINDOWS` guard we owe you a correction. We wrote back questioning whether it
covers MSVC, on the grounds that `_WINDOWS` did not appear to be defined anywhere. You told us it
is, because `_WIN32` is defined by every known Windows C compiler and `sweodef.h` derives
`_WINDOWS` from it. You were right and our check was not a real one: we had compiled a probe with
no headers included, so nothing could have defined it. Compiled properly against your headers, the
guard does what you said it does. Our apologies for the noise.

## The one we withdrew

Astrodienst's answer on `dms()` was that a zodiacal position format is not a way to express an
angular difference, so `-d` with `-fZ` is an application-level error, and `-fL` or `-fl` is the
correct format. We checked that and it holds. `swetest.c` has three sites passing `BIT_ZODIAC`
(`:2241`, `:2527`, `:2532`); the two node-longitude sites return values normalised to 0-360,
verified across three bodies, all four output arrays and all three methods, so a differential value
is the only route by which a negative reaches that formatter. `swetest.c:534` documents `Z` as
`longitude ddsignmm'ss"`, a position format. The combination is a category error by the format's
own definition.

One narrow observation survives and we are not pressing it. Reaching that path still executes
`*(sp-1)`, a one-byte write before the start of a static buffer, which AddressSanitizer or `/RTCs`
will flag whether or not the input was sensible. Rejecting `-d` together with `-fZ` at
argument-parse time would close it and tell the user what they did wrong. Entirely your call; the
numbers were never the problem.

## A separate observation, unchanged

The guarded block doubles `line_limit` when the host is named `as80`, which reads as an Astrodienst
machine. It is harmless, but it means `swetest` has one output limit inside your network and
another everywhere else, which is awkward for anyone using `swetest` as a reference for expected
output. Dropping it from the public source, or driving it from a command-line option, would make
the limit the same everywhere.

---

## The request: a tag that builds

`v2.10.3final` is still the newest tag, and it does not build, on any platform. `spmoon` is used at
`swetest.c:1139`, `:1140` and `:1621` and declared nowhere, which is a constraint violation no
compiler accepts.

```sh
git clone https://github.com/aloistr/swisseph
cd swisseph
git checkout v2.10.3final
make swetest
```

On Windows that also stops on the unguarded `gethostname`. Isolating the one translation unit
removes any linker or library question:

```
cl /nologo /c /O2 /fp:precise /MD swetest.c
```

Both fixes have been on `master` since June, but the tag is what people fetch, package and vendor,
so anyone starting from the current release still hits the break. A patch release would settle it.
Failing that, a line in the release notes saying `swetest` does not build from `v2.10.3final`, and
pointing at `master`, would save the next person the bisect.

We work around it downstream by patching a copy: we adopt your own `spmoon` declaration verbatim,
`"9501"`, so our reference binary and yours agree on what `-pv` means without an `-xv`.

One thing worth knowing if you do cut a patch release from the tag rather than from `master`: the
`gethostname` guard cannot be cherry-picked on its own. `_WINDOWS` does not appear anywhere in
`sweodef.h` at `v2.10.3final`, so `#ifndef _WINDOWS` is true there and the block still compiles.
Adding the `#define` to compensate reaches two other places in that tree:
`swephexp.h:615` pulls in `<windows.h>` and declares `extern HANDLE dllhandle`, which is set by
`swedllst::DllMain` and so is not there in a static build; and `swetest.c:3944` switches `do_printf`
from `fputs(info, stdout)` to `fprintf(fp, info)`, which moves every line `swetest` prints off
stdout. Both are fine on `master`, where the rest of the tree expects `_WINDOWS`. We guard on
`HPUNIX` in our own patched copy for exactly that reason, and mention it only so a backport does not
surprise you.

---

## Update, 2 August 2026: `v2.10.3bfinal` released, and one thing we got wrong above

Thank you for cutting the patch release. We have moved our pin from `v2.10.3final` to
`v2.10.3bfinal` (`f4dcd18e`) and rebuilt against it: `spmoon` is declared, `gethostname` is
guarded, and `swetest.c` compiles clean of both defects this report was about. Every library
translation unit (`sweph.c`, `swephlib.c`, `swecl.c`, `swehouse.c`, `swejpl.c`, `swehel.c`,
`swedate.c`, `swemmoon.c`, `swemplan.c`) and `swephexp.h` are byte-identical to `v2.10.3final`, so
this did not touch anything our port measures itself against beyond the two fixes and the six
ephemeris data files that also changed in the same release.

One correction to what we told you above, though. In "One thing worth knowing" we said `do_printf`
switching to `fprintf(fp, info)` under `_WINDOWS` was "fine on `master`, where the rest of the tree
expects `_WINDOWS`". We had not actually compiled that branch when we wrote that -- only reasoned
about it from the diff -- and it was wrong. `fp` (`swetest.c:3958`) is declared nowhere in
`swetest.c`, on `master` same as on the tag (`git ls-remote` shows `master` and `v2.10.3bfinal` at
the same commit), so `#define _WINDOWS` activates a second hard compile error the moment the first
one (`gethostname`) is fixed:

```
swetest.c(3959): error C2065: 'fp': undeclared identifier
```

`do_printf` is called by every line `swetest` prints, so this is not a corner case -- it is the
whole program's output path. We are working around it downstream the same way as before, patching
a copy: substituting `fputs(info, stdout)` for the `fprintf(fp, info)` call, matching what the
`#else` branch has always done. Sorry for the bad information last time; we should have compiled
it before saying it was fine.

---

Reported from the SwissEphNet fork (`https://github.com/Tim81/SwissEphNet`), a C# port of the
Swiss Ephemeris tracking `v2.10.3bfinal`. Original port by Yan Grenier; fork work by
Timothy van der Ham.
