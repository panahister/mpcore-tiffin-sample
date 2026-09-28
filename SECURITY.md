# Security policy

This repository is a sample. It is meant to be read and run on a developer's machine, not deployed.

## What is deliberately not a secret here

Every user name, password and client secret in `infrastructure/` and `scripts/` belongs to containers the
scripts start on your machine: `tiffin` / `tiffin`, `admin` / `admin`, `<name>-lab`,
`lab-only-<service>-secret`, `tiffin-media` / `tiffin-media-lab`. They are published on purpose, so that the sample runs without any setup of
its own. Do not reuse one anywhere.

The scenarios sign people in with the password grant, so that a script can do it. A real client uses the
authorization code flow with PKCE.

## Reporting a vulnerability

A weakness in the sample that would mislead somebody who learns from it is worth reporting: a rule that
can be bypassed, a secret that reaches a log or a message, an endpoint that answers somebody it should
not. Open an issue.

A vulnerability in **MP Core** itself is reported privately, as
[MP Core's security policy](https://github.com/panahister/mpcore/blob/main/SECURITY.md) describes.
