using System;
using System.Runtime.InteropServices;

namespace NativeLibraryLoader.Native;

internal static partial class Kernel32
{
    [DllImport("kernel32")]
    public static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32")]
    public static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [LibraryImport("kernel32")]
    public static partial int FreeLibrary(IntPtr module);
}
