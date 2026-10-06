# ADR-0013: Strong-name the assemblies

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Follows:** ADR-0001 (target frameworks)

## Context

.NET Framework reach is the headline reason this package targets `netstandard2.0` (ADR-0001). A
strong-named .NET Framework app or library cannot reference an unsigned assembly, so an unsigned
TypeSafeSharp would be closed to exactly the callers the target is for.

Microsoft's library guidance on strong naming
(https://learn.microsoft.com/dotnet/standard/library-guidance/strong-naming) says to "consider
strong naming", to "consider adding the key to source control", and "do not change the key".

Strong naming changes assembly identity. Adding it, or changing the key, after the first publish
is a binary break for every assembly compiled against the earlier one.

## Decision

1. **`SignAssembly=true` for both packages**, `TypeSafeSharp` and
   `TypeSafeSharp.Extensions.DependencyInjection`, with one key, `TypeSafeSharp.snk`, checked
   into the repo.
2. **The key is an identity, not a credential.** A strong name says which assembly this is; it
   does not prove who built it. Org rule 1 (no secrets or keys in output, secrets only in a vault)
   covers credentials, and this file is not one, so the rule does not apply to it. `CLAUDE.md`
   still lists it in the exceptions table so the reasoning has a named approver.
3. **`AssemblyVersion` is `<major>.0.0.0`:** `0.0.0.0` during 0.x, `1.0.0.0` for 1.x. This
   spares .NET Framework users binding redirects between releases of one major version.
   `FileVersion` and the package version carry the full version.
4. **Test projects are signed with the same key**, so `InternalsVisibleTo` names the public key.
   Tests need it for the internal constructor that takes a `TimeProvider` (ADR-0005).
5. **Decided before the first publish, never changed after.** The first publish is
   `0.1.0-alpha.1` (ADR-0002).

## Consequences

Strong-named .NET Framework apps can reference both packages. Apps moving between releases of one
major version need no binding redirect; the move from 0.x to 1.x changes `AssemblyVersion` once.

Anyone with the repo can build an assembly with the same strong name. What shows a package came
from this repo's release workflow is the provenance attestation (ADR-0011), not the strong name.

Every `InternalsVisibleTo` attribute carries the full public key, which is long but written once.

## Alternatives considered

**No strong name.** Rejected. It blocks strong-named .NET Framework apps, and it cannot be added
later without a binary break.

**`PublicSign` with only the public key checked in.** Rejected for now. `InternalsVisibleTo` and
local test runs get awkward without the private key. Fine to revisit.

**Authenticode signing.** Out of scope. The org has no code-signing certificate, and
provenance attestation covers origin (ADR-0011).
