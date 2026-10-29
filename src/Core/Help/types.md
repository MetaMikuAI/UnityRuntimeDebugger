# Types

- `GET /api/assemblies`: `group` is `game`, `unity`, `il2cpp`, `mods` or `bcl`.
- `GET /api/assemblies/{name}/types`
- `GET /api/types?q=`: name, prefix, CamelCase initials (`pc` → `PlayerController`), substring.
- `GET /api/types/{type}`: full name, or a unique short name. Nested types use `+`. Returns `type` and a `handle` for the static side, usable with the object routes; in code `$handle` is the type itself.
- `GET /api/types/{type}/instances`: loaded instances of a `UnityEngine.Object` type.
