# Security

## Supported versions

Wend Your Way is pre-release. Only the latest commit on `main` is supported.

| Version | Supported |
|---|---|
| `main` (latest) | Yes |
| anything older | No |

## What's sensitive here

The app holds the user's home location and full activity history. It binds to `127.0.0.1` by default, and access over home Wi-Fi is an explicit opt-in. PostgreSQL listens on localhost only. The only secret is the Thunderforest API key, kept in `dotnet user-secrets` and never committed.

## Reporting a vulnerability

Please **don't open a public issue** for a security problem. Report it privately through [GitHub Security Advisories](https://github.com/DamianCannon/wend-your-way/security/advisories/new) instead.

Expect an acknowledgement within 5 business days, and a fix or a decision within 30 days.
