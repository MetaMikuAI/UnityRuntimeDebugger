# Unity Runtime Debugger {{version}}

HTTP API for a running Unity game: scenes, objects, members, methods, C#, hooks. JSON in and out.

## Auth

Every `/api` route needs `Authorization: Bearer <token>`. The token is printed in the loader log (`UnityRuntimeDebugger token: …`) or set as `[Security] Token` in the config.

```sh
T=<token>
curl -s {{base}}/api/status -H "Authorization: Bearer $T"
```

## C#

`POST /api/eval` runs code in a persistent session. A trailing expression without `;` is the result. Code runs on the main thread.

```sh
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "Application.productName"}'
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects(); roots.Length"}'
```

Result: `{"ok", "value", "output", "error", "durationMs"}`. `error.code` is `compile_error` (with `diagnostics`) or `runtime_error`.

## Values

`{"kind", "type", "text", "handle", "count"}`. A `handle` refers to the object: `$1` in code, `/api/objects/1` in routes. `$_` is the last result.

```sh
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "roots[0]"}'
curl -s {{base}}/api/objects/1 -H "Authorization: Bearer $T"
curl -s {{base}}/api/objects/1/components -H "Authorization: Bearer $T"
```

## Search

```sh
curl -s {{base}}/api/scenes -H "Authorization: Bearer $T"
curl -s "{{base}}/api/objects/search?q=Camera" -H "Authorization: Bearer $T"
curl -s "{{base}}/api/types?q=Camera" -H "Authorization: Bearer $T"
```

## Hooks

```sh
curl -s {{base}}/api/hooks -H "Authorization: Bearer $T" -d '{"method": "UnityEngine.GameObject::SetActive"}'
curl -s "{{base}}/api/hooks/1/calls?wait=2" -H "Authorization: Bearer $T"
curl -s -X DELETE {{base}}/api/hooks/1 -H "Authorization: Bearer $T"
```

## Errors

`{"error": {"code", "message", "help", "details"}}`: 400 `bad_request`, 401 `unauthorized`, 404 `not_found`, 409 `conflict` / `folded`, 410 `destroyed`, 503 `main_thread_timeout` (the game is loading or paused).

## Topics

/help/eval · /help/objects · /help/types · /help/hooks · /help/il2cpp · /help/api

CLI: `curl -sO {{base}}/urdbg.py`
