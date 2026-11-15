using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.UI.Xaml.Media;

namespace QuranReader.App;

/// <summary>Opt-in, bounded local Release diagnostics. No logging or worker when disabled.</summary>
internal static class NavigationTrace
{
    private sealed record Entry(long Operation, string Stage, long Qpc, int Thread, bool UiThread, string? Detail);
    private static readonly string? output = Environment.GetEnvironmentVariable("QURAN_READER_NAVIGATION_TRACE");
    private static readonly Channel<Entry> events = Channel.CreateBounded<Entry>(new BoundedChannelOptions(8192) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private static readonly List<EventHandler<object>> frameHandlers = [];
    private static Task? writer;
    private static int uiThread;
    private static long sequence, current, inputStamp, dropped;
    private static bool finished;
    private static string? inputKind;
    internal static readonly AsyncLocal<bool> IdlePreparation = new();
    internal static bool Enabled => !finished && !string.IsNullOrWhiteSpace(output);
    internal static long Current => Enabled && !IdlePreparation.Value ? Interlocked.Read(ref current) : 0;

    internal static void Initialize()
    {
        if (!Enabled || writer is not null) return;
        uiThread = Environment.CurrentManagedThreadId;
        writer = Task.Run(async () =>
        {
            var directory = Path.GetFullPath(output!);
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(Path.Combine(directory, "navigation.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, true);
            await using var text = new StreamWriter(stream);
            await text.WriteLineAsync(JsonSerializer.Serialize(new { Stage = "Session", Frequency = Stopwatch.Frequency, Source = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, Pid = Environment.ProcessId }));
            await foreach (var entry in events.Reader.ReadAllAsync())
            {
                await text.WriteLineAsync(JsonSerializer.Serialize(entry));
                await text.FlushAsync();
            }
            await text.WriteLineAsync(JsonSerializer.Serialize(new { Stage = "TraceCompleted", Dropped = Interlocked.Read(ref dropped) }));
        });
    }

    internal static void Input(string kind)
    {
        if (!Enabled) return;
        inputStamp = Stopwatch.GetTimestamp(); inputKind = kind;
    }

    internal static long Begin(string kind, string? target = null)
    {
        if (!Enabled) return 0;
        foreach (var handler in frameHandlers) CompositionTarget.Rendering -= handler;
        frameHandlers.Clear();
        var accepted = Stopwatch.GetTimestamp();
        var previous = Current;
        if (previous != 0) Mark(previous, "SupersededOrNextIntent");
        var id = Interlocked.Increment(ref sequence); Interlocked.Exchange(ref current, id);
        var physical = inputKind?.EndsWith(kind, StringComparison.Ordinal) == true && Stopwatch.GetElapsedTime(inputStamp, accepted).TotalSeconds < 1;
        Write(id, "InputReceived", physical ? inputStamp : accepted, physical ? inputKind : "programmatic-command");
        inputKind = null;
        Write(id, "CommandAccepted", accepted, kind);
        Mark(id, "IntentResolved", target);
        Frames(id, "FirstResponseRender", 1);
        return id;
    }

    internal static void Mark(long id, string stage, string? detail = null)
    {
        if (Enabled) Write(id, stage, Stopwatch.GetTimestamp(), detail);
    }

    private static void Write(long id, string stage, long stamp, string? detail)
    {
        var thread = Environment.CurrentManagedThreadId;
        if (!events.Writer.TryWrite(new(id, stage, stamp, thread, thread == uiThread, detail))) Interlocked.Increment(ref dropped);
    }

    internal static void Incoming(long id)
    {
        if (id == 0) return;
        Mark(id, "IncomingVisualsReady");
        Frames(id, "IncomingFirstRender", 1);
    }

    internal static async Task AnimationAsync(long id, Task completion)
    {
        if (id == 0) return;
        await completion;
        Mark(id, "AnimationCompleted");
        if (id == Current) Frames(id, "StablePresentation", 2);
    }

    private static void Frames(long id, string stage, int remaining)
    {
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (id != Current || --remaining == 0)
            {
                CompositionTarget.Rendering -= handler;
                frameHandlers.Remove(handler!);
                if (id == Current) Mark(id, stage, "native Rendering callback; not GPU scanout instrumentation");
            }
        };
        frameHandlers.Add(handler); CompositionTarget.Rendering += handler;
    }

    internal static async Task FinishAsync()
    {
        if (writer is null) return;
        finished = true;
        foreach (var handler in frameHandlers) CompositionTarget.Rendering -= handler;
        frameHandlers.Clear(); events.Writer.TryComplete();
        await writer; // Diagnostic I/O failures must fail the run, never be concealed.
    }
}
