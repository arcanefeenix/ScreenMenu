namespace LedMenu.Rendering.Tests;

/// <summary>WPF imaging wants an STA thread; xunit test threads are not, so each test body runs on its own STA thread.</summary>
public static class Sta
{
    public static T Run<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    public static void Run(Action body) => Run<object?>(() => { body(); return null; });
}
