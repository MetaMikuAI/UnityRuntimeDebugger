using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnityRuntimeDebugger.Tests;

/// <summary>The fake game running as its own process on a free port, with a JSON client for it.</summary>
public sealed class FakeGameProcess : IDisposable
{
    public const string Token = "test-token";

    private readonly Process _process;

    public FakeGameProcess()
    {
        var dll = typeof(FakeGameProcess).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "FakeGame").Value!;
        _process = Process.Start(new ProcessStartInfo("dotnet", $"\"{dll}\" 0 {Token}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var line = _process.StandardOutput.ReadLine() ?? throw new InvalidOperationException("the fake game exited: " + _process.StandardError.ReadToEnd());
            var match = Regex.Match(line, @"web console: (http://[^/]+)/#token=");
            if (!match.Success)
                continue;
            BaseUrl = match.Groups[1].Value;
            Http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(90) };
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            return;
        }

        throw new TimeoutException("the fake game did not print its address");
    }

    public string BaseUrl { get; } = "";

    public HttpClient Http { get; } = null!;

    public Task<(int status, JsonElement json)> Get(string path) => Send(HttpMethod.Get, path);

    public Task<(int status, JsonElement json)> Post(string path, object? body = null) => Send(HttpMethod.Post, path, body ?? new { });

    public Task<(int status, JsonElement json)> Put(string path, object body) => Send(HttpMethod.Put, path, body);

    public Task<(int status, JsonElement json)> Patch(string path, object body) => Send(HttpMethod.Patch, path, body);

    public Task<(int status, JsonElement json)> Delete(string path) => Send(HttpMethod.Delete, path);

    public async Task<(int status, JsonElement json)> Send(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
            request.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>Runs script code and returns the eval result.</summary>
    public async Task<JsonElement> Eval(string code, string mode = "script")
    {
        var (status, json) = await Post("/api/eval", new { code, mode });
        Assert.Equal(200, status);
        return json;
    }

    public void Dispose()
    {
        Http?.Dispose();
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }

        _process.Dispose();
    }
}
