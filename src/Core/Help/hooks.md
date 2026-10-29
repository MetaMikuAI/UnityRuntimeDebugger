# Hooks

A hook patches a method with Harmony and records its calls.

## Method references

`Namespace.Type::Method`, `Namespace.Type::Method(int, string)` for an overload, `.ctor` for constructors.

```sh
curl -s "{{base}}/api/methods?q=UnityEngine.GameObject::SetActive" -H "Authorization: Bearer $T"
```

Each candidate: `ref`, `signature`, `hookable` / `reason`, and `native` (`address`, `location` such as `libil2cpp.so+0xAD7084C`, `folded`).

## Routes

```sh
curl -s {{base}}/api/hooks -H "Authorization: Bearer $T" -d '{"method": "UnityEngine.GameObject::SetActive", "action": "log"}'
curl -s "{{base}}/api/hooks/1/calls?wait=2" -H "Authorization: Bearer $T"
curl -s -X PATCH {{base}}/api/hooks/1 -H "Authorization: Bearer $T" -d '{"enabled": false}'
curl -s -X DELETE {{base}}/api/hooks/1 -H "Authorization: Bearer $T"
```

- `action`: `log`, `skip` (the original does not run), `return` (skip and return `returnValue`, a C# expression).
- `source`: a patch class instead of the generated one (`GET /api/hooks/{id}` shows it).
- Counters: `hits`, `recorded`, `dropped` (over 200 per second), `foreign` (folded calls let through).

## Calls

`GET /api/hooks/{id}/calls?after=&wait=` returns `{"calls", "cursor", "dropped"}`; pass `cursor` back as `after`. A call has `time`, `frame`, `thread`, `mainThread`, `instance`, `args`, `result`, `exception`, `durationUs`. The last 500 are kept.

## Patch source

A public static class with Harmony `Prefix`, `Postfix`, `Finalizer`, `Transpiler`. `HookContext.Get(id)` records calls: `Enter(instance)` returns a token (< 0: not this hook's call, run it untouched; 0: not recorded; > 0: record), `Exit(token, instance, args, result, hasResult)`, `Fail(token, exception)`.

## ICF

Identical code folding merges functions with identical machine code; a hook on one of them receives the calls of all. Such methods list the others in `native.folded`, and creating the hook answers 409 `folded` unless `acceptFolded: true`. Calls whose instance is not of the hooked class are let through and counted in `foreign`; `skip` and `return` apply only to the rest. Static methods and methods of the same class cannot be told apart. One native function takes one hook (409 `conflict`).

## Limits

Inlined calls are not seen. Open generic, abstract and interface methods cannot be hooked.
