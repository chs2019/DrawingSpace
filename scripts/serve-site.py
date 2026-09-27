#!/usr/bin/env python3
"""Serve the same /DrawingSpace/ base path used on GitHub Pages."""
import argparse
import functools
import http.server
from urllib.parse import unquote

parser = argparse.ArgumentParser()
parser.add_argument('--directory', default='artifacts/site')
parser.add_argument('--port', default=4173, type=int)
args = parser.parse_args()

class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.webmanifest': 'application/manifest+json', '.mjs': 'application/javascript'}
    def translate_path(self, path):
        if path.startswith('/DrawingSpace/'):
            path = path[len('/DrawingSpace'):]
        return super().translate_path(path)
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()

http.server.ThreadingHTTPServer(('127.0.0.1', args.port), functools.partial(Handler, directory=args.directory)).serve_forever()
