using System.IO.Compression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityRuntimeDebugger.Hosting;
using Object = UnityEngine.Object;

namespace FakeGame;

/// <summary>The debugger's view of the fake engine; the same contract the real Unity adapters implement.</summary>
internal sealed class FakeHost : IGame
{
    public GameStatus Status()
        => new(Application.productName, Application.version, Application.unityVersion, Application.platform.ToString(),
            1 / Time.deltaTime, Time.frameCount, Time.realtimeSinceStartup, Time.timeScale, Application.isFocused,
            Application.runInBackground, SceneManager.GetActiveScene().name, new[] { SceneManager.GetActiveScene().name });

    public IReadOnlyList<SceneFacts> Scenes()
        => new[] { Scene.Main, Scene.Persistent }
            .Select(s => new SceneFacts(s.name, s.buildIndex, true, s == Scene.Persistent, s.GetRootGameObjects().Select(g => Node(g)).ToList()))
            .ToList();

    public IReadOnlyList<NodeFacts> Children(object gameObject)
        => GameObjectOf(gameObject)!.Target is GameObject go ? go.transform.Children.Select(t => Node(t.gameObject)).ToList() : Array.Empty<NodeFacts>();

    public IReadOnlyList<ComponentFacts> Components(object gameObject)
        => ((GameObject)GameObjectOf(gameObject)!.Target).GetComponents()
            .Select(c => new ComponentFacts(c, c switch { Behaviour b => b.enabled, Renderer r => r.enabled, _ => null }))
            .ToList();

    public GameObjectFacts GameObjectOf(object value)
    {
        var go = value switch { GameObject g => g, Component c => c.gameObject, _ => null };
        if (go == null)
            return null;

        var ancestors = new List<object>();
        for (var t = go.transform.parent; t != null; t = t.parent)
            ancestors.Insert(0, t.gameObject);
        var path = string.Join("/", ancestors.Cast<GameObject>().Select(a => a.name).Append(go.name));
        return new GameObjectFacts(go, go.name, path, go.activeSelf, go.activeInHierarchy, $"Default ({go.layer})", go.tag, go.scene.name, ancestors);
    }

    public IReadOnlyList<NodeFacts> Search(string query, int limit)
        => Object.FindObjectsOfType<GameObject>()
            .Where(g => g.name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(g => Node(g, withPath: true))
            .ToList();

    public bool IsUnityObjectType(Type type) => typeof(Object).IsAssignableFrom(type);

    public IReadOnlyList<object> FindObjects(Type type, int limit, out int total)
    {
        var found = Object.FindObjectsOfType<Object>().Where(type.IsInstanceOfType).ToList();
        total = found.Count;
        return found.Take(limit).ToList();
    }

    public bool IsDestroyed(object value) => value is Object o && o == null;

    public string NameOf(object value) => value is Object o ? o.name : null;

    public bool CanRender(object value) => value is Texture2D or Sprite;

    public byte[] RenderPng(object value, int maxSize)
    {
        var (texture, rect) = value switch
        {
            Sprite s => (s.texture, s.textureRect),
            Texture2D t => (t, new Rect(0, 0, t.width, t.height)),
            _ => throw new ArgumentException("only Texture2D and Sprite render"),
        };

        var scale = Math.Min(1f, maxSize / Math.Max(rect.width, rect.height));
        var (w, h) = (Math.Max(1, (int)(rect.width * scale)), Math.Max(1, (int)(rect.height * scale)));
        return Png(w, h, (x, y) => texture.Pixel((int)(rect.x + x / scale), (int)(rect.y + (h - 1 - y) / scale)));
    }

    private static NodeFacts Node(GameObject go, bool withPath = false)
    {
        string path = null;
        if (withPath)
        {
            var names = new List<string>();
            for (var t = go.transform.parent; t != null; t = t.parent)
                names.Insert(0, t.gameObject.name);
            path = string.Join("/", names);
        }

        var components = go.GetComponents().Where(c => c is not Transform).Select(c => c.GetType().Name).Take(4).ToList();
        return new NodeFacts(go, go.name, go.activeSelf, go.activeInHierarchy, go.transform.childCount, components, path);
    }

    /// <summary>Minimal RGBA PNG encoder.</summary>
    private static byte[] Png(int width, int height, Func<int, int, Color32> pixel)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var c = pixel(x, y);
                raw.Write(new[] { c.r, c.g, c.b, c.a });
            }
        }

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Chunk(png, "IHDR", BigEndian(width).Concat(BigEndian(height)).Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray());
        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            raw.WriteTo(zlib);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(BigEndian(data.Length));
        png.Write(typed);
        png.Write(BigEndian((int)Crc32(typed)));
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    private static byte[] BigEndian(int value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
}
