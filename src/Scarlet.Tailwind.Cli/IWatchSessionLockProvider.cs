using System.Diagnostics.CodeAnalysis;

namespace Scarlet.Tailwind.Cli;

internal interface IWatchSessionLockProvider
{
    bool TryAcquire(IReadOnlyList<TailwindWatchInvocation> invocations, [NotNullWhen(true)] out IDisposable? lease);
}
