# Contributing

Use the SDK pinned in `global.json`. Keep platform services in the application host, not the document/editor libraries. Keep one main public type per file and follow `.editorconfig`.

Run `dotnet test tests/DrawingSpace.Tests -c Release` for every engine change. Rendering or interaction changes also require a Release browser publish and `npm run test:browser`, following the README. Review the actual screenshots; compilation alone does not validate visual behavior. Run the desktop matrix before releasing UI API changes.

For model changes, update validation, source-generated JSON metadata, compatibility documentation, and round-trip fixtures. Do not silently reinterpret existing version-1 documents. Mutating editor commands must use `EditorSession` transactions and preserve undo/redo and stable identities. Never keep stale shape references across undo.

A bug report should include platform/browser versions, source commit from `build-info.json`, a minimal non-sensitive drawing, exact reproduction steps, expected behavior, and screenshots where relevant. Remove personal and proprietary data before uploading drawings.

Third-party assets require an appropriate license and notice. Do not copy proprietary Microsoft icons, templates, source code, or fonts into this repository. Original interoperable behavior should be validated against documented public formats and reproducible fixtures, not claimed from visual similarity alone.
