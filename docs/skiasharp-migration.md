# SkiaSharp 4 migration

DrawingSpace's managed renderer, rich-text engine, test and benchmark native
assets, and browser/desktop host native references use SkiaSharp 4.153.1.
The Uno SDK remains 6.7.30 / Uno WinUI 6.7.135; HarfBuzzSharp remains 8.3.1.3.
This change integrates Dependabot PR #7 instead of merging its version-only
update without the required source and native-host changes.

## Browser native integration

SkiaSharp.NativeAssets.WebAssembly 4.153.1 supplies Dawn's emdawnwebgpu port.
Its linker inputs contain C++20 source and flags. Uno also supplies a raw C
OpenGL shim in that same emcc invocation. The application target
`DrawingSpacePrepareUnoGlShim`, before `_UnoAdjustCompatibility`, generates
`obj/.../native/uno_gl_shim.cpp` including the unchanged NuGet C source under
`extern "C"`. System headers are included first, outside the linkage block,
because Emscripten's headers contain C++ templates.

The shim basename, P/Invoke exports, signature primers, unsized-to-sized
texture-format promotion, WebGL2 support and Dawn inputs are retained. The
NuGet cache is not modified. Undefined-symbol errors remain enabled. The
target requires exactly one upstream C shim so a future upstream change is
reported rather than silently dropping native integration.

## Text and ownership

Removed SKPaint font-state and measurement calls now use SKFont. Overhang
measurement uses the actual glyph slice, not the unrelated code-point start.
Common measurements use bounded stack storage.

Four unchanged Open Sans faces from Uno.Fonts.OpenSans 2.9.4 are embedded as
headless fallbacks. They are process-lifetime shared objects, excluded from
per-engine disposal. Resolver-supplied typefaces remain caller-owned. The
Open Sans copyright and OFL-1.1 license are packaged and embedded alongside
these resources in `DrawingSpace.Text`.

Stream-backed fonts use the full existing HarfBuzz shaping path. A platform
font with no readable stream uses the conservative SKFont code-point path;
that path is not equivalent to full OpenType shaping and does not provide
contextual substitutions or mark positioning. Use a stream-backed resolver
for complex-script fidelity. This migration does not establish complete
Visio typography or pixel-identical cross-platform rendering.

## Validation

All existing engine, browser, desktop and performance gates remain enabled.
`SkiaMigrationTests` adds missing-family coverage for all four style variants,
shared/caller-owned font lifetimes, independent glyph-slice offsets, and the
embedded license. The production browser build validates the real C++/C ABI
integration rather than accepting a managed-only compilation result.

Upstream sources:
- https://github.com/mono/SkiaSharp/blob/v4.153.1/binding/SkiaSharp.NativeAssets.WebAssembly/buildTransitive/SkiaSharp.targets
- https://github.com/unoplatform/uno/blob/6.7.135/src/Uno.UI.Runtime.Skia.WebAssembly.Browser/build/native/uno_gl_shim.c
- https://github.com/unoplatform/uno.fonts/tree/2.9.4/nuget/OpenSans/Uno.Fonts.OpenSans
- https://github.com/googlefonts/opensans/blob/main/OFL.txt
