# ADR-0004: System.Text.Json without source generation, JsonNode for open values

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Follows:** ADR-0001 (target frameworks)

## Context

The wire format has three awkward shapes.

1. **Open values.** `state`, `instructions`, every Choice option description, every Score level,
   and both Noul criteria accept a string, a JSON object, or a JSON array (OpenAPI document at
   `https://api.typesafe.ai/openapi.json`, version 0.2.0). Only `instructions`, Choice option
   descriptions and the Noul `true` and `false` criteria also allow null. `state`, Score levels
   and the response's `legend` values do not. `instructions` is optional in the schema for all
   three question types, although the docs mark it required.
2. **Polymorphic answers.** `answers` is a map of `Answer`, a `oneOf` with a `type`
   discriminator (`noul`, `choice`, `score`). The discriminator is not guaranteed to be the first
   property.
3. **Forward compatibility.** The official Python SDK logs and skips an answer kind it does not
   know. The official JavaScript SDK passes unknown fields through. A new answer kind must not
   break a deployed .NET app.

`TypeSafe.AI.Sdk` types these values as `object?` and serializes with reflection
(`JsonSerializer.SerializeToNode(value, value.GetType())`). That works, but it cannot be
trimmed or compiled ahead of time, and an unknown answer kind fails the whole response.

## Decision

1. **System.Text.Json only.** No Newtonsoft.Json. On `netstandard2.0` this adds the
   `System.Text.Json` 10.0.x package. On `net10.0` it is in the box.
2. **No `JsonSerializerContext` in 0.1.** Requests are written with `Utf8JsonWriter`, and caller
   nodes go through `JsonNode.WriteTo`. Responses are read with `JsonDocument`. Every required
   field is checked in one place, and a failure throws `TypeSafeResponseValidationException` with
   the JSON path, for example `$.answers.department.confidence`.

   This is AOT safe by construction, has no source generator settings to get wrong, and the
   answer union needs hand-written mapping either way. The package sets `IsAotCompatible=true`
   on `net10.0`, and the packaging job publishes a NativeAOT consumer with zero trim or AOT
   warnings (PLAN.md section 6).
3. **`JsonNode` where the schema forbids null, `JsonNode?` where it allows null or omission.**
   So `state` and Score levels are `JsonNode`; `instructions`, Choice option descriptions and Noul
   criteria are `JsonNode?`. `JsonNode` has an implicit conversion from `string`, so the common
   case stays plain:

   ```csharp
   Question.Noul("Does this convey urgency?");                  // string
   Question.Noul(new JsonObject { ["question"] = "Is `a` the same person as `b`?",
                                  ["a"] = resumeA, ["b"] = resumeB });
   ```

   Callers with a POCO convert it themselves with
   `JsonSerializer.SerializeToNode(value, MyContext.Default.MyType)`, which is AOT safe and lets
   them pick the naming policy. The README shows that line. The SDK has no helper for it and no
   reflection path.
4. **Questions are immutable and safe to share.** The question factory methods `DeepClone()`
   every `JsonNode` they receive. Serialization writes caller nodes with `WriteTo` and never adds
   them to a new `JsonObject`, which would throw or re-parent them. Checked on 2026-09-24: writing
   the same caller nodes from 8 threads leaves `Parent == null`.
5. **Hand-written answer mapping instead of `[JsonPolymorphic]`.** `ResponseReader` reads each
   answer as a `JsonElement`, switches on `type`, and maps the known kinds by hand. Anything else
   becomes `UnknownAnswer`, which keeps the raw `JsonElement` and the `type` string. A warning is
   logged once per unknown kind per client.

   `[JsonPolymorphic]` was the first choice and was tested on 2026-09-24 against System.Text.Json
   10.0 with source generation. With `AllowOutOfOrderMetadataProperties` and
   `IgnoreUnrecognizedTypeDiscriminators = true` it does deserialize known kinds out of order and
   falls back to the base type for unknown ones. Two things rule it out:

   - The discriminator is consumed as metadata, so the fallback instance cannot tell the caller
     which kind it was. `[JsonExtensionData]` on the base captures the other fields, not `type`.
   - Declaring a `type` property on the base to recover it throws `InvalidOperationException` at
     contract build time (property name conflicts with the discriminator).
6. **Answer shapes are sealed classes with get-only properties and `IReadOnlyDictionary`
   collections.** Tests build them through `TypeSafeModelFactory` (ADR-0007).

   `ScoreAnswer.Probabilities` and `Legend` are keyed by `int` level (parsed from the wire's
   string keys), because the API defines them as positions in an ordered array. A malformed key
   raises `TypeSafeResponseValidationException` with the JSON path, never a raw
   `InvalidOperationException` or `FormatException`. `Legend` values are `JsonElement` clones, so
   they outlive the response document.
7. **Unknown response fields are ignored.** Unknown request fields can be sent through
   `SystemOneRequest.ExtraBody` (a `JsonObject`), mirroring Python's `extra_body`. A key that
   collides with `state`, `model` or `questions` throws `ArgumentException`.
8. **`Question.FromJson(JsonObject question)` is the raw escape hatch** for a question shape
   the SDK does not model yet. It deep clones the object, and `type` must be a non-empty string,
   else `ArgumentException`. The result is an internal sealed `RawQuestion` whose `Type` is that
   string; the writer emits the clone as is and never re-parents it. This matches Python's raw
   question dictionaries and the JS SDK's pass-through. A test checks the round trip and
   `Parent == null`.

## Consequences

The package is trim and AOT clean on `net10.0`. Among the packages reviewed, only `Jev.Net`
proves the same in CI, and it does not target `netstandard2.0` (see PLAN.md section 2.3). An
unknown answer kind degrades to one warning, not an outage.

Callers who want to pass arbitrary objects pay one line of `JsonSerializer.SerializeToNode`.
That is intentional. Accepting `object?` would put reflection on the default path.

`JsonNode` is mutable. Questions clone their nodes when built, so one question can be reused
across calls and threads. `state` is not cloned; the XML docs on `SystemOneRequest` say not to
mutate it during a call.

## Alternatives considered

**A source-generated `JsonSerializerContext` for every type the SDK owns.** The first draft of
this ADR. Rejected for 0.1 for the reasons in decision 2.

**`object?` everywhere with reflection serialization.** Rejected. It is the easiest API to call
and the one that cannot be trimmed.

**A custom `JsonValue`-like union struct with implicit conversions from string, `JsonObject`,
`JsonArray`.** Rejected. It duplicates `JsonNode` and every caller already knows `JsonNode`.

**`JsonElement`.** Rejected. Immutable and correct, but building one from code needs a
`JsonDocument` round trip, which is hostile for the common case of building instructions inline.

**Newtonsoft.Json.** Rejected. Larger, reflection only, and not in the box on modern .NET.
