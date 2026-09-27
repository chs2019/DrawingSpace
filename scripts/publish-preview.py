#!/usr/bin/env python3
"""Publish an acceptance-test screenshot as the README preview, never a generated mock-up."""
from pathlib import Path
import shutil

source = Path('artifacts/screenshots/DrawingSpace-workspace.png')
target = Path('artifacts/site/preview.png')
if not source.is_file():
    raise SystemExit('The accepted workspace screenshot is missing.')
shutil.copyfile(source, target)
