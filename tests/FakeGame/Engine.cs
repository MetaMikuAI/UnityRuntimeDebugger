// A small UnityEngine look-alike, enough for the debugger and for console code written against
// the real API (GameObject.Find, transform.position, Time.frameCount...).
#pragma warning disable IDE1006 // Unity naming

using System.Collections;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;

        public string name { get; set; } = "";

        public HideFlags hideFlags { get; set; }

        internal bool Destroyed { get; private set; }

        public int GetInstanceID() => _id;

        public override string ToString() => $"{name} ({GetType().Name})";

        public static void Destroy(Object obj) => obj?.OnDestroy();

        public static void DontDestroyOnLoad(Object target)
        {
            if (target is GameObject go)
                go.MoveTo(Scene.Persistent);
        }

        public static T[] FindObjectsOfType<T>() where T : Object => World.All.OfType<T>().Where(o => !o.Destroyed).ToArray();

        internal virtual void OnDestroy()
        {
            Destroyed = true;
            World.All.Remove(this);
        }

        public static bool operator ==(Object a, Object b)
            => a is null || a.Destroyed ? b is null || b.Destroyed : ReferenceEquals(a, b);

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object other) => other is Object o && this == o;

        public override int GetHashCode() => _id;

        public static implicit operator bool(Object obj) => obj != null;
    }

    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        DontSave = 52,
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();

        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            _components.Add(transform);
            World.All.Add(this);
            World.All.Add(transform);
            scene = Scene.Main;
            Scene.Main.Roots.Add(this);
            foreach (var type in components)
                AddComponent(type);
        }

        public Transform transform { get; }

        public bool activeSelf { get; private set; } = true;

        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public int layer { get; set; }

        public string tag { get; set; } = "Untagged";

        public Scene scene { get; private set; }

        public void SetActive(bool value) => activeSelf = value;

        public Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            _components.Add(component);
            World.All.Add(component);
            return component;
        }

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));

        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();

        public Component[] GetComponents() => _components.ToArray();

        public static GameObject Find(string name)
            => World.All.OfType<GameObject>().FirstOrDefault(g => g.name == name && g.activeInHierarchy);

        internal void MoveTo(Scene target)
        {
            scene.Roots.Remove(this);
            scene = target;
            if (transform.parent == null)
                target.Roots.Add(this);
            foreach (Transform child in transform)
                child.gameObject.scene = target;
        }

        internal override void OnDestroy()
        {
            foreach (var child in transform.Children.ToList())
                child.gameObject.OnDestroy();
            foreach (var component in _components)
                component.OnDestroy();
            transform.parent = null;
            scene.Roots.Remove(this);
            base.OnDestroy();
        }
    }

    public class Component : Object
    {
        private GameObject _gameObject;

        public GameObject gameObject
        {
            get => _gameObject;
            internal set
            {
                _gameObject = value;
                name = value.name;
            }
        }

        public Transform transform => gameObject.transform;

        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public sealed class Transform : Component, IEnumerable
    {
        private Transform _parent;

        internal List<Transform> Children { get; } = new();

        public Vector3 position { get; set; }

        public Quaternion rotation { get; set; } = Quaternion.identity;

        public Vector3 localScale { get; set; } = Vector3.one;

        public Transform parent
        {
            get => _parent;
            set
            {
                if (_parent != null)
                    _parent.Children.Remove(this);
                else
                    gameObject.scene.Roots.Remove(gameObject);
                _parent = value;
                if (value != null)
                    value.Children.Add(this);
                else if (!gameObject.Destroyed)
                    gameObject.scene.Roots.Add(gameObject);
            }
        }

        public int childCount => Children.Count;

        public Transform GetChild(int index) => Children[index];

        public Transform Find(string name) => Children.FirstOrDefault(c => c.gameObject.name == name);

        public IEnumerator GetEnumerator() => Children.ToList().GetEnumerator();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public virtual void Update()
        {
        }
    }

    public sealed class Camera : Behaviour
    {
        public float fieldOfView { get; set; } = 60;

        public float nearClipPlane { get; set; } = 0.3f;

        public float farClipPlane { get; set; } = 1000;

        public Color backgroundColor { get; set; } = new(0.19f, 0.3f, 0.47f, 1);

        public static Camera main => Object.FindObjectsOfType<Camera>().FirstOrDefault();
    }

    public sealed class Light : Behaviour
    {
        public Color color { get; set; } = new(1, 0.96f, 0.84f, 1);

        public float intensity { get; set; } = 1;

        public LightType type { get; set; } = LightType.Directional;
    }

    public enum LightType
    {
        Spot,
        Directional,
        Point,
        Area,
    }

    public sealed class Rigidbody : Component
    {
        public float mass { get; set; } = 1;

        public Vector3 velocity { get; set; }

        public bool useGravity { get; set; } = true;

        public bool isKinematic { get; set; }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; } = true;

        public Material sharedMaterial { get; set; } = new() { name = "Default-Material" };

        /// <summary>Like Unity: reading it instantiates a per-renderer copy.</summary>
        public Material material
        {
            get
            {
                if (!sharedMaterial.name.EndsWith(" (Instance)", StringComparison.Ordinal))
                    sharedMaterial = new Material { name = sharedMaterial.name + " (Instance)", color = sharedMaterial.color };
                return sharedMaterial;
            }
        }
    }

    public sealed class MeshRenderer : Renderer
    {
    }

    public sealed class Material : Object
    {
        public Color color { get; set; } = Color.white;
    }

    public class Texture : Object
    {
        public int width { get; protected set; }

        public int height { get; protected set; }
    }

    public sealed class Texture2D : Texture
    {
        private readonly Func<int, int, Color32> _pixel;

        public Texture2D(string name, int width, int height, Func<int, int, Color32> pixel)
        {
            this.name = name;
            this.width = width;
            this.height = height;
            _pixel = pixel;
            World.All.Add(this);
        }

        internal Color32 Pixel(int x, int y) => _pixel(x, y);
    }

    public sealed class Sprite : Object
    {
        public Texture2D texture { get; init; }

        public Rect textureRect { get; init; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new(0, 0, 0);

        public static Vector3 one => new(1, 1, 1);

        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new(0, 0, 0, 1);

        public static Quaternion Euler(float x, float y, float z)
        {
            var (cy, sy) = (MathF.Cos(y * MathF.PI / 360), MathF.Sin(y * MathF.PI / 360));
            return new Quaternion(0, sy, 0, cy);
        }
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new(1, 1, 1, 1);

        public static Color red => new(1, 0, 0, 1);
    }

    public struct Color32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public static class Application
    {
        public static string productName => "Fake Game";

        public static string version => "1.0";

        public static string unityVersion => "2022.3.0f1";

        public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer;

        public static bool isFocused => true;

        public static bool runInBackground { get; set; } = true;
    }

    public enum RuntimePlatform
    {
        WindowsPlayer = 2,
        Android = 11,
        LinuxPlayer = 13,
    }

    public static class Time
    {
        public static int frameCount { get; internal set; }

        public static float time { get; internal set; }

        public static float deltaTime { get; internal set; } = 1 / 60f;

        public static float timeScale { get; set; } = 1;

        public static float realtimeSinceStartup => (float)World.Clock.Elapsed.TotalSeconds;
    }

    public static class Debug
    {
        public static void Log(object message) => World.Log?.Invoke("info", message?.ToString() ?? "null");

        public static void LogWarning(object message) => World.Log?.Invoke("warn", message?.ToString() ?? "null");

        public static void LogError(object message) => World.Log?.Invoke("error", message?.ToString() ?? "null");
    }

    /// <summary>Registry of live objects; the debugger host enumerates it.</summary>
    internal static class World
    {
        public static readonly List<Object> All = new();
        public static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public static Action<string, string> Log;
    }
}

namespace UnityEngine.SceneManagement
{
    public sealed class Scene
    {
        internal static readonly Scene Main = new("Main", 0);
        internal static readonly Scene Persistent = new("DontDestroyOnLoad", -1);

        private Scene(string name, int buildIndex)
        {
            this.name = name;
            this.buildIndex = buildIndex;
        }

        public string name { get; }

        public int buildIndex { get; }

        public bool isLoaded => true;

        internal List<GameObject> Roots { get; } = new();

        public GameObject[] GetRootGameObjects() => Roots.ToArray();
    }

    public static class SceneManager
    {
        public static int sceneCount => 1;

        public static Scene GetActiveScene() => Scene.Main;

        public static Scene GetSceneAt(int index) => Scene.Main;
    }
}
