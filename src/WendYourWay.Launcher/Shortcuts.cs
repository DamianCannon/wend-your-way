using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WendYourWay.Launcher;

/// <summary>
/// Wend Your Way's shortcuts in the Start menu and on the desktop (#142, T138; user chose taskbar,
/// Start menu and desktop): to the launcher, with its icon and Wend Your Way's taskbar identity, so the
/// window sits under the icon once pinned. Windows 11 lets only the user pin to the taskbar.
/// </summary>
internal static class Shortcuts
{
    public static IReadOnlyList<string> Make(string launcher)
    {
        string[] made =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Wend Your Way.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wend Your Way.lnk"),
        ];
        foreach (var path in made) Make(launcher, path);
        // Its shortcuts from before the rename (T140), whose launcher has moved, and the practice map's own
        // (T161), switched to from Help instead (T163, user: "remove the shortcut"): gone, not left behind.
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
        {
            foreach (var former in new[] { "CutThrough.lnk", "Wend Your Way – practice map.lnk" })
            {
                var path = Path.Combine(Environment.GetFolderPath(folder), former);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        return made;
    }

    private static void Make(string launcher, string path)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(launcher);
        link.SetWorkingDirectory(Path.GetDirectoryName(launcher)!);
        link.SetIconLocation(launcher, 0);
        link.SetDescription("Plan running and riding Loops off the beaten track");
        var properties = (IPropertyStore)link;
        var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        var value = new PropVariant { Type = 31, Value = Marshal.StringToCoTaskMemUni(NativeMethods.AppId) };
        try
        {
            properties.SetValue(ref key, ref value);
            properties.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Value);
        }
        ((IPersistFile)link).Save(path, true);
        Marshal.ReleaseComObject(link);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid set, uint id)
    {
        public Guid Set = set;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Value;
        public IntPtr Value2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int max, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription(IntPtr name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation(IntPtr path, int max, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
