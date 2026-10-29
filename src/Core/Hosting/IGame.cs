namespace UnityRuntimeDebugger.Hosting;

/// <summary>
/// The engine side of the debugger: scenes, GameObjects, components and textures. Implemented once
/// per scripting backend against its UnityEngine assemblies. Every member is called on the main thread.
/// </summary>
public interface IGame
{
    GameStatus Status();

    IReadOnlyList<SceneFacts> Scenes();

    IReadOnlyList<NodeFacts> Children(object gameObject);

    /// <summary>Components in inspector order; a null target stands for a missing script.</summary>
    IReadOnlyList<ComponentFacts> Components(object gameObject);

    /// <summary>The GameObject a value is (or is attached to), or null for anything else.</summary>
    GameObjectFacts? GameObjectOf(object value);

    /// <summary>GameObjects whose name contains <paramref name="query"/>, across all scenes.</summary>
    IReadOnlyList<NodeFacts> Search(string query, int limit);

    bool IsUnityObjectType(Type type);

    /// <summary>Loaded instances of a UnityEngine.Object type, assets included.</summary>
    IReadOnlyList<object> FindObjects(Type type, int limit, out int total);

    /// <summary>True for a Unity object whose native side is gone (scene unloaded, Destroy).</summary>
    bool IsDestroyed(object value);

    /// <summary>The <c>name</c> of a Unity object, null for anything else.</summary>
    string? NameOf(object value);

    bool CanRender(object value);

    /// <summary>Encodes a Texture or Sprite as PNG, scaled down to fit <paramref name="maxSize"/>.</summary>
    byte[] RenderPng(object value, int maxSize);
}

public sealed record GameStatus(
    string Product,
    string Version,
    string Unity,
    string Platform,
    float Fps,
    int Frame,
    float Time,
    float TimeScale,
    bool Focused,
    bool RunInBackground,
    string ActiveScene,
    IReadOnlyList<string> Scenes);

public sealed record SceneFacts(string Name, int BuildIndex, bool Loaded, bool Persistent, IReadOnlyList<NodeFacts> Roots);

public sealed record NodeFacts(
    object Target,
    string Name,
    bool Active,
    bool ActiveInHierarchy,
    int Children,
    IReadOnlyList<string> Components,
    string? Path = null);

public sealed record ComponentFacts(object? Target, bool? Enabled);

public sealed record GameObjectFacts(
    object Target,
    string Name,
    string Path,
    bool Active,
    bool ActiveInHierarchy,
    string Layer,
    string? Tag,
    string Scene,
    IReadOnlyList<object> Ancestors);
