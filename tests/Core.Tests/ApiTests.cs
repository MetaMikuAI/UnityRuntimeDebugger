using System.Text.Json;

namespace UnityRuntimeDebugger.Tests;

/// <summary>The HTTP API end to end, against the fake game.</summary>
public class ApiTests : IClassFixture<FakeGameProcess>
{
    private readonly FakeGameProcess _game;

    public ApiTests(FakeGameProcess game) => _game = game;

    private static string Kind(JsonElement value) => value.GetProperty("kind").GetString()!;

    [Fact]
    public async Task RequiresTheToken()
    {
        using var anonymous = new HttpClient { BaseAddress = new Uri(_game.BaseUrl) };
        var response = await anonymous.GetAsync("/api/status");
        Assert.Equal(401, (int)response.StatusCode);
        Assert.Contains("UnityRuntimeDebugger token", await response.Content.ReadAsStringAsync());
        Assert.Equal(200, (int)(await anonymous.GetAsync("/help")).StatusCode);
    }

    [Fact]
    public async Task StatusDescribesGameAndDebugger()
    {
        var (status, json) = await _game.Get("/api/status");
        Assert.Equal(200, status);
        Assert.Equal("Fake Game", json.GetProperty("game").GetProperty("product").GetString());
        Assert.True(json.GetProperty("mainThread").GetBoolean());
        Assert.Equal(Debugger.Version, json.GetProperty("debugger").GetProperty("version").GetString());
    }

    [Fact]
    public async Task EvalTellsNullFromNothing()
    {
        Assert.Equal("null", Kind((await _game.Eval("(string)null")).GetProperty("value")));
        Assert.False((await _game.Eval("var unused = 1;")).TryGetProperty("value", out _));
        var printed = await _game.Eval("Console.WriteLine(\"hi\"); 1 + 1");
        Assert.Equal("hi" + Environment.NewLine, printed.GetProperty("output").GetString());
        Assert.Equal("2", printed.GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task CompileErrorsPointAtTheCodeAsSent()
    {
        var player = (await _game.Eval("GameObject.Find(\"Player\")")).GetProperty("value").GetProperty("handle").GetInt32();
        var result = await _game.Eval($"${player}.nope");
        Assert.False(result.GetProperty("ok").GetBoolean());
        var error = result.GetProperty("error");
        Assert.Equal("compile_error", error.GetProperty("code").GetString());
        var diagnostic = error.GetProperty("diagnostics")[0];
        Assert.Equal("CS1061", diagnostic.GetProperty("id").GetString());
        Assert.Equal($"${player}.".Length + 1, diagnostic.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task RuntimeErrorsKeepEarlierVariables()
    {
        var failed = await _game.Eval("var kept = 41; throw new InvalidOperationException(\"boom\");");
        Assert.Equal("runtime_error", failed.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("boom", failed.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("42", (await _game.Eval("kept + 1")).GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ProgramModeRunsExecute()
    {
        var result = await _game.Eval("public static class P { public static object Execute() => Application.productName; }", "program");
        Assert.Equal("Fake Game", result.GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task InspectsGameObjectsAndWritesMembers()
    {
        var (_, found) = await _game.Get("/api/objects/search?q=Player");
        var player = found.EnumerateArray().First(n => n.GetProperty("name").GetString() == "Player").GetProperty("handle").GetInt32();

        var (_, inspected) = await _game.Get($"/api/objects/{player}");
        Assert.Contains("components", inspected.GetProperty("facets").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("Player", inspected.GetProperty("gameObject").GetProperty("path").GetString());

        var (_, components) = await _game.Get($"/api/objects/{player}/components");
        var controller = components.EnumerateArray().First(c => c.GetProperty("value").GetProperty("type").GetString() == "PlayerController")
            .GetProperty("value").GetProperty("handle").GetInt32();

        var (status, written) = await _game.Put($"/api/objects/{controller}/members/speed", new { value = "7.25" });
        Assert.Equal(200, status);
        Assert.Equal("7.25", written.GetProperty("value").GetProperty("text").GetString());

        var (_, transform) = await _game.Get($"/api/objects/{controller}/members/transform");
        var t = transform.GetProperty("value").GetProperty("handle").GetInt32();
        var (_, rotation) = await _game.Put($"/api/objects/{t}/members/localScale", new { value = "(2, 2, 2)" });
        Assert.Equal("(2, 2, 2)", rotation.GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task CallsMethodsByNameAndPagesCollections()
    {
        var controller = (await _game.Eval("GameObject.Find(\"Player\").GetComponent<PlayerController>()")).GetProperty("value").GetProperty("handle").GetInt32();
        var (status, result) = await _game.Post($"/api/objects/{controller}/methods/TakeDamage", new { args = new[] { 1 } });
        Assert.Equal(200, status);
        Assert.Equal("number", Kind(result));

        var (_, voidResult) = await _game.Post($"/api/objects/{controller}/methods/Heal", new { args = new[] { 1 } });
        Assert.Equal("void", Kind(voidResult));

        var inventory = (await _game.Eval($"${controller}.inventory")).GetProperty("value").GetProperty("handle").GetInt32();
        var (_, page) = await _game.Get($"/api/objects/{inventory}/items?offset=1&limit=2");
        Assert.Equal(4, page.GetProperty("count").GetInt32());
        Assert.Equal(new[] { "Potion", "Potion" }, page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("value").GetProperty("text").GetString()));
        Assert.True(page.GetProperty("more").GetBoolean());
    }

    [Theory]
    [InlineData("Constant", "constant")]
    [InlineData("ReadOnly", "field")]
    public async Task NativeFieldsRejectWritesDespitePublicSetters(string name, string kind)
    {
        var (_, type) = await _game.Get("/api/types/DerivedNativeFieldExample");
        var handle = type.GetProperty("handle").GetInt32();
        var (_, member) = await _game.Get($"/api/objects/{handle}/members/{name}");
        Assert.Equal(kind, member.GetProperty("kind").GetString());
        Assert.True(member.GetProperty("native").GetBoolean());
        Assert.False(member.TryGetProperty("writable", out var writable) && writable.GetBoolean());

        var (status, error) = await _game.Put($"/api/objects/{handle}/members/{name}", new { value = "0.5" });
        Assert.Equal(400, status);
        Assert.Contains("read-only", error.GetProperty("error").GetProperty("message").GetString());
        var (_, calls) = await _game.Get($"/api/objects/{handle}/members/SetterCalls");
        Assert.Equal("0", calls.GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task NativeFieldsAllowMutableFloatWrites()
    {
        var (_, type) = await _game.Get("/api/types/DerivedNativeFieldExample");
        var handle = type.GetProperty("handle").GetInt32();
        var (status, member) = await _game.Put($"/api/objects/{handle}/members/Mutable", new { value = "0.5" });
        Assert.Equal(200, status);
        Assert.Equal("field", member.GetProperty("kind").GetString());
        Assert.True(member.GetProperty("native").GetBoolean());
        Assert.True(member.GetProperty("writable").GetBoolean());
        Assert.Equal("0.5", member.GetProperty("value").GetProperty("text").GetString());
    }

    [Fact]
    public async Task DestroyedObjectsAnswerGone()
    {
        var go = (await _game.Eval("new GameObject(\"Doomed\")")).GetProperty("value").GetProperty("handle").GetInt32();
        await _game.Eval($"UnityEngine.Object.Destroy(${go});");
        var (status, json) = await _game.Get($"/api/objects/{go}");
        Assert.Equal(410, status);
        Assert.Equal("destroyed", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownRoutesSuggestTheRightOnes()
    {
        var (status, json) = await _game.Get("/api/objectz/1");
        Assert.Equal(404, status);
        Assert.Contains(json.GetProperty("error").GetProperty("details").GetProperty("suggestions").EnumerateArray(), s => s.GetString()!.Contains("/api/objects/{h}"));
    }

    [Fact]
    public async Task TypesResolveToStaticViews()
    {
        var (_, found) = await _game.Get("/api/types?q=pc");
        Assert.Equal("PlayerController", found[0].GetProperty("name").GetString());

        var (_, type) = await _game.Get("/api/types/GameManager");
        var statics = type.GetProperty("handle").GetInt32();
        var (_, view) = await _game.Get($"/api/objects/{statics}?statics=1");
        Assert.True(view.GetProperty("static").GetBoolean());
        Assert.Contains(view.GetProperty("members").EnumerateArray(), m => m.GetProperty("name").GetString() == "Instance" && m.TryGetProperty("value", out _));

        var (_, instances) = await _game.Get("/api/types/Enemy/instances");
        Assert.Equal(3, instances.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RendersImagesAsPng()
    {
        var sprite = (await _game.Eval("GameObject.Find(\"Health Bar\").GetComponent<HealthBar>().fill")).GetProperty("value").GetProperty("handle").GetInt32();
        var bytes = await _game.Http.GetByteArrayAsync($"/api/objects/{sprite}/image.png?max=64");
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, bytes[..4]);
    }

    [Fact]
    public async Task HooksRecordCallsWithArguments()
    {
        var (status, hook) = await _game.Post("/api/hooks", new { method = "PlayerController::TakeDamage(int)" });
        Assert.Equal(200, status);
        var id = hook.GetProperty("id").GetInt32();
        try
        {
            Assert.Contains("Prefix", (await _game.Get($"/api/hooks/{id}")).json.GetProperty("source").GetString());

            var player = (await _game.Eval("GameObject.Find(\"Player\").GetComponent<PlayerController>()")).GetProperty("value").GetProperty("handle").GetInt32();
            await _game.Post($"/api/objects/{player}/methods/TakeDamage", new { args = new[] { 3 } });

            var (_, calls) = await _game.Get($"/api/hooks/{id}/calls?wait=5");
            var call = calls.GetProperty("calls").EnumerateArray().First(c => c.GetProperty("args")[0].GetProperty("value").GetProperty("text").GetString() == "3");
            Assert.Equal("amount", call.GetProperty("args")[0].GetProperty("name").GetString());
            Assert.Equal("number", Kind(call.GetProperty("result")));
            Assert.Equal("PlayerController", call.GetProperty("instance").GetProperty("type").GetString());

            var (_, returning) = await _game.Patch($"/api/hooks/{id}", new { action = "return", returnValue = "-1" });
            Assert.Equal("return", returning.GetProperty("action").GetString());
            var (_, forced) = await _game.Post($"/api/objects/{player}/methods/TakeDamage", new { args = new[] { 3 } });
            Assert.Equal("-1", forced.GetProperty("text").GetString());

            var (bad, error) = await _game.Patch($"/api/hooks/{id}", new { action = "return", returnValue = "\"text\"" });
            Assert.Equal(400, bad);
            Assert.Equal("compile_error", error.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            await _game.Delete($"/api/hooks/{id}");
        }
    }

    [Fact]
    public async Task FoldedMethodsNeedConfirmation()
    {
        var (status, refused) = await _game.Post("/api/hooks", new { method = "PlayerController::OnRespawn" });
        Assert.Equal(409, status);
        Assert.Equal("folded", refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(refused.GetProperty("error").GetProperty("details").GetProperty("folded").EnumerateArray(), f => f.GetString() == "Enemy::OnRespawn");

        var (accepted, hook) = await _game.Post("/api/hooks", new { method = "PlayerController::OnRespawn", acceptFolded = true });
        Assert.Equal(200, accepted);
        try
        {
            var (conflict, json) = await _game.Post("/api/hooks", new { method = "Enemy::OnRespawn", acceptFolded = true });
            Assert.Equal(409, conflict);
            Assert.Equal("conflict", json.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            await _game.Delete($"/api/hooks/{hook.GetProperty("id").GetInt32()}");
        }
    }

    [Fact]
    public async Task LogsTailWithACursor()
    {
        var (_, first) = await _game.Get("/api/logs");
        var cursor = first.GetProperty("cursor").GetInt64();
        await _game.Eval("Debug.Log(\"tail me\");");
        var (_, next) = await _game.Get($"/api/logs?after={cursor}&wait=5");
        Assert.Contains(next.GetProperty("entries").EnumerateArray(), e => e.GetProperty("text").GetString() == "tail me");
    }
}
