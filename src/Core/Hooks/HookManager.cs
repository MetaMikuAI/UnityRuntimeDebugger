using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityRuntimeDebugger.Hosting;
using UnityRuntimeDebugger.Http;
using UnityRuntimeDebugger.Inspection;
using UnityRuntimeDebugger.Logging;
using UnityRuntimeDebugger.Scripting;

namespace UnityRuntimeDebugger.Hooks;

public sealed class HookRequest
{
    /// <summary>Method reference, see <see cref="MethodRefs"/>.</summary>
    public string Method { get; init; } = "";

    public HookAction Action { get; init; } = HookAction.Log;

    /// <summary>C# expression returned by the <see cref="HookAction.Return"/> action.</summary>
    public string? ReturnValue { get; init; }

    /// <summary>Patch source to use instead of a generated one.</summary>
    public string? Source { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Hook even though other methods share the compiled function (ICF).</summary>
    public bool AcceptFolded { get; init; }
}

public sealed class HookUpdate
{
    public bool? Enabled { get; init; }
    public HookAction? Action { get; init; }
    public string? ReturnValue { get; init; }
    public string? Source { get; init; }
}

public sealed record NativeView(string Address, string? Location, IReadOnlyList<string>? Folded)
{
    public static NativeView? From(NativeMethod? native)
        => native == null ? null : new NativeView("0x" + native.Address.ToString("X"), native.Location, native.FoldedWith);
}

public sealed class HookView
{
    public int Id { get; init; }
    public string Method { get; init; } = "";
    public string DeclaringType { get; init; } = "";
    public string Signature { get; init; } = "";
    public HookAction Action { get; init; }
    public string? ReturnValue { get; init; }
    public bool Enabled { get; init; }
    public long Hits { get; init; }
    public long Recorded { get; init; }
    public long Dropped { get; init; }

    /// <summary>Calls let through because they reached the function via a folded method.</summary>
    public long Foreign { get; init; }

    public NativeView? Native { get; init; }
    public string? Error { get; init; }
    public long CreatedAt { get; init; }
    public string? Source { get; init; }
}

public sealed record ArgView(string Name, ValueView Value);

public sealed record CallView(
    long Seq,
    long Time,
    long Frame,
    int Thread,
    bool MainThread,
    ValueView? Instance,
    IReadOnlyList<ArgView> Args,
    ValueView? Result,
    string? Exception,
    double DurationUs);

public sealed record MethodCandidate(string Ref, string Signature, string DeclaringType, bool? Static, bool Hookable, string? Reason, NativeView? Native);

/// <summary>
/// Hooks: generated (or user-written) Harmony patches that record calls into a feed. Patches are
/// compiled with Roslyn into a regular (non-collectible) assembly, since detours outlive the call.
/// </summary>
internal sealed class HookManager : IDisposable
{
    private static readonly TimeSpan PatchTimeout = TimeSpan.FromSeconds(30);

    private readonly MainThread _main;
    private readonly IRuntime _runtime;
    private readonly Values _values;
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<int, Hook> _hooks = new();
    private readonly object _sync = new();
    private int _next;
    private int _compiled;

    public HookManager(MainThread main, IRuntime runtime, Values values, ObjectTable objects)
    {
        _main = main;
        _runtime = runtime;
        _values = values;
        _objects = objects;
    }

    public int Count => _hooks.Count;

    public List<HookView> List() => _hooks.Values.OrderBy(h => h.Id).Select(h => View(h, withSource: false)).ToList();

    public HookView Get(int id) => View(Find(id), withSource: true);

    public List<MethodCandidate> Candidates(string reference)
        => MethodRefs.Resolve(reference).Select(m =>
        {
            var reason = WhyNotHookable(m, out var native);
            return new MethodCandidate(MethodTable.Ref(m), MethodTable.Signature(m), TypeNames.Display(m.DeclaringType!),
                m.IsStatic ? true : null, reason == null, reason, NativeView.From(native));
        }).ToList();

    public HookView Create(HookRequest request)
    {
        var method = Resolve(request.Method);
        if (WhyNotHookable(method, out var native) is { } reason)
            throw ApiException.BadRequest($"{MethodTable.Ref(method)} cannot be hooked: {reason}", "/help/hooks");

        lock (_sync)
        {
            if (native != null && _hooks.Values.FirstOrDefault(h => h.Native?.Address == native.Address) is { } other)
            {
                throw new ApiException(409, "conflict",
                    $"hook #{other.Id} ({MethodTable.Ref(other.Method)}) already patches this native function", "/help/hooks#icf");
            }

            var folded = native?.FoldedWith ?? Array.Empty<string>();
            if (folded.Count > 0 && !request.AcceptFolded)
            {
                throw new ApiException(409, "folded",
                    $"{folded.Count} other method(s) share this compiled function (identical code folding); a hook fires for all of them. "
                    + "Calls from other classes are filtered out by instance type; pass acceptFolded: true to hook anyway.",
                    "/help/hooks#icf", new { folded });
            }

            var id = ++_next;
            var context = new HookContext(id, method, _main, _runtime, _values, filterByType: folded.Count > 0);
            var hook = new Hook(id, method, context, native)
            {
                Action = request.Source != null ? HookAction.Custom : request.Action,
                ReturnValue = request.ReturnValue,
            };

            HookContext.Register(context);
            try
            {
                hook.Source = request.Source ?? HookSource.Generate(id, method, request.Action, request.ReturnValue);
                hook.PatchType = Compile(hook.Source);
                if (request.Enabled)
                    Patch(hook);
            }
            catch
            {
                Unpatch(hook);
                HookContext.Unregister(id);
                throw;
            }

            _hooks[id] = hook;
            return View(hook, withSource: true);
        }
    }

    public HookView Update(int id, HookUpdate update)
    {
        lock (_sync)
        {
            var hook = Find(id);
            if (update.Source != null || update.Action != null || update.ReturnValue != null)
            {
                var action = update.Source != null ? HookAction.Custom : update.Action ?? hook.Action;
                var returnValue = update.ReturnValue ?? hook.ReturnValue;
                var source = update.Source ?? (action == HookAction.Custom ? hook.Source : HookSource.Generate(id, hook.Method, action, returnValue));
                var patchType = Compile(source);

                var wasEnabled = hook.Enabled;
                Unpatch(hook);
                (hook.Action, hook.ReturnValue, hook.Source, hook.PatchType) = (action, returnValue, source, patchType);
                if (update.Enabled ?? wasEnabled)
                    Patch(hook);
            }
            else if (update.Enabled is { } enabled && enabled != hook.Enabled)
            {
                if (enabled)
                    Patch(hook);
                else
                    Unpatch(hook);
            }

            return View(hook, withSource: true);
        }
    }

    public void Delete(int id)
    {
        lock (_sync)
        {
            var hook = Find(id);
            Unpatch(hook);
            HookContext.Unregister(id);
            _hooks.TryRemove(id, out _);
        }
    }

    public async Task<object> Calls(int id, long after, int limit, TimeSpan wait, CancellationToken cancellation)
    {
        var hook = Find(id);
        var page = await hook.Context.Calls.WaitAsync(after, Math.Clamp(limit, 1, 500), wait, cancellation);
        return new { calls = page.Items.Select(c => View(hook, c)).ToList(), cursor = page.Cursor, dropped = page.Dropped };
    }

    public void ClearCalls(int id) => Find(id).Context.Calls.Clear();

    public void Dispose()
    {
        foreach (var hook in _hooks.Values)
        {
            try
            {
                Unpatch(hook);
            }
            catch
            {
                // shutting down
            }

            HookContext.Unregister(hook.Id);
        }

        _hooks.Clear();
    }

    private Hook Find(int id) => _hooks.TryGetValue(id, out var hook) ? hook : throw ApiException.NotFound($"no hook #{id}", "/help/hooks");

    private static MethodBase Resolve(string reference)
    {
        try
        {
            return MethodRefs.ResolveOne(reference);
        }
        catch (Exception ex) when (ex is FormatException or TypeLoadException or MissingMethodException or AmbiguousMatchException)
        {
            throw ApiException.BadRequest(ex.Message, "/help/hooks");
        }
    }

    private string? WhyNotHookable(MethodBase method, out NativeMethod? native)
    {
        native = null;
        if (method.ContainsGenericParameters)
            return "open generic methods have no single compiled body";
        if (method.IsAbstract)
            return "abstract methods have no body; hook an override";

        native = _runtime.NativeMethodOf(method);
        return native is { Address: 0 } ? "no compiled code (stripped, or only reachable through generic sharing)" : null;
    }

    private Type Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            $"urdbg-hook-{Interlocked.Increment(ref _compiled)}",
            new[] { References.GlobalUsings(), CSharpSyntaxTree.ParseText(source) },
            References.All(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            var diagnostics = DiagnosticView.From(emit.Diagnostics, source, new List<HandleRewriter.Shift>());
            throw new ApiException(400, "compile_error", "the hook source does not compile: " + diagnostics.FirstOrDefault(), "/help/hooks", new { diagnostics });
        }

        // Detours must not point into an assembly that could be unloaded.
        var assembly = Assembly.Load(image.ToArray());
        return assembly.GetTypes().FirstOrDefault(t => t.IsAbstract && t.IsSealed && PatchMethods(t).Any())
               ?? throw ApiException.BadRequest("the hook source needs a static class with Prefix, Postfix, Finalizer or Transpiler methods", "/help/hooks");
    }

    private static IEnumerable<MethodInfo> PatchMethods(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer" or "Transpiler");

    /// <summary>On the main thread, so the detour is never written while the game runs the function.</summary>
    private void Patch(Hook hook)
    {
        HarmonyMethod? Of(string name) => hook.PatchType!.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) is { } m ? new HarmonyMethod(m) : null;

        try
        {
            _main.Run(() => hook.Harmony.Patch(hook.Method, Of("Prefix"), Of("Postfix"), Of("Transpiler"), Of("Finalizer"), ilmanipulator: null), PatchTimeout);
            hook.Enabled = true;
            hook.Error = null;
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            hook.Error = Values.Message(ex);
            throw new ApiException(400, "patch_failed", "Harmony could not patch the method: " + hook.Error, "/help/hooks");
        }
    }

    private void Unpatch(Hook hook)
    {
        if (!hook.Enabled)
            return;
        _main.Run(() => hook.Harmony.Unpatch(hook.Method, HarmonyPatchType.All, hook.Harmony.Id), PatchTimeout);
        hook.Enabled = false;
    }

    private HookView View(Hook hook, bool withSource) => new()
    {
        Id = hook.Id,
        Method = MethodTable.Ref(hook.Method),
        DeclaringType = TypeNames.Display(hook.Method.DeclaringType!),
        Signature = MethodTable.Signature(hook.Method),
        Action = hook.Action,
        ReturnValue = hook.ReturnValue,
        Enabled = hook.Enabled,
        Hits = hook.Context.Hits,
        Recorded = hook.Context.Recorded,
        Dropped = hook.Context.Dropped,
        Foreign = hook.Context.Foreign,
        Native = NativeView.From(hook.Native),
        Error = hook.Error,
        CreatedAt = hook.CreatedAt,
        Source = withSource ? hook.Source : null,
    };

    /// <summary>Values captured as live objects get handles only now, when a client looks at them.</summary>
    private CallView View(Hook hook, CallRecord call)
    {
        ValueView? Bind(Snapshot? snapshot)
        {
            if (snapshot is not { } s)
                return null;
            if (s.Target != null)
                s.View.Handle = _objects.Register(s.Target);
            return s.View;
        }

        var names = hook.Context.ArgNames;
        var args = call.Args.Select((a, i) => new ArgView(i < names.Length ? names[i] : "arg" + i, Bind(a)!)).ToList();
        return new CallView(call.Seq, call.Time, call.Frame, call.Thread, call.MainThread, Bind(call.Instance), args, Bind(call.Result), call.Exception, call.DurationUs);
    }

    private sealed class Hook
    {
        public Hook(int id, MethodBase method, HookContext context, NativeMethod? native)
        {
            Id = id;
            Method = method;
            Context = context;
            Native = native;
            Harmony = new Harmony("urdbg.hook." + id);
        }

        public int Id { get; }
        public MethodBase Method { get; }
        public HookContext Context { get; }
        public NativeMethod? Native { get; }
        public Harmony Harmony { get; }
        public long CreatedAt { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public HookAction Action { get; set; }
        public string? ReturnValue { get; set; }
        public string Source { get; set; } = "";
        public Type? PatchType { get; set; }
        public bool Enabled { get; set; }
        public string? Error { get; set; }
    }
}
