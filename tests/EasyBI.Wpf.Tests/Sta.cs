using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace EasyBI.Wpf.Tests;

/// <summary>WPF elements need a single-threaded apartment: runs a test body on a dedicated STA thread.</summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // No modifier key held, whatever the person at the machine is pressing; a test simulates its own
                using var keys = ModifierKeysState.Simulate(System.Windows.Input.ModifierKeys.None);
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
