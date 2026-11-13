using System.Reflection;
using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>
/// MoonSharp reserves two 128 K-entry stacks (about 2 MB) for every Lua state and for every coroutine it creates. Scripts here run each event in a fresh coroutine, so that would cost 2 MB per script and 2 MB of garbage per event.
/// This shrinks those stacks to what scripts need: 8 K values and 2 K call frames (a recursion depth of about two thousand calls; deeper than that fails cleanly with an error instead of eating memory).
/// It reaches into private fields of the pinned MoonSharp 2.0.0; if they are not there (another version) it does nothing, and a test notices that memory per script went back up.
/// </summary>
public static class MoonSharpStacks
{
    public const int ValueSlots = 8192, CallFrames = 2048;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly FieldInfo? MainProcessor = typeof(Script).GetField("m_MainProcessor", Any);
    private static readonly FieldInfo? CoroutineProcessor = typeof(Coroutine).GetFields(Any).FirstOrDefault(f => f.FieldType.Name == "Processor");
    private static FieldInfo? _valueStack, _executionStack;
    private static readonly Dictionary<Type, (FieldInfo? Storage, FieldInfo? Head)> Layouts = [];

    public static bool Available => MainProcessor is not null && CoroutineProcessor is not null;

    public static void Shrink(Script script) { try { if (MainProcessor?.GetValue(script) is { } processor) Resize(processor); } catch (Exception exception) when (exception is FieldAccessException or TargetException or InvalidCastException or ArgumentException or NullReferenceException) { } }

    public static void Shrink(Coroutine coroutine) { try { if (CoroutineProcessor?.GetValue(coroutine) is { } processor) Resize(processor); } catch (Exception exception) when (exception is FieldAccessException or TargetException or InvalidCastException or ArgumentException or NullReferenceException) { } }

    private static void Resize(object processor)
    {
        _valueStack ??= processor.GetType().GetField("m_ValueStack", Any); _executionStack ??= processor.GetType().GetField("m_ExecutionStack", Any);
        Resize(_valueStack?.GetValue(processor), ValueSlots); Resize(_executionStack?.GetValue(processor), CallFrames);
    }

    private static (FieldInfo? Storage, FieldInfo? Head) LayoutOf(Type type)
    {
        lock (Layouts) { if (!Layouts.TryGetValue(type, out var layout)) Layouts[type] = layout = (type.GetField("m_Storage", Any), type.GetField("m_HeadIdx", Any)); return layout; }
    }

    private static void Resize(object? stack, int size)
    {
        if (stack is null) return;
        var layout = LayoutOf(stack.GetType()); var current = layout.Storage?.GetValue(stack) as Array;
        if (current is null || current.Length <= size) return;
        var head = layout.Head?.GetValue(stack) as int?; if (head is null || head >= size) return; // unknown layout, or in use deeper than the new size: leave it
        var smaller = Array.CreateInstance(current.GetType().GetElementType()!, size); Array.Copy(current, smaller, Math.Min(head ?? 0, size)); layout.Storage!.SetValue(stack, smaller);
    }
}
