@AGENTS.md

## Exceptions to the org standard

Each exception needs a named approver (org standard section 14). The maintainer approves each
one; a row marked pending is not in force.

| Org rule | Exception | Why | Record | Approved by |
| --- | --- | --- | --- | --- |
| Section 5, Clean Architecture by default | One library project with folders | An HTTP client for two endpoints has no domain or infrastructure to separate | ADR-0003 | Sanam, 2026-10-06 |
| Section 1, no secrets in the repo (a reading, not an exception) | `TypeSafeSharp.snk` is committed | A strong-name key is an assembly identity, not a credential, so the rule does not apply; Microsoft's guidance is to check it in. Listed so the reading has a named approver. | ADR-0013 | Sanam, 2026-10-06 |
| Section 10, one approval (two for security, payment or migration), no self-approval | The single maintainer merges their own PRs. Mitigation: every PR runs full CI, and security-relevant PRs (auth, retries, release) wait 24 hours and get a `/code-review` pass before merge. | One person cannot approve their own work, so the rule cannot be met. The exception ends if this becomes company work (PLAN.md Q3). | This table | pending (Sanam) |
