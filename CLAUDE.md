# Keyframe: Agent Instructions

The active plan is `AUDIO_ENHANCEMENTS_PLAN.md`. Read it first. It holds the decisions, the API contract changes, the phased work plan, and the progress log. Update its status markers and append to its progress log (section 11) at the end of every working session. `archive/KEYFRAME_PLAN.md` is the original build plan, kept for history only; do not read or update it for this work.

The requirement documents in `~/Code/Agents/requirements` are normative. Section 2 of the active plan lists the deliberate exceptions (most importantly, authentication is DocumentAtom-style rather than full AAA, and routes use the `/v1.0` prefix).

## Build and test

```bash
dotnet build src/Keyframe.sln -c Release
dotnet run --project src/Test.Automated -c Release
```

ffmpeg and ffprobe must be on the PATH for the media tests. Without them, those cases are skipped with a reason.

## Code style (strict)

- The namespace declaration comes first, and `using` statements go inside the namespace block.
- Microsoft and System usings first, alphabetical, then all others, alphabetical.
- All public members, constructors, and methods have XML documentation. Private members and methods do not.
- Public members that need range or null validation use explicit getters and setters with backing fields.
- Private fields are `_PascalCase` (for example `_Settings`, never `_settings`).
- No tuples. No `var`. No partial classes. One class or one enum per file.
- Use classic `using (...) { }` statements, not using declarations.
- Async methods accept a `CancellationToken` and use `.ConfigureAwait(false)` on every await. Check cancellation at sensible points.
- When a method returns `IEnumerable`, add an async variant that takes a `CancellationToken`.
- Configurable values are public members with private backing fields and reasonable defaults, not constants. Document defaults, minimums, and maximums.
- Throw specific exception types with meaningful messages, and document them with `/// <exception>` tags.
- Implement the full dispose pattern (`protected virtual void Dispose(bool disposing)`) when holding disposable resources.
- Nullable reference types are enabled. Validate parameters with guard clauses.
- Prefer LINQ where it stays readable. Use `.Any()` rather than `.Count() > 0`.
- No `Console.WriteLine` in library code (`Keyframe.Core`).
- Do not use `System.Text.Json` DOM types (`JsonElement`, `JsonNode`) for fixed contracts. Deserialize into named types. The only schemaless value is the caller-defined `Results` dictionary.
- Never use em-dashes anywhere: code, comments, XML docs, Postman, Markdown, commit messages.
- Do not change version numbers unless the owner explicitly asks.
