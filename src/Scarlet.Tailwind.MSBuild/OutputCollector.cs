using System;
using System.Collections.Generic;
using System.Text;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Accumulates one redirected stream: the whole thing when asked to, and always a bounded tail for the
/// failure message.
/// </summary>
/// <remarks>
/// Its own type rather than a <see cref="StringBuilder"/> and a queue side by side, because the writes come
/// from <c>Process</c>'s reader callbacks on the thread pool while the task reads from the build thread.
/// Keeping the text, the tail and the truncation flag behind one lock is what makes that safe, and makes it
/// hard to add a fourth field later that forgets to be.
/// </remarks>
internal sealed class OutputCollector
{
    private readonly Queue<string> _tail;
    private readonly StringBuilder? _all;
    private readonly int _capacity;
    private bool _truncated;

    public OutputCollector(int capacity, bool captureAll)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;
        _tail = new Queue<string>(capacity);
        _all = captureAll ? new StringBuilder() : null;
    }

    public void Add(string line)
    {
        lock (_tail)
        {
            _all?.AppendLine(line);
            _tail.Enqueue(line);

            if (_tail.Count > _capacity)
            {
                _tail.Dequeue();
                _truncated = true;
            }
        }
    }

    /// <summary>
    /// Everything added, or <see langword="null"/> when this stream is not being retained.
    /// </summary>
    public string? All
    {
        get
        {
            lock (_tail)
            {
                return _all?.ToString();
            }
        }
    }

    /// <summary>
    /// The last lines added, prefixed with a count when earlier ones were dropped.
    /// </summary>
    /// <remarks>
    /// An empty collector renders as "" without a special case: it can never be truncated, because dropping
    /// a line leaves the queue full.
    /// </remarks>
    public string Tail
    {
        get
        {
            lock (_tail)
            {
                var prefix = _truncated
                    ? $"(last {_tail.Count} lines){Environment.NewLine}"
                    : string.Empty;

                return prefix + string.Join(Environment.NewLine, _tail);
            }
        }
    }
}
