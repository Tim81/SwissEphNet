/*
 * Prints raw IEEE-754 bit patterns for a fixed set of C runtime math calls, so
 * scripts/verify-crt-parity.ps1 can diff them against Tools/CrtParity's .NET
 * output line for line.
 *
 * WHY THIS FILE EXISTS
 *
 * The bit-exact oracle this repo is building toward (Tools/CReference/build-c.ps1,
 * scripts/validate-c-reference.ps1) rests on one assumption that is never itself
 * checked anywhere: that MSVC C and .NET compute the same trig/exp/log/pow bits on
 * Windows x64. That assumption is not a coincidence and not luck. On Windows x64,
 * CoreCLR implements Math.Sin/Cos/Tan/Asin/Acos/Atan/Atan2/Exp/Log/Pow as internal-call
 * FCALLs in src/coreclr/vm/floatdouble.cpp whose bodies are literally `return sin(x);`
 * and so on -- calls into the C runtime, not managed code. On this platform that C
 * runtime is ucrtbase.dll, and an MSVC build compiled /MD binds the very same DLL (see
 * build-c.ps1's own .DESCRIPTION for the full argument). Math.Sqrt is the one exception:
 * the JIT emits the sqrtsd instruction directly, which is exactly rounded per IEEE 754,
 * and MSVC emits the same instruction, so it agrees for a different reason than the rest.
 *
 * Measured once, by hand, over 200 values: MSVC C /MD and .NET 10 produced zero bit
 * differences. That measurement lived nowhere durable. If a future .NET release moves
 * any of these functions to managed code -- other parts of the BCL's math surface have
 * gone that way -- or if ucrtbase.dll servicing ever diverges from what MSVC links
 * against, the port would start failing the conformance oracle for a reason that has
 * nothing to do with sweph.c, and nothing in the repo would say why. This program, run
 * through verify-crt-parity.ps1, turns that one-time measurement into something the repo
 * re-checks.
 *
 * VALUE SELECTION
 *
 * Three input tables cover the argument domains the functions below actually accept:
 *
 *   g_values       -- general spread, fed to sin/cos/tan/atan/exp/floor/ceil. Includes
 *                      zero and signed zero, small values near the precision floor,
 *                      +-1.0 and its immediate float neighbors (the values right at the
 *                      last-bit boundary are where a rounding difference is most likely
 *                      to show up), pi/2 and pi themselves, and 1e10/1e15 -- large enough
 *                      that argument reduction for sin/cos/tan has real work to do and a
 *                      library-specific reduction algorithm would show up as a bit
 *                      difference here first. Also two real quantities from this port's
 *                      own domain: an obliquity of the ecliptic near 23.4392911 degrees,
 *                      and a Julian day near 2451545.0 (J2000).
 *
 *   g_unit_domain  -- restricted to [-1, 1], fed only to asin/acos. Both are undefined
 *                      outside that range, and an out-of-domain call returns NaN with an
 *                      implementation-chosen payload -- two NaNs can be unequal bit for
 *                      bit while both are perfectly correct, which would make this
 *                      program report a difference that is not a real one. Every value
 *                      here is a value g_values also contains restricted to the legal
 *                      range, plus the exact domain edges -1.0 and 1.0.
 *
 *   g_non_negative -- fed to log/log10/sqrt, all undefined (or NaN-producing) for
 *                      negative arguments, for the same NaN-payload reason as above.
 *                      log(0) and log10(0) are well-defined (both give -infinity, whose
 *                      bit pattern IS fully specified by IEEE 754), so 0.0 stays in.
 *
 * atan2/pow/fmod take two arguments, so each gets its own table of pairs rather than
 * being driven off the tables above. The atan2 pairs include signed-zero combinations,
 * since atan2's sign convention depends on the sign of a zero argument. The pow pairs
 * avoid a negative base with a non-integer exponent (NaN again) but include a negative
 * base with an integer exponent, which is well-defined. The fmod pairs never divide by
 * zero.
 *
 * OUTPUT FORMAT
 *
 * One line per call: the function name, a tab, and the sixteen-digit lowercase hex bit
 * pattern of the result, obtained via memcpy into a uint64_t rather than a pointer cast
 * through a union or a reinterpret -- the memcpy form has no strict-aliasing hazard and
 * is what the C standard actually guarantees works.
 *
 * KEEPING THIS FILE AND Tools/CrtParity/Program.cs IN STEP
 *
 * The two files must call the same functions over the same values in the same order --
 * that correspondence is the entire gate. There is no shared source the two are
 * generated from; verify-crt-parity.ps1 only checks that the two output streams already
 * agree line for line, which detects a value added on one side and not the other (the
 * line counts stop matching) but would NOT reliably detect one file's array holding a
 * different value than the other's at the same position and the two arrays somehow
 * ending up the same length -- that reads as a real CRT difference and fails loudly,
 * just for the wrong reason. Whoever edits one of these two files must edit the other
 * to match, by hand, in the same commit.
 */

#include <stdio.h>
#include <string.h>
#include <math.h>
#include <stdint.h>

static const double g_values[] = {
    0.0, -0.0,
    1e-10, -1e-10,
    0.5, -0.5,
    0.9999999999999999, 1.0000000000000002,
    1.0, -1.0,
    1.5707963267948966, -1.5707963267948966,
    3.141592653589793, 2.718281828459045,
    1e10, -1e10,
    1e15, -1e15,
    23.4392911, -23.4392911,
    2451545.0, -2451545.0,
    100.0, -100.0,
    0.1, 10.0
};
#define VALUES_COUNT (sizeof(g_values) / sizeof(g_values[0]))

static const double g_unit_domain[] = {
    0.0, -0.0,
    1e-10, -1e-10,
    0.1, -0.1,
    0.5, -0.5,
    0.7071067811865476, -0.7071067811865476,
    0.9999999999999999, -0.9999999999999999,
    1.0, -1.0
};
#define UNIT_DOMAIN_COUNT (sizeof(g_unit_domain) / sizeof(g_unit_domain[0]))

static const double g_non_negative[] = {
    0.0,
    1e-10,
    0.1,
    0.5,
    0.9999999999999999, 1.0000000000000002,
    1.0,
    1.5707963267948966,
    3.141592653589793, 2.718281828459045,
    1e10, 1e15,
    23.4392911,
    2451545.0,
    100.0,
    10.0
};
#define NON_NEGATIVE_COUNT (sizeof(g_non_negative) / sizeof(g_non_negative[0]))

typedef struct { double a; double b; } pair_t;

static const pair_t g_atan2_pairs[] = {
    { 0.0, 1.0 }, { 1.0, 0.0 }, { 0.0, -1.0 }, { -0.0, 1.0 },
    { 1.0, 1.0 }, { -1.0, -1.0 },
    { 23.4392911, 2451545.0 },
    { 1e15, 1e10 }, { -1e10, 1e15 },
    { 0.5, -0.5 },
    { 3.141592653589793, 2.718281828459045 },
    { -1.0, 0.0 },
    { 1e-10, 1e-10 }
};
#define ATAN2_COUNT (sizeof(g_atan2_pairs) / sizeof(g_atan2_pairs[0]))

static const pair_t g_pow_pairs[] = {
    { 2.0, 10.0 }, { 2.0, 0.5 }, { 10.0, -1.0 }, { 0.5, 0.5 },
    { 1.0000000000000002, 1e15 },
    { 23.4392911, 2.0 }, { 2451545.0, 0.5 },
    { 0.0, 0.0 }, { 0.0, 2.0 }, { 2.0, 0.0 },
    { -2.0, 3.0 }, { -2.0, 2.0 },
    { 1e10, 0.1 }
};
#define POW_COUNT (sizeof(g_pow_pairs) / sizeof(g_pow_pairs[0]))

static const pair_t g_fmod_pairs[] = {
    { 5.5, 2.0 }, { -5.5, 2.0 }, { 5.5, -2.0 },
    { 2451545.0, 365.25 },
    { 1e15, 1e10 },
    { 23.4392911, 1.0 },
    { 0.1, 0.03 },
    { 7.0, 7.0 },
    { 1e-10, 3.0 },
    { 100.0, 0.1 }, { -100.0, 3.0 },
    { 3.141592653589793, 1.0 }
};
#define FMOD_COUNT (sizeof(g_fmod_pairs) / sizeof(g_fmod_pairs[0]))

static uint64_t bits_of(double x)
{
    uint64_t bits;
    memcpy(&bits, &x, sizeof bits);
    return bits;
}

static void emit(const char *name, double result)
{
    printf("%s\t%016llx\n", name, (unsigned long long)bits_of(result));
}

int main(void)
{
    size_t i;

    for (i = 0; i < VALUES_COUNT; i++) emit("sin", sin(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("cos", cos(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("tan", tan(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("atan", atan(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("exp", exp(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("floor", floor(g_values[i]));
    for (i = 0; i < VALUES_COUNT; i++) emit("ceil", ceil(g_values[i]));

    for (i = 0; i < UNIT_DOMAIN_COUNT; i++) emit("asin", asin(g_unit_domain[i]));
    for (i = 0; i < UNIT_DOMAIN_COUNT; i++) emit("acos", acos(g_unit_domain[i]));

    for (i = 0; i < NON_NEGATIVE_COUNT; i++) emit("log", log(g_non_negative[i]));
    for (i = 0; i < NON_NEGATIVE_COUNT; i++) emit("log10", log10(g_non_negative[i]));
    for (i = 0; i < NON_NEGATIVE_COUNT; i++) emit("sqrt", sqrt(g_non_negative[i]));

    for (i = 0; i < ATAN2_COUNT; i++) emit("atan2", atan2(g_atan2_pairs[i].a, g_atan2_pairs[i].b));
    for (i = 0; i < POW_COUNT; i++) emit("pow", pow(g_pow_pairs[i].a, g_pow_pairs[i].b));
    for (i = 0; i < FMOD_COUNT; i++) emit("fmod", fmod(g_fmod_pairs[i].a, g_fmod_pairs[i].b));

    return 0;
}
