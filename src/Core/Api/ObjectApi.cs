using System.Text.Json;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;

namespace UnityRuntimeDebugger.Api;

internal static class ObjectApi
{
    private sealed record WriteRequest(JsonElement Value);

    private sealed record InvokeRequest(JsonElement[]? Args);

    public static void Map(Router router)
    {
        router.Group("Objects");

        router.Get("/api/objects/{h}", "Inspect an object or a type's static side: type, facets, GameObject facts and members.",
                c => c.Main(() => c.Debugger.Inspector.Inspect(c.Handle(), c.Alive(), c.Flag("statics"))))
            .Query("statics", "`1` also reads static properties.");

        router.Get("/api/objects/{h}/members/{name}", "Read one field or property, including lazy ones.",
            c => c.Main(() => c.Debugger.Inspector.Members.ReadOne(c.Alive(), c.Route("name"))));

        router.Put("/api/objects/{h}/members/{name}", "Write a field or property; returns the member as read back.", async c =>
            {
                var body = await c.Body<WriteRequest>();
                return c.Main(() => c.Debugger.Inspector.Members.Write(c.Alive(), c.Route("name"), body.Value));
            })
            .Body("{\"value\": \"(1, 2, 3)\"}", "`value`: a JSON scalar, text such as `(1, 2, 3)` or `#ff8800`, or a handle `\"$12\"`.");

        router.Get("/api/objects/{h}/items", "Elements of a collection (array, list, dictionary, any enumerable), paged.",
                c => c.Main(() => c.Debugger.Inspector.Collections.Read(c.Alive(), c.Int("offset", 0, 0), c.Int("limit", 100, 1, 1000))))
            .Query("offset", "First index (default 0).")
            .Query("limit", "Page size (default 100, max 1000).");

        router.Get("/api/objects/{h}/methods", "Public methods (instance methods of an object, static methods of a type handle).",
            c => c.Debugger.Inspector.Methods.List(c.Target()));

        router.Post("/api/objects/{h}/methods/{name}", "Call a method by name (or by `id` from the method list); returns its value.", async c =>
            {
                var text = await c.Text();
                var args = string.IsNullOrWhiteSpace(text) ? Array.Empty<JsonElement>() : JsonSerializer.Deserialize<InvokeRequest>(text, Json.Options)?.Args ?? Array.Empty<JsonElement>();
                var d = c.Debugger;
                return c.Main(() =>
                {
                    var (result, isVoid) = d.Inspector.Methods.Invoke(c.Alive(), c.Route("name"), args);
                    if (isVoid)
                        return ValueView.Void;
                    if (result != null)
                        d.Objects.Last = result;
                    return d.Inspector.Values.Describe(result);
                });
            })
            .Body("{\"args\": [5, \"$12\"]}", "`args`: one entry per parameter, as for member writes.");

        router.Get("/api/objects/{h}/image.png", "A Texture or Sprite as PNG.", c =>
            {
                var png = c.Main(() => c.Debugger.Game.RenderPng(c.Alive(), c.Int("max", 1024, 16, 4096)));
                return HttpResponse.Bytes(png, "image/png");
            })
            .Query("max", "Longest side in pixels (default 1024).");

        router.Delete("/api/handles", "Forget all handles (objects they kept alive can be collected).", c =>
        {
            c.Debugger.Objects.Clear();
            return new { ok = true };
        });
    }
}
