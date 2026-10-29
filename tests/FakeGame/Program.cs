using UnityEngine;
using UnityRuntimeDebugger;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Logging;
using Object = UnityEngine.Object;

namespace FakeGame;

internal static class Program
{
    private static void Main(string[] args)
    {
        var port = args.Length > 0 ? int.Parse(args[0]) : 18765;
        var token = args.Length > 1 ? args[1] : "dev";
        var logFile = Path.Combine(Path.GetTempPath(), "fake-game.log");
        File.WriteAllText(logFile, "");

        BuildScene();

        var plugins = new[] { new PluginInfo("UnityRuntimeDebugger", "Unity Runtime Debugger", Debugger.Version) };
        using var debugger = Debugger.Start(new DebuggerOptions(token, new FakeHost(), new LoaderInfo("FakeGame", "1.0", () => plugins))
        {
            ListenAddress = "127.0.0.1",
            Runtime = new FakeRuntime(),
            Port = port,
            LogFile = logFile,
            OnError = Console.Error.WriteLine,
        });

        World.Log = (level, text) =>
        {
            var parsed = level switch { "warn" => LogLevel.Warn, "error" => LogLevel.Error, _ => LogLevel.Info };
            debugger.Logs.Add(parsed, "Unity", text);
            File.AppendAllText(logFile, $"[{parsed,-7}:   Unity] {text}\n");
        };
        Debug.Log("fake game started");

        // Tests read the first line to find the port when they pass 0.
        foreach (var url in debugger.Urls())
            Console.WriteLine($"web console: {url}/#token={token}");
        Console.Out.Flush();

        RunLoop(debugger);
    }

    /// <summary>The "main thread": ticks behaviours and drains the debugger's queue, ~60 fps.</summary>
    private static void RunLoop(Debugger debugger)
    {
        var frame = TimeSpan.FromSeconds(1 / 60.0);
        while (true)
        {
            var started = World.Clock.Elapsed;
            Time.frameCount++;
            Time.time += Time.deltaTime * Time.timeScale;

            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour.enabled && behaviour.gameObject.activeInHierarchy)
                {
                    try
                    {
                        behaviour.Update();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(ex.ToString());
                    }
                }
            }

            debugger.MainThread.Pump();
            var rest = frame - (World.Clock.Elapsed - started);
            if (rest > TimeSpan.Zero)
                Thread.Sleep(rest);
        }
    }

    private static void BuildScene()
    {
        var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
        camera.transform.position = new Vector3(0, 1, -10);

        var light = new GameObject("Directional Light", typeof(Light));
        light.transform.rotation = Quaternion.Euler(50, -30, 0);

        var player = new GameObject("Player", typeof(PlayerController), typeof(Rigidbody), typeof(MeshRenderer)) { tag = "Player" };
        var model = new GameObject("Model", typeof(MeshRenderer));
        model.transform.parent = player.transform;
        var weapon = new GameObject("Weapon", typeof(Spinner));
        weapon.transform.parent = player.transform;
        var controller = player.GetComponent<PlayerController>();
        controller.weapon = weapon.transform;

        var enemies = new GameObject("Enemies");
        for (var i = 1; i <= 3; i++)
        {
            var enemy = new GameObject($"Enemy ({i})", typeof(Enemy), typeof(MeshRenderer));
            enemy.transform.parent = enemies.transform;
            enemy.transform.position = new Vector3(i * 2, 0, 5);
            var e = enemy.GetComponent<Enemy>();
            e.target = controller;
            e.attackInterval = 1.5f + i;
        }

        var gradient = new Texture2D("Gradient", 128, 32, (x, y) => new Color32((byte)(x * 2), (byte)(80 + y * 4), 220, 255));
        var canvas = new GameObject("Canvas");
        var bar = new GameObject("Health Bar", typeof(HealthBar));
        bar.transform.parent = canvas.transform;
        var healthBar = bar.GetComponent<HealthBar>();
        healthBar.player = controller;
        healthBar.fill = new Sprite { name = "Fill", texture = gradient, textureRect = new Rect(0, 0, 96, 32) };

        new GameObject("Spawner", typeof(Spawner));

        var manager = new GameObject("GameManager", typeof(GameManager));
        Object.DontDestroyOnLoad(manager);
        var overlay = new GameObject("[Debug Overlay]");
        overlay.SetActive(false);
        Object.DontDestroyOnLoad(overlay);
    }
}
