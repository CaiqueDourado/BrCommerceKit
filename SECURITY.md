# Security

Please report vulnerabilities privately through GitHub: **Security → Report a vulnerability** on this repository. Do not open a public issue.

Supported versions: the latest release.

Notes for users:

- The XML reader disables DTD processing and external entity resolution.
- `PageFetcher` limits how many bytes it reads from each response (`DetectorOptions.MaxPageBytes`, `MaxFeedProbeBytes`).
- Never commit API keys or credentials; this repository has secret scanning enabled.
