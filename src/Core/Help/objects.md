# Objects

## Values

| field | |
| --- | --- |
| `kind` | `null` `string` `number` `bool` `char` `enum` `pointer` `type` `object` `struct` `destroyed` `error` `void` |
| `type` | short type name |
| `text` | preview; strings are cut at 300 chars (`truncated`, `length`) |
| `handle` | `/api/objects/{handle}`, `$handle` in code |
| `count` | collection size |
| `color` | RGBA 0–1 |

Handles last until evicted (20 000 most recent), `DELETE /api/handles`, or a game restart (`debugger.boot` changes). Unknown: 404. Destroyed Unity object: 410.

## Inspect

`GET /api/objects/{h}` (`?statics=1` reads static properties of a type handle) returns `type`, `value`, `address`, `facets`, `gameObject` and `members`.

- `facets`: `components` (`/components`), `items` (`/items?offset=&limit=`), `image` (`/image.png?max=`), `instances` (`/api/types/{type}/instances`).
- Members: `name`, `kind` (`field` `property` `constant`), `type`, `value` or `error`, `native`, `writable`, `editor`, `options`, `lazy` (`static` or `sideEffect`; read with `GET .../members/{name}`).

## Write

`PUT /api/objects/{h}/members/{name}` with `{"value": ...}`: JSON scalars, `"(1, 2, 3)"`, `"#ff8800"`, enum names, `"0x1F"`, `{"handle": 12}` or `"$12"`, type names for `Type` parameters.

## Call

`GET /api/objects/{h}/methods` lists methods. `POST /api/objects/{h}/methods/{name or id}` with `{"args": [...]}` calls one; overloads are picked by argument count, otherwise call by `id`. Omitted trailing optional arguments use their defaults.

```sh
curl -s {{base}}/api/eval -H "Authorization: Bearer $T" -d '{"code": "UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()[0]"}'
curl -s {{base}}/api/objects/1/methods -H "Authorization: Bearer $T"
curl -s -X POST {{base}}/api/objects/1/methods/SetActive -H "Authorization: Bearer $T" -d '{"args": [true]}'
curl -s -X PUT {{base}}/api/objects/1/members/name -H "Authorization: Bearer $T" -d '{"value": "Renamed"}'
```

## Scenes

- `GET /api/scenes`: scenes with root GameObjects; the last is DontDestroyOnLoad.
- `GET /api/objects/{h}/children`, `GET /api/objects/{h}/components`.
- `GET /api/objects/search?q=`: GameObjects by name, with `path`.
