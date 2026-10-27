using System;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern void RaiseException(uint code, uint flags, uint count, IntPtr arguments);

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[1] != "argument with spaces")
            return 99;

        if (args[0] == "crash")
            RaiseException(0xC0000005, 1, 0, IntPtr.Zero);

        return int.Parse(args[0]);
    }
}
