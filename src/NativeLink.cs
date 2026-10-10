using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MascotIcons
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    internal sealed class LinkInfo
    {
        public string Target, Arguments, WorkingDirectory, Description, IconPath, IdList, ExtraData;
        public uint LaunchFlags;
        public int IconIndex, ShowCommand;
        public short Hotkey;
        public bool SameLaunch(LinkInfo other)
        {
            return Target == other.Target && Arguments == other.Arguments &&
                WorkingDirectory == other.WorkingDirectory && Description == other.Description &&
                ShowCommand == other.ShowCommand && Hotkey == other.Hotkey && IdList == other.IdList &&
                LaunchFlags == other.LaunchFlags && ExtraData == other.ExtraData;
        }
    }

    internal static class NativeLink
    {
        [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string first, IntPtr second);

        internal static LinkInfo Read(string file)
        {
            object obj = new ShellLinkObject();
            try
            {
                ((IPersistFile)obj).Load(file, 0);
                IShellLinkW link = (IShellLinkW)obj;
                LinkInfo info = new LinkInfo();
                StringBuilder buffer = new StringBuilder(32768);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 4); info.Target = buffer.ToString();
                buffer.Clear(); link.GetArguments(buffer, buffer.Capacity); info.Arguments = buffer.ToString();
                buffer.Clear(); link.GetWorkingDirectory(buffer, buffer.Capacity); info.WorkingDirectory = buffer.ToString();
                buffer.Clear(); link.GetDescription(buffer, buffer.Capacity); info.Description = buffer.ToString();
                buffer.Clear(); link.GetIconLocation(buffer, buffer.Capacity, out info.IconIndex); info.IconPath = buffer.ToString();
                link.GetHotkey(out info.Hotkey); link.GetShowCmd(out info.ShowCommand);
                IntPtr id;
                link.GetIDList(out id);
                try
                {
                    int length = id == IntPtr.Zero ? 0 : checked((int)ILGetSize(id));
                    if (length > 1048576) throw new IOException("快捷方式的目标数据过大。");
                    byte[] bytes = new byte[length];
                    if (length != 0) Marshal.Copy(id, bytes, 0, length);
                    info.IdList = Convert.ToBase64String(bytes);
                }
                finally { if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id); }
                ReadProtectedMetadata(file, info);
                return info;
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        private static void ReadProtectedMetadata(string file, LinkInfo info)
        {
            // Preserve RunAsUser and Shell metadata such as AppUserModelID, environment
            // targets, console settings and installer descriptors. Never call Resolve.
            byte[] bytes = File.ReadAllBytes(file);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (reader.ReadUInt32() != 76) throw new IOException("不是有效的 Windows 快捷方式。");
                reader.BaseStream.Position = 20;
                uint flags = reader.ReadUInt32();
                info.LaunchFlags = flags & ~0x4040U; // IconLocation and expandable icon flags only.
                reader.BaseStream.Position = 76;
                if ((flags & 1) != 0) { int size = reader.ReadUInt16(); reader.BaseStream.Position += size; }
                if ((flags & 2) != 0) { long start = reader.BaseStream.Position; uint size = reader.ReadUInt32(); if (size < 4) throw new IOException("无效的快捷方式数据。"); reader.BaseStream.Position = checked(start + size); }
                foreach (uint bit in new uint[] { 4, 8, 16, 32, 64 })
                    if ((flags & bit) != 0) { int count = reader.ReadUInt16(); reader.BaseStream.Position += checked(count * ((flags & 128) != 0 ? 2 : 1)); }
                System.Collections.Generic.List<string> blocks = new System.Collections.Generic.List<string>();
                while (reader.BaseStream.Position < bytes.Length)
                {
                    long start = reader.BaseStream.Position;
                    uint size = reader.ReadUInt32();
                    if (size == 0) break;
                    if (size < 8 || size > 4 * 1024 * 1024 || start + size > bytes.Length) throw new IOException("无效的快捷方式扩展数据。");
                    uint signature = reader.ReadUInt32();
                    // TrackerData can be regenerated by Windows; IconEnvironment is the icon.
                    if (signature != 0xA0000003 && signature != 0xA0000007)
                        blocks.Add(Convert.ToBase64String(bytes, checked((int)start), checked((int)size)));
                    reader.BaseStream.Position = start + size;
                }
                blocks.Sort(StringComparer.Ordinal);
                info.ExtraData = string.Join("|", blocks.ToArray());
            }
        }

        internal static void SetIcon(string file, string icon, int index)
        {
            object obj = new ShellLinkObject();
            try
            {
                IPersistFile persist = (IPersistFile)obj;
                persist.Load(file, 0);
                ((IShellLinkW)obj).SetIconLocation(icon, index);
                persist.Save(file, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        // Tests create inert links; production never creates or invokes an application target.
        internal static void CreateFixture(string file, string target, string args, string icon)
        {
            object obj = new ShellLinkObject();
            try
            {
                IShellLinkW link = (IShellLinkW)obj;
                link.SetPath(target); link.SetArguments(args);
                link.SetWorkingDirectory(Path.GetDirectoryName(file));
                link.SetDescription("测试：中文、空格与保护启动参数");
                link.SetHotkey(0x0641); link.SetShowCmd(7);
                link.SetIconLocation(icon, 0);
                ((IPersistFile)obj).Save(file, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        internal static void Notify(string file)
        {
            // SHCNE_UPDATEITEM + SHCNF_PATHW + SHCNF_FLUSHNOWAIT. No Explorer restart.
            SHChangeNotify(0x2000, 0x2005, file, IntPtr.Zero);
        }
    }
}
