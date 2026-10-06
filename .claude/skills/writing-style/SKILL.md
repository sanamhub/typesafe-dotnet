---
name: writing-style
description: House writing rules for this repo. Load before writing or editing any prose, including README, docs, ADRs, commit messages, PR descriptions, code comments, and XML doc comments. Use it as a final pass on anything already written. Trigger on "write the docs", "update the README", "commit this", "open a PR", "add comments", or any request that produces text a human will read.
---

# Writing style

Everything written here has to read like an engineer wrote it. Not a model.

## Hard rules

1. **No em dashes.** Use a period, a comma, parentheses, or a colon.
2. **No AI filler.** Banned outright: delve, leverage (as a verb), seamless, robust,
   comprehensive, cutting-edge, best-in-class, game-changing, unlock, empower, foster,
   navigate (figurative), realm, landscape, tapestry, "it's worth noting", "it's important to
   note", "in today's world", "at the end of the day".
3. **No closing summary that restates the section.** Stop when the point is made.
4. **No duplication.** If a fact appears in two places, one of them links to the other.
5. **Short paragraphs.** Three or four sentences. Break anything longer.
6. **Plain words.** "fix" not "implement a solution for". "use" not "utilise". "so" not
   "thereby". "start" not "commence".
7. **No hedging stacks.** Pick one: "probably", not "it may potentially be possible that".
8. **No triads for rhythm.** Three items only when there are genuinely three.

## Commits

- Conventional Commits: `type(scope): summary`. Types: feat, fix, docs, chore, refactor, test,
  build, ci, perf.
- Summary in the imperative, lower case, no trailing period, under 72 characters.
- Body explains why, not what. The diff already says what.
- **Never add `Co-Authored-By`, `Generated with`, or any other AI attribution.**

Good:

```
feat(http): honour retry-after-ms before Retry-After

Matches typesafe-sdk-js 0.6.0, which reads the millisecond header first.
Retry-After in seconds alone rounds every wait up to a whole second.
```

Bad:

```
feat: 🚀 Implement comprehensive retry handling

This commit leverages cutting-edge techniques to seamlessly integrate...

Co-Authored-By: Claude <noreply@anthropic.com>
```

## Pull request descriptions

No `Summary` or `Changes` headings. The shape, from ada-csharp's merged PRs:

1. One opening paragraph: what changes and why, in two or three sentences.
2. A bullet per file or area, each saying what changed there and why that choice.
3. **Not in this PR:** anything found but left out, with the reason.
4. **Verifying it:** only when the reviewer has to check something the diff does not show.
5. `Closes #N` last, if there is an issue.

Numbers over adjectives: "4 to 13 percent slower on `CanParse`", not "slightly slower". Quote the
exact error or log line that motivated the change.

Good:

```
Drops `continue-on-error` from the dependency review job. The Dependency graph is enabled now,
and the run on #57 produced a real report, which is the condition the old comment set for
removing it.
```

## Changelog entries

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) sections: Added, Changed, Deprecated,
Removed, Fixed, Security. Each entry says what a package user notices, why it changed, and what it
costs, then cites the ADR or issue. Breaking entries start with `**Breaking:**`.

## Config comments

Comments in YAML, MSBuild and `NuGet.Config` say why the setting exists and what breaks without
it. A setting that looks redundant gets the comment that stops someone deleting it.

```xml
<!-- With a single source this looks redundant, and it is not: the day a second source is
     added, every existing package keeps resolving from nuget.org. -->
```

## Code comments

Comment why, not what. If the code needs a comment to say what it does, rename something.

Good:

```csharp
// 529 is retried like any 5xx, but callers need to tell overload from a bug, so it gets its own type.
```

Bad:

```csharp
// This method gets the request id from the response headers and returns it to the caller.
```

XML docs on public members are required. Say what the member does, what it returns, and what
breaks it. Skip the marketing.

## Docs and README

- Lead with what the thing is and who it is for. No throat clearing.
- State limits honestly and early. A README that hides a limit costs more trust than the limit does.
- Tables for facts. Prose for reasoning.
- Code samples must compile. If they cannot yet, say so.
- Links in any file packed into a `.nupkg` (`PACKAGE.md`) are absolute. nuget.org cannot
  resolve `docs/...` or `LICENSE`.
- ADRs are not edited after they are accepted. A later ADR supersedes an earlier one.

## Final pass

Read it back and cut:

- Every em dash.
- Every sentence that could be deleted without losing information.
- Every word from the banned list.
- Every paragraph over four sentences.
- Every restatement of something said above.

If a section survives the cut unchanged, it was probably already fine.
