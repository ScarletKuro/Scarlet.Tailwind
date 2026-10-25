namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Compilations of one project only take turns if every build arrives at the same mutex for it, however its
/// directory was spelled - and builds of different projects never share one.
/// </summary>
public class TailwindRunLockTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock");

    [Fact]
    public void MutexName_IgnoresTrailingSeparatorAndRelativeSegments()
    {
        var plain = MutexNameFor(Path.Combine(Root, "project"));

        Assert.Equal(plain, MutexNameFor(Path.Combine(Root, "project") + Path.DirectorySeparatorChar));
        Assert.Equal(plain, MutexNameFor(Path.Combine(Root, "other", "..", "project")));
    }

    [Fact]
    public void MutexName_IgnoresCase()
    {
        // Windows paths are case-insensitive, so two builds can spell the same directory differently.
        Assert.Equal(MutexNameFor(Path.Combine(Root, "Project")), MutexNameFor(Path.Combine(Root, "project")));
    }

    [Fact]
    public void MutexName_DiffersBetweenProjects()
    {
        Assert.NotEqual(MutexNameFor(Path.Combine(Root, "a")), MutexNameFor(Path.Combine(Root, "b")));
    }

    [Fact]
    public void MutexName_DoesNotCollideWithTheDownloadMutex()
    {
        // Both are keyed on a hashed path. If a compilation's name could equal the download's, a compilation of
        // a project inside the runtime directory would deadlock against the download it is waiting on.
        var directory = Path.Combine(Root, "runtimes");

        Assert.NotEqual(TailwindDownloader.CreateMutexName(directory), MutexNameFor(directory));
    }

    [Fact]
    public void ResolveDirectory_WithoutAProjectDirectory_UsesTheCurrentDirectory()
    {
        var expected = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Assert.Equal(expected, TailwindRunLock.ResolveDirectory(" "));
    }

    [Fact]
    public void Acquire_WhileAnotherCompilationHoldsTheLock_WaitsForIt()
    {
        var project = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        string? waitedFor = null;
        var acquired = new ManualResetEventSlim();

        using (TailwindRunLock.Acquire(project, _ => Assert.Fail("Nothing else holds this lock yet.")))
        {
            var contender = new Thread(() =>
            {
                using (TailwindRunLock.Acquire(project, directory => waitedFor = directory))
                {
                    acquired.Set();
                }
            });
            contender.Start();

            Assert.False(acquired.Wait(TimeSpan.FromMilliseconds(300)), "The second compilation got the lock while the first held it.");
            Assert.Equal(TailwindRunLock.ResolveDirectory(project), waitedFor);

            // Released below by leaving the using block; the contender must then get through.
            Assert.False(acquired.IsSet);
        }

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10)), "The second compilation never got the lock after it was released.");
    }

    [Fact]
    public void Acquire_ForDifferentProjects_DoesNotWait()
    {
        using var first = TailwindRunLock.Acquire(Path.Combine(Root, Guid.NewGuid().ToString("N")), _ => Assert.Fail("First lock waited."));

        var acquired = false;
        var other = new Thread(() =>
        {
            using (TailwindRunLock.Acquire(Path.Combine(Root, Guid.NewGuid().ToString("N")), _ => Assert.Fail("Unrelated project waited.")))
            {
                acquired = true;
            }
        });
        other.Start();

        Assert.True(other.Join(TimeSpan.FromSeconds(10)));
        Assert.True(acquired);
    }

    private static string MutexNameFor(string projectDirectory) =>
        TailwindRunLock.CreateMutexName(TailwindRunLock.ResolveDirectory(projectDirectory));
}
