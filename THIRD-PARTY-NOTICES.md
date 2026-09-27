# Third-party notices

DrawingSpace's original source is MIT licensed; see LICENSE.

## RichTextKit

Copyright © 2019–2020 Topten Software and contributors. Licensed under Apache-2.0.
Pinned upstream: https://github.com/toptensoftware/RichTextKit/commit/c215125dbf23f521956d051619e19bd2ecd472f8

The source under `src/DrawingSpace.Text/Vendor` retains its copyright headers. DrawingSpace isolates namespaces, includes four Unicode trie tables and integrates the code with SkiaSharp 3/HarfBuzz. The upstream checksums and adaptation notice are retained in that directory. The complete Apache license is in `src/DrawingSpace.Text/LICENSE.Apache-2.0.txt` and included in the NuGet package. No upstream font files, screenshots or test documents are vendored.

Other dependencies remain separate NuGet packages with their own licenses, including Uno Platform, SkiaSharp and HarfBuzzSharp. The host acquires Open Sans through Uno's font package rather than storing font binaries in this repository.
