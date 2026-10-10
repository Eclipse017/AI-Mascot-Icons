using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("AI 娘图标换装")]
[assembly: AssemblyDescription("离线更换 Windows 快捷方式图标，自动备份与恢复")]
[assembly: AssemblyCompany("Eclipse017")]
[assembly: AssemblyProduct("AI-Mascot-Icons")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MascotIcons
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool preview = args.Length == 2 && args[0] == "--preview";
            if (args.Length != 0 && !preview) return 2;
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\AI-Mascot-Icons.v1", out created))
            {
                if (!created) { MessageBox.Show("图标换装工具已经打开，请回到已有窗口。", "AI 娘图标换装"); return 0; }
                try
                {
                    string root = preview ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "preview-data") :
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Mascot-Icons");
                    ShortcutService service = new ShortcutService(root);
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    MainWindow window = new MainWindow(service, preview);
                    if (preview) window.ContentRendered += delegate { window.SavePreview(args[1]); window.Close(); };
                    app.Run(window);
                    return 0;
                }
                catch (Exception ex)
                {
                    if (preview) File.WriteAllText(args[1] + ".error.txt", ex.ToString());
                    else MessageBox.Show("程序未能完成操作：\n" + ex.Message, "AI 娘图标换装", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
