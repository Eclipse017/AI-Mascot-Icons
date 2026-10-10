using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MascotIcons
{
    internal static class Tests
    {
        private static int count;
        private static string directory;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public IntPtr Icon; public int Index; public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
        private static byte[] ShellPixels(string path, out int nativeSize)
        {
            ShellFileInfo info;
            if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(ShellFileInfo)), 0x100) == IntPtr.Zero || info.Icon == IntPtr.Zero)
                throw new IOException("Shell did not resolve the icon.");
            try
            {
                BitmapSource native = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                nativeSize = native.PixelWidth;
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                FormatConvertedBitmap image = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream png = File.Create(Path.Combine(directory, Path.GetFileName(path) + ".shell.png"))) encoder.Save(png);
                if (image.PixelWidth != 32 || image.PixelHeight != 32) throw new IOException("Unexpected Shell icon size: " + image.PixelWidth + "x" + image.PixelHeight);
                byte[] pixels = new byte[32 * 32 * 4]; image.CopyPixels(pixels, 32 * 4, 0); return pixels;
            }
            finally { DestroyIcon(info.Icon); }
        }
        private static bool SimilarShellIcon(string shortcut, string ico)
        {
            int nativeSize;
            byte[] actual = ShellPixels(shortcut, out nativeSize), expected = new byte[32 * 32 * 4];
            // Shell large icons follow display scaling (64 px on this 200% DPI host).
            // Decode the SAME native frame before normalizing both images to 32 px.
            IntPtr expectedIcon = LoadImage(IntPtr.Zero, ico, 1, nativeSize, nativeSize, 0x10);
            if (expectedIcon == IntPtr.Zero) throw new IOException("Windows could not decode the reference ICO.");
            try
            {
                BitmapSource frame = Imaging.CreateBitmapSourceFromHIcon(expectedIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                FormatConvertedBitmap expectedImage = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(expectedImage));
                using (FileStream png = File.Create(shortcut + ".reference.png")) encoder.Save(png);
                expectedImage.CopyPixels(expected, 32 * 4, 0);
            }
            finally { DestroyIcon(expectedIcon); }
            long error = 0; int samples = 0;
            for (int y = 0; y < 32; ++y) for (int x = 0; x < 32; ++x)
            {
                if (x < 18 && y >= 14) continue; // Windows shortcut arrow overlay.
                int i = (y * 32 + x) * 4;
                // RGB in fully transparent pixels is unspecified.
                if (actual[i + 3] == 0 && expected[i + 3] == 0) continue;
                for (int c = 0; c < 4; ++c) { error += Math.Abs(actual[i + c] - expected[i + c]); ++samples; }
            }
            double mean = samples == 0 ? double.MaxValue : (double)error / samples;
            Console.WriteLine("Shell comparison mean channel error: " + mean.ToString("F3"));
            return samples > 0 && mean < 4;
        }
        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            ++count; Console.WriteLine("PASS: " + description);
        }
        private static void Refuses(Action action, string description)
        {
            bool refused = false;
            try { action(); } catch (IOException) { refused = true; } catch (UnauthorizedAccessException) { refused = true; }
            Assert(refused, description);
        }
        private static string Fixture(string name)
        {
            string path = Path.Combine(directory, name + ".lnk");
            NativeLink.CreateFixture(path, Path.Combine(directory, "nonexistent harmless target.exe"),
                "--launch placeholder --profile \"中文 用户\" --literal \"$() & %\"", Path.Combine(directory, "original.ico"));
            return path;
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                directory = Path.Combine(Path.GetFullPath(args[0]), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8), "中文 空格");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "original.ico"), IconStore.BuiltIn[4].Bytes());
                ShortcutService service = new ShortcutService(Path.Combine(directory, "data"));
                if (args.Length == 2 && args[1] == "--ui")
                {
                    string uiFile = Fixture("UI 测试 - 从不启动应用");
                    File.WriteAllText(Path.Combine(Path.GetFullPath(args[0]), "ui-latest.txt"), directory);
                    Application uiApp = new Application();
                    uiApp.Run(new MainWindow(service, false, uiFile));
                    return 0;
                }
                foreach (IconChoice choice in IconStore.BuiltIn)
                {
                    IconStore.Validate(choice.Bytes());
                    Assert(choice.Preview() != null, choice.Id + " embedded ICO and preview decode");
                    string shellFile = Fixture("shell-" + choice.Id);
                    service.Apply(shellFile, choice.Bytes());
                    Assert(SimilarShellIcon(shellFile, NativeLink.Read(shellFile).IconPath), choice.Id + " Windows Shell resolves the actual new shortcut icon");
                }

                string file = Fixture("测试 shortcut");
                string original = IconStore.FileHash(file);
                LinkInfo before = NativeLink.Read(file);
                service.Apply(file, IconStore.BuiltIn[0].Bytes());
                LinkInfo after = NativeLink.Read(file);
                Assert(before.SameLaunch(after), "launch target, arguments, working directory, hotkey, show mode, IDList, flags and extra data preserved");
                Assert(after.IconIndex == 0 && IconStore.FileHash(after.IconPath) == IconStore.Hash(IconStore.BuiltIn[0].Bytes()), "icon stored durably outside download folder");
                Assert(IconStore.FileHash(service.BackupPath(file)) == original, "original shortcut backed up byte-for-byte");
                Assert(service.State(file) == "可恢复", "applied shortcut marked recoverable");
                service.Apply(file, IconStore.BuiltIn[1].Bytes());
                Assert(IconStore.FileHash(service.BackupPath(file)) == original, "second theme change retains first backup");
                service.Restore(file);
                Assert(IconStore.FileHash(file) == original && service.State(file) == "已恢复", "restore returns exact original bytes");
                service.Restore(file);
                Assert(IconStore.FileHash(file) == original, "repeat restore is idempotent");
                service.Apply(file, IconStore.BuiltIn[2].Bytes());
                Assert(before.SameLaunch(NativeLink.Read(file)), "reapply after restore preserves launch settings");

                // Simulate an application updater changing an already modified shortcut.
                NativeLink.CreateFixture(file, Path.Combine(directory, "updated.exe"), "--new-setting", Path.Combine(directory, "original.ico"));
                string updated = IconStore.FileHash(file);
                Refuses(delegate { service.Restore(file); }, "refuse restoring over an externally updated shortcut");
                Refuses(delegate { service.Apply(file, IconStore.BuiltIn[0].Bytes()); }, "refuse reapplying over an externally updated shortcut");
                Assert(IconStore.FileHash(file) == updated, "external update remains byte-identical after refusals");

                string corrupted = Fixture("corrupt-backup"); service.Apply(corrupted, IconStore.BuiltIn[0].Bytes());
                string changed = IconStore.FileHash(corrupted);
                File.AppendAllText(service.BackupPath(corrupted), "damage");
                Refuses(delegate { service.Restore(corrupted); }, "corrupt backup is rejected");
                Assert(IconStore.FileHash(corrupted) == changed, "corrupt backup never changes shortcut");

                string interrupted = Fixture("interrupted"); string interruptedOriginal = IconStore.FileHash(interrupted);
                service.AfterJournalSaved = delegate { throw new IOException("simulated interruption"); };
                Refuses(delegate { service.Apply(interrupted, IconStore.BuiltIn[0].Bytes()); }, "injected pre-commit interruption");
                Assert(IconStore.FileHash(interrupted) == interruptedOriginal, "pre-commit interruption keeps original intact");
                service.AfterJournalSaved = null;
                service.Apply(interrupted, IconStore.BuiltIn[0].Bytes());
                BackupRecord pending = service.Load(interrupted);
                pending.AppliedHash = null; service.Save(interrupted, pending);
                new ShortcutService(service.Root).Restore(interrupted);
                Assert(IconStore.FileHash(interrupted) == interruptedOriginal, "pending journal recovers after simulated post-commit crash and restart");

                string concurrent = Fixture("changed-during-apply");
                string concurrentHash = null;
                service.AfterJournalSaved = delegate
                {
                    NativeLink.CreateFixture(concurrent, Path.Combine(directory, "other.exe"), "--changed", Path.Combine(directory, "original.ico"));
                    concurrentHash = IconStore.FileHash(concurrent);
                };
                Refuses(delegate { service.Apply(concurrent, IconStore.BuiltIn[0].Bytes()); }, "concurrent external update stops replacement");
                service.AfterJournalSaved = null;
                Assert(IconStore.FileHash(concurrent) == concurrentHash, "concurrent edit preserved");

                string badIcon = Fixture("invalid-icon"); string badOriginal = IconStore.FileHash(badIcon);
                Refuses(delegate { service.Apply(badIcon, new byte[100]); }, "invalid icon rejected");
                Assert(IconStore.FileHash(badIcon) == badOriginal, "invalid icon leaves shortcut unchanged");
                Refuses(delegate { service.Apply(Path.Combine(directory, "application.exe"), IconStore.BuiltIn[0].Bytes()); }, "executable modification rejected");
                Refuses(delegate { service.Restore(badIcon); }, "restore without a backup rejected");

                string readonlyLink = Fixture("readonly"); File.SetAttributes(readonlyLink, FileAttributes.ReadOnly);
                string readonlyHash = IconStore.FileHash(readonlyLink);
                try { Refuses(delegate { service.Apply(readonlyLink, IconStore.BuiltIn[0].Bytes()); }, "read-only shortcut fails without elevation"); }
                finally { File.SetAttributes(readonlyLink, FileAttributes.Normal); }
                Assert(IconStore.FileHash(readonlyLink) == readonlyHash, "read-only shortcut remains intact");

                string elevated = Fixture("run-as-flag"); byte[] runAs = File.ReadAllBytes(elevated);
                runAs[21] = (byte)(runAs[21] | 0x20); File.WriteAllBytes(elevated, runAs);
                LinkInfo elevatedBefore = NativeLink.Read(elevated);
                service.Apply(elevated, IconStore.BuiltIn[3].Bytes());
                Assert(elevatedBefore.SameLaunch(NativeLink.Read(elevated)) && (File.ReadAllBytes(elevated)[21] & 0x20) != 0, "RunAsUser flag preserved");

                string store = Path.Combine(directory, "Store-style.lnk");
                NativeLink.CreateFixture(store, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    @"shell:AppsFolder\Example.Package!App", Path.Combine(directory, "original.ico"));
                LinkInfo storeBefore = NativeLink.Read(store); service.Apply(store, IconStore.BuiltIn[0].Bytes());
                Assert(storeBefore.SameLaunch(NativeLink.Read(store)), "Store-style explorer arguments preserved without launching");
                Assert(IconStore.Suggest("Gemini Notebook") == null && IconStore.Suggest("Antigravity") == IconStore.BuiltIn[2], "name suggestions exclude Gemini Notebook");
                Assert(!Directory.GetFiles(directory, ".mascot-*.lnk").Any(), "staged shortcuts cleaned after successes and failures");
                Assert(!File.Exists(Path.Combine(directory, "nonexistent harmless target.exe")), "test targets remain nonexistent and were never invoked");

                Application app = new Application();
                MainWindow window = new MainWindow(service, true);
                window.Show(); window.UpdateLayout();
                window.SavePreview(Path.Combine(directory, "preview.png"));
                Assert(window.Rows.Count == 5 && window.ActualWidth >= 860, "five-character WPF preview renders");
                window.Close();
                string report = "PASS: " + count + " checks. Test fixtures only; no AI application launched.\nArtifacts: " + directory;
                File.WriteAllText(Path.Combine(directory, "result.txt"), report);
                File.WriteAllText(Path.Combine(Path.GetFullPath(args[0]), "latest.txt"), directory);
                Console.WriteLine(report);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
