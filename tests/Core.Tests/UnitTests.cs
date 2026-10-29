using System.Text.Json;
using UnityRuntimeDebugger.Api;
using UnityRuntimeDebugger.Hooks;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;
using UnityRuntimeDebugger.Logging;
using UnityRuntimeDebugger.Scripting;

namespace UnityRuntimeDebugger.Tests;

public class RouterTests
{
    private static Router All()
    {
        var router = new Router();
        StatusApi.Map(router);
        SceneApi.Map(router);
        ObjectApi.Map(router);
        TypeApi.Map(router);
        EvalApi.Map(router);
        HookApi.Map(router);
        LogApi.Map(router);
        return router;
    }

    [Fact]
    public void EveryRouteIsDocumented()
    {
        foreach (var route in All().Routes)
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Summary), $"{route.Method} {route.Pattern} has no summary");
            foreach (var p in route.Parameters.Where(p => p.In == "path"))
                Assert.False(string.IsNullOrWhiteSpace(p.Description), $"{route.Pattern}: path parameter {p.Name} has no description");
            if (route.Method is "POST" or "PUT" or "PATCH" && route.Pattern != "/api/objects/{h}/methods/{name}")
                Assert.NotNull(route.BodyExample);
        }
    }

    [Fact]
    public void LiteralSegmentsWinOverParameters()
    {
        var match = All().Match("GET", "/api/objects/search");
        Assert.Equal("/api/objects/search", match.Route!.Pattern);
        Assert.Equal("12", All().Match("GET", "/api/objects/12").Values["h"]);
    }

    [Fact]
    public void WrongMethodListsTheAllowedOnes()
    {
        var match = All().Match("POST", "/api/objects/3");
        Assert.Null(match.Route);
        Assert.Contains("GET", match.Allowed);
    }

    [Fact]
    public void TyposGetSuggestions()
        => Assert.Contains(All().Suggest("GET", "/api/objectz/3"), s => s.StartsWith("GET /api/objects/{h}"));
}

public class HandleRewriterTests
{
    private readonly ObjectTable _objects = new(new ManagedRuntime());

    [Fact]
    public void HandlesBecomeTypedLookups()
    {
        var id = _objects.Register(new List<int> { 1, 2 });
        var code = new HandleRewriter(_objects).Rewrite($"${id}.Count + $_.GetHashCode()");
        Assert.Contains($"((global::System.Collections.Generic.List<int>)global::UnityRuntimeDebugger.Handles.Get({id})).Count", code);
        Assert.Contains("global::UnityRuntimeDebugger.Handles.Last", code);
    }

    [Theory]
    [InlineData("\"$1\"")]
    [InlineData("// $1")]
    [InlineData("/* $1 */")]
    [InlineData("'$'")]
    [InlineData("price$1")]
    public void LeavesStringsCommentsAndIdentifiersAlone(string code)
        => Assert.Equal(code, new HandleRewriter(_objects).Rewrite(code));

    [Fact]
    public void MapsPositionsBothWays()
    {
        var rewriter = new HandleRewriter(_objects);
        const string code = "$1.x + y";
        var rewritten = rewriter.Rewrite(code, out var shifts);
        var y = code.IndexOf('y');
        var mapped = HandleRewriter.MapPosition(y, shifts);
        Assert.Equal('y', rewritten[mapped]);
        Assert.Equal(y, HandleRewriter.MapBack(mapped, shifts));
    }
}

#pragma warning disable CS0649 // fields set by the parser through reflection
public class ValueParserTests
{
    private struct Vec3
    {
        public float x, y, z;
    }

    private struct Color
    {
        public float r, g, b, a;
    }

    [Flags]
    private enum Mode
    {
        None = 0,
        Fast = 1,
    }

    private readonly ObjectTable _objects = new(new ManagedRuntime());

    private ValueParser Parser => new(new ManagedRuntime(), _objects);

    [Fact]
    public void ParsesVectorsColorsEnumsAndNumbers()
    {
        var v = (Vec3)Parser.Parse("(1, 2.5, -3)", typeof(Vec3))!;
        Assert.Equal((1f, 2.5f, -3f), (v.x, v.y, v.z));
        var c = (Color)Parser.Parse("#ff000080", typeof(Color))!;
        Assert.Equal(1f, c.r);
        Assert.InRange(c.a, 0.5f, 0.51f);
        Assert.Equal(Mode.Fast, Parser.Parse("fast", typeof(Mode)));
        Assert.Equal(31, Parser.Parse("0x1F", typeof(int)));
        Assert.Null(Parser.Parse("null", typeof(int?)));
        Assert.Throws<FormatException>(() => Parser.Parse("1.5", typeof(int)));
    }

    [Fact]
    public void ResolvesHandles()
    {
        var list = new List<string> { "a" };
        var id = _objects.Register(list);
        Assert.Same(list, Parser.Parse("$" + id, typeof(IList<string>)));
        Assert.Same(list, Parser.Convert(JsonDocument.Parse($"{{\"handle\": {id}}}").RootElement, typeof(object)));
        Assert.Throws<InvalidCastException>(() => Parser.Parse("$" + id, typeof(string[])));
    }

    [Fact]
    public void PicksEditors()
    {
        Assert.Equal(Editor.Vector, ValueParser.EditorFor(typeof(Vec3), out var fields));
        Assert.Equal(new[] { "x", "y", "z" }, fields);
        Assert.Equal(Editor.Color, ValueParser.EditorFor(typeof(Color), out _));
        Assert.Equal(Editor.Flags, ValueParser.EditorFor(typeof(Mode), out _));
        Assert.Null(ValueParser.EditorFor(typeof(object), out _));
    }
}

public class FeedTests
{
    [Fact]
    public void CursorReturnsOnlyNewEntries()
    {
        var feed = new Feed<long>(4);
        for (var i = 0; i < 3; i++)
            feed.Add(seq => seq);

        var first = feed.Since(-1, 10);
        Assert.Equal(new long[] { 0, 1, 2 }, first.Items);
        Assert.Equal(2, first.Cursor);

        feed.Add(seq => seq);
        Assert.Equal(new long[] { 3 }, feed.Since(first.Cursor, 10).Items);
    }

    [Fact]
    public void ReportsEntriesThatFellOut()
    {
        var feed = new Feed<long>(2);
        for (var i = 0; i < 5; i++)
            feed.Add(seq => seq);

        var page = feed.Since(0, 10);
        Assert.True(page.Dropped);
        Assert.Equal(new long[] { 3, 4 }, page.Items);
    }

    [Fact]
    public async Task WaitReturnsAsSoonAsSomethingArrives()
    {
        var feed = new Feed<long>(8);
        var wait = feed.WaitAsync(-1, 10, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(50);
        feed.Add(seq => 42);
        var page = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new long[] { 42 }, page.Items);
    }

    [Fact]
    public void ClearKeepsCursorsValid()
    {
        var feed = new Feed<long>(8);
        feed.Add(seq => seq);
        var cursor = feed.Since(-1, 10).Cursor;
        feed.Clear();
        Assert.Empty(feed.Since(cursor, 10).Items);
        feed.Add(seq => seq);
        Assert.Equal(new long[] { 1 }, feed.Since(cursor, 10).Items);
    }
}

public class HookContextTests
{
    private sealed class Base
    {
        public void Method()
        {
        }
    }

    private sealed class Other
    {
    }

    [Fact]
    public void FoldedCallsFromOtherClassesAreLetThrough()
    {
        var runtime = new ManagedRuntime();
        var objects = new ObjectTable(runtime);
        var values = new Values(runtime, new NoGame(), objects);
        var context = new HookContext(99, typeof(Base).GetMethod(nameof(Base.Method))!, new MainThread(), runtime, values, filterByType: true);

        Assert.Equal(-1, context.Enter(new Other()));
        Assert.Equal(1, context.Foreign);

        var token = context.Enter(new Base());
        Assert.True(token > 0);
        context.Exit(token, new Base(), Array.Empty<object?>(), null, false);
        Assert.Equal(1, context.Hits);
        Assert.Equal(1, context.Recorded);
    }

    /// <summary>No engine: plain objects only.</summary>
    private sealed class NoGame : IGame
    {
        public GameStatus Status() => throw new NotSupportedException();
        public IReadOnlyList<SceneFacts> Scenes() => throw new NotSupportedException();
        public IReadOnlyList<NodeFacts> Children(object gameObject) => throw new NotSupportedException();
        public IReadOnlyList<ComponentFacts> Components(object gameObject) => throw new NotSupportedException();
        public GameObjectFacts? GameObjectOf(object value) => null;
        public IReadOnlyList<NodeFacts> Search(string query, int limit) => throw new NotSupportedException();
        public bool IsUnityObjectType(Type type) => false;
        public IReadOnlyList<object> FindObjects(Type type, int limit, out int total) => throw new NotSupportedException();
        public bool IsDestroyed(object value) => false;
        public string? NameOf(object value) => null;
        public bool CanRender(object value) => false;
        public byte[] RenderPng(object value, int maxSize) => throw new NotSupportedException();
    }
}
