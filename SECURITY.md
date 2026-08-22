# Security Policy

## Supported Versions

Security fixes are applied to the latest release only. Please update to the
[latest release](https://github.com/DevPossible/DesktopPossible/releases)
before reporting an issue.

| Version | Supported |
| ------- | --------- |
| Latest release | :white_check_mark: |
| Older releases | :x: |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report privately via [GitHub Security Advisories](https://github.com/DevPossible/DesktopPossible/security/advisories/new)
on this repository.

### What to Include

- Type of issue and its impact
- Affected source file(s) and location (tag/branch/commit or direct URL)
- Step-by-step instructions to reproduce
- Any special configuration required
- Proof-of-concept code, if available

### Response Expectations

- Acknowledgement of your report within 3 business days
- A more detailed response within 7 days indicating next steps
- Updates as we progress toward a fix, and notification when it ships

## Scope Notes

DesktopPossible is a **local desktop utility**. It does not run a server,
expose network endpoints, or collect telemetry. Its only network activity is an
update/announcement check that fetches a static JSON manifest from this
repository (`remote/getversion.json` via `raw.githubusercontent.com`). The app
reads and writes its own configuration files beside the executable and, when
the start-with-Windows option is enabled, a per-user autostart registry entry.

We appreciate responsible disclosure — it helps protect all users.
