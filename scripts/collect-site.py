#!/usr/bin/env python3
"""Collect the Uno publish root and stamp the exact source revision."""
import argparse
import json
import os
import shutil
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('publish', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
candidates = sorted(args.publish.rglob('index.html'), key=lambda p: len(p.parts))
if not candidates:
    raise SystemExit(f'No index.html below {args.publish}')
source = candidates[0].parent
args.output.mkdir(parents=True, exist_ok=True)
shutil.copytree(source, args.output, dirs_exist_ok=True)
(args.output / '.nojekyll').touch()
(args.output / 'build-info.json').write_text(json.dumps({
    'application': 'DrawingSpace', 'host': 'Uno WebAssembly',
    'version': os.environ.get('VERSION', '0.1.0-alpha.1'),
    'commit': os.environ.get('GITHUB_SHA', 'local')
}))
print(f'Collected {source} into {args.output}')
