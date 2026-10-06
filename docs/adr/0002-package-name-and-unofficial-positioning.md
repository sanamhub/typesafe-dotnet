# ADR-0002: Package name TypeSafeSharp, positioned as unofficial

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Decides:** PLAN.md open question Q1

## Context

TypeSafe publishes official SDKs for JavaScript (`@typesafe-ai/sdk`) and Python (`typesafe-sdk`),
both MIT. There is no official .NET SDK.

In the two weeks since Jev launched, at least seventeen community .NET packages have appeared.
Several of their IDs are close enough to confuse each other, and to look official:

| ID | Owner | Downloads (2026-09-24) |
| --- | --- | --- |
| `JevSharp` | bariskisir | 18,296 (about 18k in three days, 0 stars; probably not organic) |
| `tryAGI.TypeSafeAI` | tryAGI | 768 |
| `Jev.Net` | Brightshore | 319 |
| `TypeSafe-AI.Sdk` | Stef Heyenrath | 250 |
| `TypeSafe.AI.Sdk` | hardkoded | 226 |
| `TypeSafeAI` | Hawxy | 208 |
| `TypeSafe.AI` | "TypeSafe .NET Contributors" | 191 |
| `TypeSafe.Jev`, `Adjudge.Jev`, `Kassad.TypeSafe`, `TimDinh.TypeSafe`, `ElBruno.AI.Jev`, `Supprocom.TypeSafeAI`, `Zilpio.TypeSafe.Jev`, and others | various | under 130 each |

The `TypeSafe.` prefix is not reserved on nuget.org. Unrelated packages already use it
(`TypeSafe.Http.Net.*`, about 60k downloads, a REST library from 2018). If TypeSafe the company
reserves a prefix later, existing IDs keep publishing, but the reservation marks the vendor's
packages as verified and ours as not, which is correct.

The Anthropic ecosystem shows the cost of getting this wrong: the community `Anthropic.SDK` and
the official `Anthropic` package now coexist, and users regularly install the wrong one.

NuGet's authoring guidance: "DO choose a package ID that is unique and clearly differentiated".

## Decision

1. **Package ID and root namespace: `TypeSafeSharp`.** The DI package is
   `TypeSafeSharp.Extensions.DependencyInjection`. Both IDs returned 404 on nuget.org on
   2026-09-24.

   The `Sharp` suffix is the .NET community convention for an independent client of someone
   else's service or format (`RestSharp`, `OllamaSharp`, `ImageSharp`). It says ".NET port" and
   "not the vendor" in one word, sorts next to searches for "TypeSafe", and cannot be mistaken for
   the `TypeSafe.AI.*` family.
2. **Type names follow the official SDKs** (`TypeSafeClient`, `RetryPolicy`, `Noul`, `Choice`,
   `Score`) so TypeSafe's own docs read across. The namespace is what keeps them apart.
3. **Unofficial status is stated in five places:** the first line of the NuGet description
   ("Unofficial .NET client for TypeSafe AI's System One API and the Jev model."), the first
   paragraph of the README, a disclaimer at the end of the README (not affiliated with or
   endorsed by TypeSafe; names used only to say what the package talks to), the request headers (`User-Agent: TypeSafeSharp/<version>` and
   `X-TypeSafe-SDK: TypeSafeSharp/<version>`, never `typesafe-sdk/...`), and the GitHub repo
   description. No TypeSafe logo is used as the package icon.
4. **Attribution.** Behaviour ported from the MIT-licensed official SDKs is credited in
   `THIRD-PARTY-NOTICES.txt`, and each ported rule links to its source file and version in a code
   comment. The JavaScript SDK is credited with its copyright line, "Copyright (c) 2026 TypeSafe".
   The Python SDK's LICENSE copyright line is an unfilled placeholder, so it is credited as
   "typesafe-sdk (MIT, per pyproject.toml)".
5. **Reserve the IDs with the first publish,** `0.1.0-alpha.1` at P4 (PLAN.md section 7). The
   maintainer decided that an unofficial client needs no confirmation from TypeSafe (PLAN.md
   section 9, Q4, closed 2026-09-25).

## Consequences

The name is distinct from every package above and from the vendor's likely future `TypeSafe`
or `TypeSafe.Sdk` ID. If TypeSafe ships an official .NET SDK, this package is deprecated on
nuget.org pointing to it, and the README says so at the top (PLAN.md section 8, R1).

Renaming is a find and replace until the first publish. After it, a rename means a new ID and
deprecating the old one.

## Alternatives considered

**`TypeSafe.Community`.** Clear, but claims the vendor's name as the leading segment, which is
the exact collision the table above shows.

**Author prefix, `Sanamhub.TypeSafe`.** The safest option legally and the one the NuGet guidance
leans towards. Rejected on discoverability: nobody searches for the author. Kept as the fallback
if TypeSafe objects to `TypeSafeSharp`.

**`Jev.*` or `SystemOne.*`.** Rejected. Jev is the model, not the API, and will change name at
some point (`jev-2`?). System One is a model class name TypeSafe may apply to other products.

**Contribute to an existing package instead of shipping a new one.** Considered seriously, see
PLAN.md section 2.4. Not ruled out.
