using System.Collections.Concurrent;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// <see cref="OutputCollector"/> is what a process's reader callbacks write into while the task reads from
/// it, so these cover the two things the callbacks made hard to see: what the tail renders, and that reading
/// while writing is safe.
/// </summary>
public class OutputCollectorTests
{
    [Fact]
    public void Tail_WithFewerLinesThanCapacity_ShouldRenderAllOfThemWithoutANotice()
    {
        var collector = new OutputCollector(capacity: 5, captureAll: false);

        collector.Add("first");
        collector.Add("second");

        Assert.Equal($"first{Environment.NewLine}second", collector.Tail);
        Assert.DoesNotContain("last", collector.Tail, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_WithNothingAdded_ShouldBeEmpty()
    {
        var collector = new OutputCollector(capacity: 5, captureAll: false);

        Assert.Equal(string.Empty, collector.Tail);
    }

    [Fact]
    public void Tail_WithMoreLinesThanCapacity_ShouldKeepTheLastAndSaySo()
    {
        var collector = new OutputCollector(capacity: 3, captureAll: false);

        for (var i = 1; i <= 10; i++)
        {
            collector.Add($"line {i}");
        }

        var tail = collector.Tail;

        Assert.StartsWith($"(last 3 lines){Environment.NewLine}", tail, StringComparison.Ordinal);
        Assert.Contains("line 8", tail, StringComparison.Ordinal);
        Assert.Contains("line 10", tail, StringComparison.Ordinal);

        // The dropped head has to be gone, not merely unmentioned - the notice is a promise about content.
        Assert.DoesNotContain("line 1" + Environment.NewLine, tail, StringComparison.Ordinal);
        Assert.DoesNotContain("line 7", tail, StringComparison.Ordinal);
    }

    [Fact]
    public void All_WhenNotCapturing_ShouldBeNull()
    {
        var collector = new OutputCollector(capacity: 5, captureAll: false);

        collector.Add("ignored");

        Assert.Null(collector.All);
        Assert.Contains("ignored", collector.Tail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tail is bounded; the capture is not. Truncating both would quietly shorten
    /// <c>TailwindCompileTask.StandardOutput</c>, which callers read in full.
    /// </summary>
    [Fact]
    public void All_WhenCapturing_ShouldKeepEverythingEvenPastTheTailCapacity()
    {
        var collector = new OutputCollector(capacity: 2, captureAll: true);

        for (var i = 1; i <= 20; i++)
        {
            collector.Add($"line {i}");
        }

        var all = collector.All;

        Assert.NotNull(all);
        Assert.Contains("line 1" + Environment.NewLine, all, StringComparison.Ordinal);
        Assert.Contains("line 20", all!, StringComparison.Ordinal);
        Assert.Equal(20, all!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>
    /// The regression test for the real defect: reads used to hit a bare <see cref="System.Text.StringBuilder"/>
    /// while the process's reader callbacks were still appending, which can tear the text or throw.
    /// </summary>
    [Fact]
    public async Task ReadingWhileWriting_ShouldNeitherThrowNorLoseLines()
    {
        // Enough concurrency to interleave a read with a write, and no more: readers call All, which renders
        // the whole accumulated buffer, so raising this makes the test quadratic rather than stronger.
        const int lineCount = 2_000;
        var collector = new OutputCollector(capacity: 50, captureAll: true);
        var failures = new ConcurrentQueue<Exception>();
        using var writersDone = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 4).Select(reader => Task.Run(() =>
        {
            try
            {
                while (!writersDone.IsCancellationRequested)
                {
                    _ = collector.All;
                    _ = collector.Tail;
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        })).ToArray();

        var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < lineCount / 4; i++)
                {
                    collector.Add($"writer {writer} line {i}");
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        })).ToArray();

        await Task.WhenAll(writers);
        writersDone.Cancel();
        await Task.WhenAll(readers);

        Assert.Empty(failures);

        var all = collector.All;
        Assert.NotNull(all);
        Assert.Equal(lineCount, all!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveCapacity_ShouldThrow(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputCollector(capacity, captureAll: false));
    }
}
