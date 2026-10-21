using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.AutoOrganize.Core;

internal static class HardLinkHelper
{
    public static void Create(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkWindows(destination, source, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return;
        }

        if (LinkUnix(source, destination) != 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinkUnix(string existingPath, string newPath);

    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newFileName, string existingFileName, IntPtr securityAttributes);
}
