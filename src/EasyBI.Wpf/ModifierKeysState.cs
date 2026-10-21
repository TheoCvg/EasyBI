using System.Windows.Input;

namespace EasyBI.Wpf;

/// <summary>
/// The modifier keys held down. Tests set them on their own thread: they cannot fake the real keyboard, and must not
/// depend on what the person at the machine is pressing.
/// </summary>
internal static class ModifierKeysState
{
    [ThreadStatic] private static ModifierKeys? _simulated;

    public static ModifierKeys Current => _simulated ?? Keyboard.Modifiers;

    public static bool Ctrl => Current.HasFlag(ModifierKeys.Control);

    public static bool Shift => Current.HasFlag(ModifierKeys.Shift);

    /// <summary>Simulates <paramref name="keys"/> on the calling thread until the result is disposed.</summary>
    public static IDisposable Simulate(ModifierKeys keys)
    {
        var previous = _simulated;
        _simulated = keys;
        return new Restore(previous);
    }

    private sealed class Restore(ModifierKeys? previous) : IDisposable
    {
        public void Dispose() => _simulated = previous;
    }
}
