namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// The gate exists to make one guarantee: once <see cref="TaskLifetimeGate.Close"/> has returned, nothing it
/// guards is running or will start. Logging after that point does not lose a message - MSBuild throws, and
/// <c>AsyncStreamReader</c> rethrows on a thread-pool thread, which kills the build process.
/// </summary>
public class TaskLifetimeGateTests
{
    [Fact]
    public void TryRun_WhileOpen_ShouldRunTheWork()
    {
        var gate = new TaskLifetimeGate();
        var ran = false;

        Assert.True(gate.TryRun(() => ran = true));
        Assert.True(ran);
    }

    [Fact]
    public void TryRun_AfterClose_ShouldNotRunTheWork()
    {
        var gate = new TaskLifetimeGate();
        gate.Close();

        var ran = false;

        Assert.False(gate.TryRun(() => ran = true));
        Assert.False(ran);
    }

    [Fact]
    public void Close_ShouldBeIdempotent()
    {
        var gate = new TaskLifetimeGate();

        gate.Close();
        gate.Close();

        Assert.False(gate.TryRun(() => { }));
    }

    /// <summary>
    /// The property a plain flag could not give: a handler that has already passed the check must finish
    /// before <see cref="TaskLifetimeGate.Close"/> returns, or the task can complete underneath it.
    /// </summary>
    [Fact]
    public async Task Close_ShouldWaitForWorkAlreadyRunning()
    {
        var gate = new TaskLifetimeGate();
        using var workStarted = new ManualResetEventSlim(false);
        using var releaseWork = new ManualResetEventSlim(false);

        var running = Task.Run(() => gate.TryRun(() =>
        {
            workStarted.Set();
            releaseWork.Wait(TimeSpan.FromSeconds(30));
        }));

        Assert.True(workStarted.Wait(TimeSpan.FromSeconds(30)), "The guarded work never started.");

        var closing = Task.Run(gate.Close);

        Assert.NotSame(
            closing,
            await Task.WhenAny(closing, Task.Delay(TimeSpan.FromMilliseconds(250))));

        releaseWork.Set();

        await closing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(await running);
    }

    /// <summary>
    /// The race the flag left open: read the flag as open, get preempted, resume after the task returned.
    /// Repeated because a single pass would rarely land in the window.
    /// </summary>
    [Fact]
    public async Task NoWorkShouldEverRunAfterCloseReturns()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var gate = new TaskLifetimeGate();
            var closeReturned = false;
            var afterCloseRuns = 0;

            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    gate.TryRun(() =>
                    {
                        if (Volatile.Read(ref closeReturned))
                        {
                            Interlocked.Increment(ref afterCloseRuns);
                        }
                    });
                }
            })).ToArray();

            gate.Close();
            Volatile.Write(ref closeReturned, true);

            await Task.WhenAll(workers);

            Assert.Equal(0, afterCloseRuns);
        }
    }
}
