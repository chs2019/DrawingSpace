#!/usr/bin/env python3
"""Pin and vendor the Apache-2.0 RichTextKit layout implementation, never its fonts or test assets."""
from pathlib import Path
import hashlib
import shutil
import subprocess
import tempfile

revision = 'c215125dbf23f521956d051619e19bd2ecd472f8'
output = Path('src/DrawingSpace.Text/Vendor')
with tempfile.TemporaryDirectory(prefix='drawingspace-text-') as temporary:
    root = Path(temporary)
    subprocess.run(['git', 'init', '-q', str(root)], check=True)
    subprocess.run(['git', '-C', str(root), 'remote', 'add', 'origin', 'https://github.com/toptensoftware/RichTextKit.git'], check=True)
    subprocess.run(['git', '-C', str(root), 'fetch', '--depth=1', 'origin', revision], check=True)
    subprocess.run(['git', '-C', str(root), 'checkout', '--detach', '--quiet', 'FETCH_HEAD'], check=True)
    actual = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != revision:
        raise RuntimeError('The upstream revision does not match the pinned source')
    source = root / 'Topten.RichTextKit'
    files = sorted(source.rglob('*.cs')) + sorted((source / 'Resources').glob('*.bin'))
    if not files or sum(p.stat().st_size for p in files) > 16 * 1024 * 1024:
        raise RuntimeError('Unexpected source size')
    output.mkdir(parents=True, exist_ok=True)
    provenance = []
    for path in files:
        relative = path.relative_to(source)
        if 'obj' in relative.parts or 'bin' in relative.parts:
            continue
        if path.is_symlink():
            raise RuntimeError('Symlinks are not accepted in vendored source')
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        original = path.read_bytes()
        provenance.append(hashlib.sha256(original).hexdigest() + '  ' + str(relative))
        if path.suffix == '.cs':
            text = original.decode('utf-8-sig').replace('Topten.RichTextKit', 'DrawingSpace.Text.Internal')
            target.write_text(text)
        else:
            target.write_bytes(original)
    shutil.copyfile(root / 'license.txt', output / 'LICENSE.txt')
    (output / 'UPSTREAM-SHA256SUMS').write_text('\n'.join(provenance) + '\n')
    (output / 'NOTICE.md').write_text(f'''# RichTextKit source attribution

RichTextKit is Copyright © 2019–2020 Topten Software and its contributors, licensed under Apache-2.0.

Upstream: https://github.com/toptensoftware/RichTextKit
Pinned revision: `{revision}`

DrawingSpace changes: namespace isolation and SkiaSharp 3 compatibility. Original source headers are retained.
The adjacent checksum manifest records the unmodified upstream files. Only C# source and Unicode data tables are included.
No upstream fonts, screenshots, samples, or test documents are distributed here.
''')
    print('Vendored', len(provenance), 'source/data files from', revision)
