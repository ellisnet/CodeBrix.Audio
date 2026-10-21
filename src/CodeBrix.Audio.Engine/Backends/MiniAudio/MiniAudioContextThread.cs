using System.Collections.Concurrent;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio;

// Runs ma_context_init and ma_context_uninit on one dedicated thread on Windows.
// miniaudio's Win32 context init calls CoInitializeEx on the CALLING thread, and its uninit calls
// CoUninitialize on ITS calling thread whenever that init succeeded. An engine created on one
// thread and disposed on another - a game loop starts the shared output, the UI thread shuts it
// down - therefore uninitializes COM on a thread that never asked for it. On an STA UI thread that
// tears the apartment down, and COM-based components such as WebView2 stop working for the rest of
// the process. Pinning both calls to this thread keeps the pair balanced and leaves application
// threads' COM state alone. Other platforms have no COM and run the calls inline.
internal static class MiniAudioContextThread
{
    private static readonly BlockingCollection<Action> WorkItems = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker);

    public static T Invoke<T>(Func<T> function)
    {
        if (!OperatingSystem.IsWindows())
            return function();

        if (Thread.CurrentThread == Worker.Value)
            return function();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkItems.Add(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        });
        return completion.Task.GetAwaiter().GetResult();
    }

    public static void Invoke(Action action) => Invoke(() =>
    {
        action();
        return true;
    });

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var item in WorkItems.GetConsumingEnumerable())
                item();
        })
        {
            IsBackground = true,
            Name = "CodeBrix.Audio miniaudio context"
        };
        // A new thread is MTA by default, which is also what miniaudio asks COM for.
        thread.Start();
        return thread;
    }
}
