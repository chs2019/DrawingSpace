# Security

DrawingSpace is an alpha local-first application. Do not use it as the sole store for critical drawings. Export editable JSON backups.

Native JSON is treated as data and validated before loading. Imported content is not evaluated as code. SVG export uses XML escaping. Image allocations and model sizes are bounded. CSV export quotes fields and prefixes values beginning with common spreadsheet formula characters. Browser file and clipboard operations use browser permission boundaries.

Recovery copies live in IndexedDB or the user's local application-data directory. They are not end-to-end encrypted, synchronized, or protected from another application running with the same user privileges. Clearing site data removes browser recovery. Drawings are not sent to an application backend; the static host still receives ordinary asset requests.

The optional `?test=1` diagnostics publish read-only local drawing state for acceptance tests. Do not share a debugging session containing sensitive diagrams. Disable this query parameter during ordinary work.

CI uses read-only permissions for pull-request builds. Deployment accepts successful trusted main-branch builds and verifies the artifact commit. Release and package publishing are separate explicit workflows; publishing credentials must be stored as repository secrets, never committed.

Report vulnerabilities privately through the repository's security reporting facility when available, or contact the maintainer privately. Do not post exploit drawings containing sensitive data in public issues. There is no production security audit or support SLA for this alpha.
