# Security Policy

Platform.SharedKernel ships security-sensitive building blocks: encryption, password hashing,
signing, TOTP, OIDC/DPoP/mTLS/API-key authentication, tenant isolation and outbound webhook
delivery. Vulnerability reports are taken seriously and handled privately until a fix is available.

## Reporting a vulnerability

**Do not open a public GitHub issue, discussion or pull request for a security problem.**

Report it privately through one of these channels:

1. **GitHub private vulnerability reporting (preferred)** — open the repository's **Security** tab
   and choose **Report a vulnerability**.
2. **Email** — [dinc.dincer.business@gmail.com](mailto:dinc.dincer.business@gmail.com), with a
   subject starting with `[SECURITY]`.

Please include:

- the affected package(s) and version
- a description of the issue and its impact (what an attacker gains)
- steps to reproduce, or a minimal proof of concept
- any configuration the issue depends on
- whether you would like to be credited, and under what name

## What to expect

This is an independently maintained project, so the timelines below are best-effort targets
rather than contractual guarantees.

| Step | Target |
|------|--------|
| Acknowledgement of your report | within 5 business days |
| Initial assessment (confirmed, needs more info, or declined) | within 14 days |
| Fix for a confirmed critical or high-severity issue | as soon as practical, typically within 30 days |

You will be kept informed as the report progresses. If a report is declined, you will be told why.

## Supported versions

Every package in this repository ships under **one shared version**, derived from a single git tag.
Security fixes are released as a new version of the whole package set.

| Version | Supported |
|---------|-----------|
| Latest published release | ✅ |
| Any older release, including earlier pre-releases | ❌ |

The project is currently in pre-release (`1.0.0-alpha.*`). No fixes are backported to older
versions; upgrade to the latest release to receive them.

## Scope

**In scope** — vulnerabilities in the code in this repository, for example:

- weaknesses in the cryptography primitives (encryption, key handling, hashing, signing,
  HMAC, TOTP/HOTP, fixed-time comparison, secure random generation)
- authentication or authorization bypass in the OIDC, DPoP, mTLS, API-key or TOTP providers,
  or in the application and presentation authorization checks
- cross-tenant data access through caching, persistence, search, vector storage, workflows
  or tenant resolution
- server-side request forgery (SSRF) in webhook delivery
- secrets, keys or personal data reaching logs, telemetry or error responses
- insecure defaults, where the out-of-the-box configuration is exploitable

**Out of scope:**

- misconfiguration in a consuming application
- issues that only occur after a protection has been deliberately disabled, such as
  `AllowPrivateNetworkTargets`, weakened `RevocationMode`/`AllowedCertificateTypes` settings,
  or a CORS policy combining any-origin with credentials
- vulnerabilities in third-party dependencies — please report those to the upstream project
  (a report explaining how this repository exposes such a vulnerability is still welcome)
- the sample applications under `samples/`, which are for demonstration only
- denial of service that requires unrealistic traffic volumes, and findings from automated
  scanners without a demonstrated impact

## Disclosure

This project follows coordinated disclosure:

1. The report is confirmed and a fix is prepared privately.
2. A fixed release is published.
3. A GitHub Security Advisory is published describing the issue, affected versions and the fix,
   crediting the reporter unless they prefer to stay anonymous.

Please allow a fix to be released before disclosing the issue publicly. If you believe a report
is not being handled, you may follow up by email.

## Safe harbor

Good-faith security research that follows this policy is welcome. Please test only against your
own deployments, avoid accessing or modifying other people's data, and do not degrade services
you do not own. No legal action will be pursued against researchers acting in good faith under
these terms.
