if (args is ["--hold-lock", var lockPath, var readyPath])
{
    Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
    using var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    File.WriteAllText(readyPath, Environment.ProcessId.ToString());
    await Task.Delay(Timeout.InfiniteTimeSpan);

    return 0;
}

Console.Out.WriteLine("STDOUT_CONTEXT");
Console.Error.WriteLine("STDERR_CONTEXT");
return 3;
