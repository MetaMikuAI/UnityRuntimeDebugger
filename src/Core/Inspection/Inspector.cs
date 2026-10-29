using System.Text.Json.Serialization;
using UnityRuntimeDebugger.Hosting;

namespace UnityRuntimeDebugger.Inspection;

public enum Facet
{
    /// <summary>A GameObject: its components can be listed.</summary>
    Components,

    /// <summary>A collection: elements can be paged.</summary>
    Items,

    /// <summary>A texture or sprite: renders to PNG.</summary>
    Image,

    /// <summary>The static view of a UnityEngine.Object type: loaded instances can be listed.</summary>
    Instances,
}

public sealed record TypeView(string Name, string FullName, string Assembly, string Kind, bool? Native, [property: JsonPropertyName("csharp")] string? CSharp, IReadOnlyList<string> Bases);

public sealed record GameObjectView(int Handle, string Name, string Path, bool Active, bool ActiveInHierarchy, string Layer, string? Tag, string Scene, IReadOnlyList<ValueView> Ancestors);

public sealed record NodeView(int Handle, string Name, bool Active, bool ActiveInHierarchy, int Children, IReadOnlyList<string>? Components, string? Path);

public sealed record SceneView(string Name, int BuildIndex, bool Loaded, bool? Persistent, IReadOnlyList<NodeView> Roots);

public sealed record ComponentView(ValueView Value, bool? Enabled, bool? Missing);

public sealed class InspectionView
{
    public int Handle { get; init; }
    public bool Static { get; init; }
    public TypeView Type { get; init; } = null!;
    public ValueView Value { get; init; } = ValueView.Null;
    public string? Address { get; init; }
    public int? Count { get; init; }
    public List<Facet> Facets { get; init; } = new();

    /// <summary>For a GameObject or a component: the GameObject's facts.</summary>
    public GameObjectView? GameObject { get; init; }

    public List<MemberView> Members { get; init; } = new();

    /// <summary>The full text when the handle is a string.</summary>
    public string? Text { get; init; }
}

/// <summary>Builds the views the API returns. Everything here reads engine objects: main thread only.</summary>
public sealed class Inspector
{
    private readonly IGame _game;
    private readonly IRuntime _runtime;
    private readonly ObjectTable _objects;

    public Inspector(IGame game, IRuntime runtime, ObjectTable objects, Values values, MemberReader members, CollectionReader collections, MethodTable methods)
    {
        _game = game;
        _runtime = runtime;
        _objects = objects;
        Values = values;
        Members = members;
        Collections = collections;
        Methods = methods;
    }

    public Values Values { get; }

    public MemberReader Members { get; }

    public CollectionReader Collections { get; }

    public MethodTable Methods { get; }

    public InspectionView Inspect(int handle, object target, bool readStatics)
    {
        if (target is string text)
            return new InspectionView { Handle = handle, Type = TypeOf(typeof(string)), Value = Values.Describe(text), Text = text };

        var (type, instance) = MemberReader.Target(target);
        var facets = new List<Facet>();
        GameObjectView? gameObject = null;

        if (instance == null)
        {
            if (_game.IsUnityObjectType(type))
                facets.Add(Facet.Instances);
        }
        else
        {
            if (_game.GameObjectOf(instance) is { } facts)
            {
                gameObject = View(facts);
                if (gameObject.Handle == handle)
                    facets.Add(Facet.Components);
            }

            if (Values.IsCollection(type))
                facets.Add(Facet.Items);
            if (_game.CanRender(instance))
                facets.Add(Facet.Image);
        }

        var value = instance == null
            ? new ValueView { Kind = ValueKind.Type, Type = "Type", Text = TypeNames.Display(type) }
            : Values.Snapshot(instance, out _); // a fresh Describe would register a boxed struct again
        value.Handle = handle;

        return new InspectionView
        {
            Handle = handle,
            Static = instance == null,
            Type = TypeOf(type),
            Value = value,
            Address = instance == null ? null : _runtime.AddressOf(instance),
            Count = instance == null ? null : Values.CountOf(instance),
            Facets = facets,
            GameObject = gameObject,
            Members = Members.Read(target, readStatics),
        };
    }

    public TypeView TypeOf(Type type)
    {
        var nameable = TypeNames.NameableType(type);
        return new TypeView(
            TypeNames.Display(type),
            type.FullName ?? type.Name,
            type.Assembly.GetName().Name ?? "",
            TypeCatalog.KindOf(type),
            _runtime.IsNativeType(type) ? true : null,
            nameable == null ? null : TypeNames.CSharp(nameable),
            Members.Bases(type));
    }

    public List<SceneView> Scenes()
        => _game.Scenes().Select(s => new SceneView(s.Name, s.BuildIndex, s.Loaded, s.Persistent ? true : null, s.Roots.Select(View).ToList())).ToList();

    public List<NodeView> Children(object gameObject) => _game.Children(gameObject).Select(View).ToList();

    public List<NodeView> Search(string query, int limit) => _game.Search(query, limit).Select(View).ToList();

    public List<ComponentView> Components(object gameObject)
        => _game.Components(gameObject)
            .Select(c => c.Target == null
                ? new ComponentView(new ValueView { Kind = ValueKind.Null, Type = "Missing script" }, null, true)
                : new ComponentView(Values.Describe(c.Target), c.Enabled, null))
            .ToList();

    public object Instances(Type type, int limit)
    {
        var found = _game.FindObjects(type, limit, out var total);
        return new { total, items = found.Select(Values.Describe).ToList() };
    }

    private NodeView View(NodeFacts node)
        => new(_objects.Register(node.Target), node.Name, node.Active, node.ActiveInHierarchy, node.Children,
            node.Components.Count > 0 ? node.Components : null, node.Path);

    private GameObjectView View(GameObjectFacts facts)
        => new(_objects.Register(facts.Target), facts.Name, facts.Path, facts.Active, facts.ActiveInHierarchy, facts.Layer, facts.Tag, facts.Scene,
            facts.Ancestors.Select(Values.Describe).ToList());
}
