using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UnityRuntimeDebugger.Tests;

/// <summary>The shell examples in /help run as written.</summary>
public class HelpTests
{
    [Theory]
    [InlineData("")]
    [InlineData("eval")]
    [InlineData("objects")]
    [InlineData("hooks")]
    public async Task ExamplesRunAsWritten(string topic)
    {
        // A fresh game per page: examples count on the first handle and the first hook being 1.
        using var game = new FakeGameProcess();
        using var anonymous = new HttpClient { BaseAddress = new Uri(game.BaseUrl) };
        var page = await anonymous.GetStringAsync("/help/" + topic);

        var commands = Regex.Matches(page, "```sh\n(.*?)```", RegexOptions.Singleline)
            .SelectMany(m => m.Groups[1].Value.Split('\n'))
            .Where(line => line.StartsWith("curl ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(commands);

        foreach (var command in commands)
        {
            // -f turns HTTP errors into a failing exit code
            var (exit, output) = await Shell(command.Replace("curl -s", "curl -sSf"));
            Assert.True(exit == 0, $"`{command}` failed ({exit}): {output}");
        }
    }

    [Fact]
    public async Task PagesMentionedInTheIndexExist()
    {
        using var game = new FakeGameProcess();
        using var anonymous = new HttpClient { BaseAddress = new Uri(game.BaseUrl) };
        var index = await anonymous.GetStringAsync("/help");
        foreach (Match link in Regex.Matches(index, @"/help/([a-z0-9]+)"))
            Assert.Equal(200, (int)(await anonymous.GetAsync(link.Value)).StatusCode);
        Assert.Contains("GET /api/hooks/{id}/calls", await anonymous.GetStringAsync("/help/api"));
    }

    private static async Task<(int exit, string output)> Shell(string command)
    {
        var info = new ProcessStartInfo("sh", new[] { "-c", command }) { RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["T"] = FakeGameProcess.Token;
        using var process = Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
