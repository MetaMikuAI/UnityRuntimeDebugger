# C#

`POST /api/eval` with `{"code", "mode"}`; a plain-text body is script code. Always 200 for a well-formed request: check `ok`.

## Script mode

A persistent session. Declarations carry over between calls; a trailing expression without `;` is `value`.

- Imports: `System`, `System.Linq`, `System.Collections.Generic`, `System.Text`, `System.Threading.Tasks`, `UnityEngine`.
- `$12` is the object behind handle 12, typed as its runtime class; `$_` is the last result; `Handles.Of(obj)` returns a handle.
- `output` is what the code wrote to `Console`.
- `value` is absent when the code returns nothing, `{"kind": "null"}` for null.
- No `namespace` blocks; use program mode.
- References are fixed at the first call: reset the session to see assemblies loaded later.
- No `async`/`await` or blocking waits: the main thread is waiting on the code.

```sh
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "var t = Time.realtimeSinceStartup;"}'
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "Time.realtimeSinceStartup - t"}'
curl -s {{base}}/api/eval/vars -H "Authorization: Bearer $T"
curl -s -X DELETE {{base}}/api/eval/session -H "Authorization: Bearer $T"
```

## Program mode

A full compilation unit with `public static object Execute()`, run once and unloaded.

```json
{"mode": "program", "code": "public static class Program { public static object Execute() => Application.unityVersion; }"}
```

## Other routes

- `POST /api/eval/complete {"code", "position"}`: completions.
- `POST /api/watch {"expressions": [...]}`: evaluate several expressions; compiled once, cheap to poll. Handles work, session variables do not.

Execution waits up to 60 s for the main thread.
