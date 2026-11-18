# Security Policy

## Supported Versions

Security fixes go into the latest release. Before 1.0, that's the latest minor version: upgrade to it to get a fix
(see [Upgrading](https://xactjobs.com/docs/upgrading/)). After 1.0, the latest minor version of each supported major
version gets them.

## Reporting a Vulnerability

Please don't open a public issue. Report it privately on GitHub, in the repository's
[Security tab](https://github.com/XactJobs/XactJobs/security/advisories/new) ("Report a vulnerability").

Include what's affected (the package, `XactJobs` or `XactJobs.UI`, and its version), what an attacker can do, and how
to reproduce it. You'll get an answer within a week. Once it's fixed and released, the advisory is published, with
credit to you if you want it.

## What's in Scope

- **The dashboard (`XactJobs.UI`)**: its access checks (local requests only by default, or your authorization
  policy), antiforgery, and anything that shows or changes jobs without that access.
- **SQL built by XactJobs**: the claims, the leases and the scripts are parameterized; a way to inject SQL through
  them is a vulnerability.
- **Job arguments**: they're stored as JSON and deserialized to the job method's parameter types, never to a type
  named in the data.

Not in scope:

- **Writing to XactJobs' tables runs code.** A job row names the type and the method a worker calls, so whoever can
  insert or change a row there can call any method a job could be. Give that access only to the application, as for
  the rest of its data.
- A job's own code, and what it does with its arguments, is the application's.
