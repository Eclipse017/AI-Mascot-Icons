using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
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
        private static string Fixture(string name, string folder = null)
        {
            string targetDir = folder ?? directory;
            string path = Path.Combine(targetDir, name + ".lnk");
            NativeLink.CreateFixture(path, Path.Combine(directory, "nonexistent harmless target.exe"),
                "--launch placeholder --profile \"中文 用户\" --literal \"$() & %\"", Path.Combine(directory, "original.ico"));
            return path;
        }
        private static T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; ++i)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T typed = child as T;
                if (typed != null) return typed;
                T nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
        private static bool HasVisualAncestor(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }
        private static DependencyObject HitTestAt(Visual root, FrameworkElement relativeTo, Point point)
        {
            Point pt = relativeTo.TransformToAncestor(root).Transform(point);
            HitTestResult hit = VisualTreeHelper.HitTest(root, pt);
            return hit == null ? null : hit.VisualHit;
        }
        private static Rect BoundsInAncestor(FrameworkElement element, Visual ancestor)
        {
            return element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        private static bool InsideBounds(Rect inner, Rect outer)
        {
            return inner.Width > 0 && inner.Height > 0 &&
                inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
                inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;
        }
        private static void Click(ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        private static void TestPreviewLazyDecode(ShortcutService service)
        {
            foreach (IconChoice choice in IconStore.BuiltIn)
                Assert(choice.PreviewDecodeCount == 0, choice.Id + " preview starts un-decoded");

            MainWindow window = new MainWindow(service, true);
            window.Show();
            window.UpdateLayout();
            window.SavePreview(Path.Combine(directory, "preview.png"));

            for (int i = 0; i < IconStore.BuiltIn.Length; ++i)
            {
                if (i == 7) Assert(IconStore.BuiltIn[i].PreviewDecodeCount == 0, "DoubaoWork preview stays un-decoded before switching");
                else Assert(IconStore.BuiltIn[i].PreviewDecodeCount == 1, IconStore.BuiltIn[i].Id + " decoded once when constructing cards");
            }

            MainWindow.RoleCard doubaoCard = window.RoleCards[6];
            Click(doubaoCard.SkinButtons[1]);
            Assert(IconStore.BuiltIn[7].PreviewDecodeCount == 1, "switching to DoubaoWork decodes preview once");

            for (int i = 0; i < window.Rows.Count; ++i) window.Rows[i].Checked = i == 0;
            window.Rows[0].Choice = IconStore.BuiltIn[0];
            Click(doubaoCard.SkinButtons[1]);
            Assert(window.Rows[0].Choice == IconStore.BuiltIn[7], "re-clicking active DoubaoWork pill applies skin to newly checked row");
            Assert(IconStore.BuiltIn[7].PreviewDecodeCount == 1, "re-clicking active pill does not re-decode preview");

            Click(doubaoCard.SkinButtons[0]);
            Assert(IconStore.BuiltIn[6].PreviewDecodeCount == 1, "switching back to DoubaoClassic reuses frozen cached preview");
            Assert(window.Rows.Count == 11 && window.ActualWidth > 0 && window.ActualHeight > 0 &&
                window.ActualWidth <= window.MaxWidth + 1 && window.ActualHeight <= window.MaxHeight + 1,
                "eleven-icon WPF preview renders within host work area");
            window.Close();
        }
        private static void TestMatchKnownCases(ShortcutService service)
        {
            Assert(IconStore.Suggest("glmnet") == null, "Suggest rejects glmnet");
            Assert(IconStore.Suggest("海螺水泥") == null && IconStore.Suggest("海螺 - 水泥报价") == null, "Suggest rejects 海螺水泥 and 海螺 - 水泥报价");
            Assert(IconStore.Suggest("元宝记账") == null && IconStore.Suggest("元宝 - 记账") == null, "Suggest rejects 元宝记账 and 元宝 - 记账");
            Assert(IconStore.Suggest("混元太极") == null && IconStore.Suggest("混元 太极") == null, "Suggest rejects 混元太极 and 混元 太极");
            Assert(IconStore.Suggest("红豆包食谱") == null, "Suggest rejects 红豆包食谱");
            Assert(IconStore.Suggest("Gemini Notebook") == null, "Suggest rejects Gemini Notebook");
            Assert(IconStore.Suggest("ChatGPT - Claude") == null, "Suggest returns null when explicit products conflict");
            Assert(IconStore.Suggest("Kimi - GLM") == IconStore.BuiltIn[10], "Suggest prefers explicit Kimi over GLM alias");
            Assert(IconStore.Suggest("GLM") == IconStore.BuiltIn[5] && IconStore.Suggest("ChatGLM") == IconStore.BuiltIn[5] && IconStore.Suggest("Z.ai") == IconStore.BuiltIn[5], "Suggest matches bounded ZCode aliases including ChatGLM");
            Assert(IconStore.Suggest("海螺AI") == IconStore.BuiltIn[8] && IconStore.Suggest("海螺") == IconStore.BuiltIn[8], "Suggest matches Hailuo product and bare name");
            Assert(IconStore.Suggest("腾讯元宝") == IconStore.BuiltIn[9] && IconStore.Suggest("腾讯混元") == IconStore.BuiltIn[9] && IconStore.Suggest("元宝") == IconStore.BuiltIn[9], "Suggest matches Yuanbao and Hunyuan product names");
            Assert(IconStore.Suggest("Doubao-Work") == IconStore.BuiltIn[7] && IconStore.Suggest("Doubao Work") == IconStore.BuiltIn[7] && IconStore.Suggest("DoubaoWork") == IconStore.BuiltIn[7] &&
                IconStore.Suggest("Doubao 工作台") == IconStore.BuiltIn[7] && IconStore.Suggest("豆包 Work") == IconStore.BuiltIn[7] && IconStore.Suggest("豆包（工作版）") == IconStore.BuiltIn[7] &&
                IconStore.Suggest("豆包 工作台") == IconStore.BuiltIn[7], "Suggest matches all Doubao work skin variants");
            Assert(IconStore.Suggest("Doubao") == IconStore.BuiltIn[6] && IconStore.Suggest("豆包") == IconStore.BuiltIn[6], "Suggest keeps Doubao classic default");

            Dictionary<string, IconChoice> cases = new Dictionary<string, IconChoice>(StringComparer.Ordinal);
            cases["glmnet"] = null;
            cases["海螺水泥"] = null;
            cases["海螺 - 水泥报价"] = null;
            cases["元宝记账"] = null;
            cases["元宝 - 记账"] = null;
            cases["混元太极"] = null;
            cases["混元 太极"] = null;
            cases["红豆包食谱"] = null;
            cases["Gemini Notebook"] = null;
            cases["ChatGPT - Claude"] = null;
            cases["豆包（工作版） - Claude"] = null;
            cases["Kimi + 豆包"] = null;
            cases["海螺AI + GLM"] = null;
            cases["腾讯元宝 - Moonshot"] = null;
            cases["豆包 Work + GLM"] = IconStore.BuiltIn[7];
            cases["GLM2net"] = null;
            cases["Work profile - Doubao"] = IconStore.BuiltIn[6];
            cases["Kimi - GLM"] = IconStore.BuiltIn[10];
            cases["GLM"] = IconStore.BuiltIn[5];
            cases["ChatGLM"] = IconStore.BuiltIn[5];
            cases["海螺AI"] = IconStore.BuiltIn[8];
            cases["腾讯元宝"] = IconStore.BuiltIn[9];
            cases["Doubao-Work"] = IconStore.BuiltIn[7];
            cases["Doubao Work"] = IconStore.BuiltIn[7];
            cases["DoubaoWork"] = IconStore.BuiltIn[7];
            cases["Doubao 工作台"] = IconStore.BuiltIn[7];
            cases["豆包 Work"] = IconStore.BuiltIn[7];
            cases["豆包（工作版）"] = IconStore.BuiltIn[7];
            cases["豆包 工作台"] = IconStore.BuiltIn[7];
            cases["Doubao"] = IconStore.BuiltIn[6];
            cases["豆包"] = IconStore.BuiltIn[6];

            string matchDir = Path.Combine(directory, "match-cases");
            Directory.CreateDirectory(matchDir);
            List<string> files = new List<string>();
            foreach (string name in cases.Keys) files.Add(Fixture(name, matchDir));

            MainWindow window = new MainWindow(service, false, null, false, new Rect(0, 0, 1060, 860));
            window.AddFiles(files, "隔离测试");
            window.Rows.First(r => r.Name == "glmnet").Checked = true;
            window.Rows.First(r => r.Name == "红豆包食谱").Checked = true;
            window.Rows.First(r => r.Name == "Kimi - GLM").Choice = IconStore.BuiltIn[0];
            Click(window.MatchKnownButton);
            int expectedMatches = 0;
            foreach (KeyValuePair<string, IconChoice> entry in cases)
            {
                ShortcutRow row = window.Rows.First(r => r.Name == entry.Key);
                if (entry.Value == null) Assert(!row.Checked, "MatchKnown leaves " + entry.Key + " unchecked");
                else
                {
                    ++expectedMatches;
                    Assert(row.Checked && row.Choice == entry.Value, "MatchKnown selects " + entry.Value.Id + " for " + entry.Key);
                }
            }
            Assert(window.StatusText.Contains("按名称匹配了 " + expectedMatches + " 项"), "MatchKnown footer reports exact matched count");
            window.Close();
        }
        private static void TestCardInteractionAndStatusSync(ShortcutService service)
        {
            string fileA = Fixture("sync-A");
            string fileB = Fixture("sync-B");
            MainWindow window = new MainWindow(service, false, null, false, new Rect(0, 0, 1060, 860));
            window.AddFiles(new[] { fileA, fileB }, "隔离测试");
            window.Show();
            window.UpdateLayout();

            ShortcutRow rowA = window.Rows[0];
            ShortcutRow rowB = window.Rows[1];
            rowA.Checked = true;
            rowB.Checked = false;
            rowB.Choice = IconStore.BuiltIn[4];

            window.ShortcutGrid.ScrollIntoView(rowA);
            window.ShortcutGrid.UpdateLayout();
            DataGridRow gridRowA = (DataGridRow)window.ShortcutGrid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(gridRowA != null, "real DataGridRow container generated for row A");
            gridRowA.ApplyTemplate();
            gridRowA.UpdateLayout();
            ComboBox comboA = FindVisualChild<ComboBox>(gridRowA);
            Assert(comboA != null, "real ComboBox generated in DataGrid cell");

            MainWindow.RoleCard doubaoCard = window.RoleCards[6];
            Click(doubaoCard.SkinButtons[1]);
            Assert(rowA.Choice == IconStore.BuiltIn[7] && rowB.Choice == IconStore.BuiltIn[4] && Equals(comboA.SelectedItem, IconStore.BuiltIn[7]), "clicking work pill updates only checked row and real ComboBox binding");
            Assert(window.StatusText.Contains("豆包工作娘"), "status shows work skin after clicking work pill");

            comboA.SelectedItem = IconStore.BuiltIn[6];
            window.UpdateLayout();
            Assert(rowA.Choice == IconStore.BuiltIn[6] && rowB.Choice == IconStore.BuiltIn[4], "real ComboBox dropdown override changes only that row");
            Assert(window.StatusText.Contains("豆包经典娘") && !window.StatusText.Contains("豆包工作娘"), "status updates after real ComboBox override without mentioning work skin");

            comboA.SelectedIndex = -1;
            window.UpdateLayout();
            Assert(rowA.Choice == null, "clearing ComboBox sets Choice to null safely");
            rowA.Checked = false;
            rowA.Checked = true;
            Assert(window.StatusText.Contains("尚未选择"), "toggling check with null Choice handles empty selection without exception");
            Click(window.ApplyButton);
            Assert(window.StatusText.Contains("尚未选择"), "clicking Apply with null Choice handles empty selection safely");
            comboA.SelectedItem = IconStore.BuiltIn[6];
            window.UpdateLayout();
            Assert(rowA.Choice == IconStore.BuiltIn[6], "re-selecting ComboBox item restores valid Choice");

            rowB.Checked = true;
            Assert(rowA.Choice == IconStore.BuiltIn[6] && rowB.Choice == IconStore.BuiltIn[4], "multiple checked rows can hold distinct icons");

            IconChoice custom = window.ImportCustomIconFile(Path.Combine(directory, "original.ico"));
            Color defaultBorder = (Color)ColorConverter.ConvertFromString("#E4E0EF");
            Color activeBorder = (Color)ColorConverter.ConvertFromString("#8770D6");
            foreach (MainWindow.RoleCard card in window.RoleCards)
            {
                Color c = ((SolidColorBrush)card.Border.BorderBrush).Color;
                Assert(c == defaultBorder, "custom ICO clears highlight on card " + card.Current.Id);
            }
            Assert(doubaoCard.ActiveSkinIndex == 1, "custom ICO preserves last selected Doubao skin");

            rowB.Checked = false;
            Click(doubaoCard.MainButton);
            Assert(((SolidColorBrush)doubaoCard.Border.BorderBrush).Color == activeBorder && doubaoCard.ActiveSkinIndex == 1, "re-clicking Doubao card highlights and restores last skin");
            Assert(rowA.Choice == IconStore.BuiltIn[7] && rowB.Choice == custom, "re-clicking Doubao card updates checked row and leaves unchecked custom row unchanged");

            rowB.Checked = true;
            Click(doubaoCard.SkinButtons[1]);
            Assert(rowB.Choice == IconStore.BuiltIn[7] && IconStore.BuiltIn[7].PreviewDecodeCount == 1, "re-clicking active work pill updates newly checked row without re-decoding");

            ShortcutRow detached = rowA;
            window.Rows.Clear();
            string statusAfterClear = window.StatusText;
            detached.Checked = false;
            detached.Choice = IconStore.BuiltIn[0];
            Assert(window.StatusText == statusAfterClear, "Rows.Clear unsubscribes removed rows so old rows do not change status");
            window.Close();
        }
        private static void TestWindowBoundsDragAndJitter(ShortcutService service)
        {
            Rect leftDisplay = new Rect(-1920, 60, 1920, 1020);
            Rect crossing = new Rect(-360, 90, 1200, 840);
            Assert(MainWindow.ReachableOrigin(crossing, leftDisplay, 1.5, 1.5) == crossing.Location,
                "visible title strip across a negative-origin 150 percent display stays in place");
            Rect rightDisplay = new Rect(1920, 0, 2560, 1400);
            Assert(MainWindow.ReachableOrigin(new Rect(4800, -300, 1600, 1000), rightDisplay, 2, 2) == new Point(2880, 0),
                "off-screen window returns in physical pixels on a 200 percent display");

            MainWindow window = new MainWindow(service, true, null, false, new Rect(0, 0, 1520, 900));
            window.Width = 880;
            window.Show();
            window.UpdateLayout();

            double[] dragTargets = { 670, 700, 790 };
            foreach (double target in dragTargets)
            {
                window.Left = target;
                window.UpdateLayout();
                Assert(Math.Abs(window.Left - target) < 1.0, "dragging window across work-area edge is not snapped back at Left=" + target);
            }
            window.FinishMoveOrDisplayChange();
            window.UpdateLayout();
            Assert(Math.Abs(window.Left - 790) < 1.0, "settling partially visible window straddling boundary preserves Left=790");

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            SendMessageW(hwnd, 0x0231, IntPtr.Zero, IntPtr.Zero); // enter interactive move
            window.Left = 1800;
            window.UpdateLayout();
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            Assert(Math.Abs(window.Left - 1800) < 1.0, "interactive move never pulls the preview back mid-drag");
            SendMessageW(hwnd, 0x0232, IntPtr.Zero, IntPtr.Zero); // exit and settle
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            window.UpdateLayout();
            Assert(window.Left + window.ActualWidth <= 1520.5 && window.Left >= -0.5, "truly off-screen window is pulled back inside work area after settle");

            window.Width = 1000;
            window.Height = 750;
            window.Left = 100;
            window.Top = 40;
            window.UpdateLayout();
            double baseMarginTop = ((FrameworkElement)window.RootPanel).Margin.Top;
            double baseGridTop = BoundsInAncestor(window.ShortcutGrid, window).Top;
            for (int step = 0; step < 5; ++step)
            {
                window.Left += 4;
                window.UpdateLayout();
                window.RefreshLayoutForTest();
                window.UpdateLayout();
                Assert(Math.Abs(((FrameworkElement)window.RootPanel).Margin.Top - baseMarginTop) < 0.1 &&
                    Math.Abs(BoundsInAncestor(window.ShortcutGrid, window).Top - baseGridTop) < 0.5,
                    "horizontal move at Height=750 causes zero layout jitter step=" + step);
            }
            window.Close();

            MainWindow negWin = new MainWindow(service, true, null, false, new Rect(-1280, 40, 880, 560));
            negWin.Show();
            negWin.UpdateLayout();
            Assert(negWin.Left >= -1280.5 && negWin.Left + negWin.ActualWidth <= -399.5 && negWin.Top >= 39.5 && negWin.Top + negWin.ActualHeight <= 600.5,
                "negative-origin simulated work area places window within [-1280, -400]x[40, 600]");
            Rect negRect = new Rect(0, 0, negWin.ActualWidth, negWin.ActualHeight);
            Assert(InsideBounds(BoundsInAncestor(negWin.ApplyButton, negWin), negRect) &&
                InsideBounds(BoundsInAncestor(negWin.RestoreButton, negWin), negRect),
                "action buttons visible on negative-origin monitor");
            negWin.Left = -600;
            negWin.UpdateLayout();
            negWin.FinishMoveOrDisplayChange();
            Assert(Math.Abs(negWin.Left - (-600)) < 1.0, "window straddling negative-origin monitor edge stays at Left=-600");
            negWin.Close();
        }
        private static void AssertActionButtonsHitTestable(MainWindow window, string label)
        {
            Rect winRect = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            Rect rootRect = BoundsInAncestor((FrameworkElement)window.RootPanel, window);
            Button[] actionButtons = { window.ApplyButton, window.RestoreButton };
            foreach (Button btn in actionButtons)
            {
                Rect rect = BoundsInAncestor(btn, window);
                Assert(btn.IsVisible && btn.IsEnabled && InsideBounds(rect, winRect) && InsideBounds(rect, rootRect), "action button " + btn.Content + " visible inside " + label);
                DependencyObject hit = HitTestAt(window, btn, new Point(btn.ActualWidth / 2, btn.ActualHeight / 2));
                Assert(HasVisualAncestor(hit, btn), "action button " + btn.Content + " hit-testable inside " + label);
            }
        }
        private static void AssertDoubaoSkinPillsClearAndIndependent(MainWindow window, string label)
        {
            MainWindow.RoleCard doubaoCard = window.RoleCards[6];
            for (int skin = 0; skin < doubaoCard.Skins.Length; ++skin)
            {
                Click(doubaoCard.SkinButtons[skin]);
                doubaoCard.Border.BringIntoView();
                window.UpdateLayout();
                Rect imageRect = BoundsInAncestor(doubaoCard.PreviewImage, doubaoCard.Border);
                for (int s = 0; s < doubaoCard.SkinButtons.Length; ++s)
                {
                    Button pill = doubaoCard.SkinButtons[s];
                    Rect pillRect = BoundsInAncestor(pill, doubaoCard.Border);
                    Assert(pillRect.Left > imageRect.Right, label + " skin=" + skin + " pill " + s + " does not overlap avatar image");
                    DependencyObject pillHit = HitTestAt(window, pill, new Point(pill.ActualWidth / 2, pill.ActualHeight / 2));
                    Assert(HasVisualAncestor(pillHit, pill) && !HasVisualAncestor(pillHit, doubaoCard.MainButton), label + " skin=" + skin + " pill " + s + " hits independently");
                }
            }
            Click(doubaoCard.SkinButtons[0]);
        }
        private static void TestSmallViewportAndHitTesting(ShortcutService service)
        {
            MainWindow window = new MainWindow(service, true, null, false, new Rect(0, 0, 880, 560));
            window.Show();
            window.UpdateLayout();

            Assert(window.ActualWidth <= 880.5 && window.ActualHeight <= 560.5 && window.MinHeight <= 560, "simulated 880x560 DIP work area constrains window height");
            Assert(window.CardGrid.Columns == 5 && window.CardScroller.ScrollableHeight > 0, "cards scroll cleanly in 5 columns on 880x560 DIP");
            Assert(window.ShortcutGrid.ActualHeight >= 100, "shortcut DataGrid retains usable height on 880x560 DIP");
            window.SavePreview(Path.Combine(directory, "preview-880x560.png"));
            AssertActionButtonsHitTestable(window, "880x560 DIP");

            window.CardScroller.ScrollToTop();
            window.UpdateLayout();
            MainWindow.RoleCard firstCard = window.RoleCards[0];
            DependencyObject firstHit = HitTestAt(window, firstCard.Border, new Point(10, 36));
            Assert(HasVisualAncestor(firstHit, firstCard.MainButton), "left whitespace on card 0 hits card button at 880 DIP width");

            MainWindow.RoleCard doubaoCard = window.RoleCards[6];
            doubaoCard.Border.BringIntoView();
            window.UpdateLayout();
            DependencyObject doubaoBlankHit = HitTestAt(window, doubaoCard.Border, new Point(10, 36));
            Assert(HasVisualAncestor(doubaoBlankHit, doubaoCard.MainButton) &&
                !HasVisualAncestor(doubaoBlankHit, doubaoCard.SkinButtons[0]) &&
                !HasVisualAncestor(doubaoBlankHit, doubaoCard.SkinButtons[1]),
                "left whitespace on Doubao card hits main card button at 880 DIP width");
            AssertDoubaoSkinPillsClearAndIndependent(window, "880x560");

            double gridBeforeCollapse = window.ShortcutGrid.ActualHeight;
            Click(window.ToggleCardsButton);
            window.UpdateLayout();
            Assert(window.CardScroller.Visibility == Visibility.Collapsed && window.ShortcutGrid.ActualHeight > gridBeforeCollapse, "collapse button expands DataGrid space on short viewport");
            Click(window.ToggleCardsButton);
            window.UpdateLayout();
            Assert(window.CardScroller.Visibility == Visibility.Visible, "expand button restores card area");
            window.Close();

            MainWindow w720 = new MainWindow(service, true, null, false, new Rect(0, 0, 720, 560));
            w720.Show();
            w720.UpdateLayout();
            Assert(w720.ActualWidth <= 720.5 && w720.CardGrid.Columns == 4 && w720.ShortcutGrid.ActualHeight >= 100, "720x560 adapts to 4 columns with usable list");
            AssertActionButtonsHitTestable(w720, "720x560 DIP");
            AssertDoubaoSkinPillsClearAndIndependent(w720, "720x560");
            w720.Close();

            MainWindow w640 = new MainWindow(service, true, null, false, new Rect(0, 0, 640, 560));
            w640.Show();
            w640.UpdateLayout();
            Assert(w640.ActualWidth <= 640.5 && w640.CardGrid.Columns == 3 && w640.ShortcutGrid.ActualHeight >= 100, "640x560 adapts to 3 columns with usable list");
            AssertActionButtonsHitTestable(w640, "640x560 DIP");
            AssertDoubaoSkinPillsClearAndIndependent(w640, "640x560");
            w640.SavePreview(Path.Combine(directory, "preview-640x560.png"));
            w640.Close();

            MainWindow wShort = new MainWindow(service, true, null, false, new Rect(0, 0, 640, 360));
            wShort.Show();
            wShort.UpdateLayout();
            Assert(wShort.ActualWidth <= 640.5 && wShort.ActualHeight <= 360.5, "640x360 fits within ultra-short work area");
            Assert(wShort.CardScroller.Visibility == Visibility.Collapsed && wShort.ShortcutGrid.ActualHeight >= 78, "640x360 auto-collapses cards to preserve operable shortcut list");
            AssertActionButtonsHitTestable(wShort, "640x360 DIP");
            wShort.ShortcutGrid.ScrollIntoView(wShort.Rows[0]);
            wShort.ShortcutGrid.UpdateLayout();
            DataGridRow firstRow = (DataGridRow)wShort.ShortcutGrid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(firstRow != null, "640x360 renders at least one shortcut row");
            CheckBox rowCheck = FindVisualChild<CheckBox>(firstRow);
            Rect shortWinRect = new Rect(0, 0, wShort.ActualWidth, wShort.ActualHeight);
            Assert(rowCheck != null && InsideBounds(BoundsInAncestor(rowCheck, wShort), shortWinRect) &&
                HasVisualAncestor(HitTestAt(wShort, rowCheck, new Point(rowCheck.ActualWidth / 2, rowCheck.ActualHeight / 2)), rowCheck),
                "640x360 keeps first shortcut row checkbox visible and hit-testable");
            wShort.SavePreview(Path.Combine(directory, "preview-640x360.png"));
            wShort.Close();
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
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Assert(IconStore.BuiltIn.Length == 11, "eleven built-in icons registered");
                TestPreviewLazyDecode(service);
                foreach (IconChoice choice in IconStore.BuiltIn)
                {
                    IconStore.Validate(choice.Bytes());
                    Assert(choice.Preview() != null && ReferenceEquals(choice.Preview(), choice.Preview()), choice.Id + " embedded ICO and cached preview decode");
                    Assert(choice.PreviewDecodeCount == 1, choice.Id + " built-in preview decodes at most once");
                    int sep = choice.Subtitle.IndexOf(" · ");
                    Assert(sep == 4 && choice.Subtitle.Length > sep + 3, choice.Id + " subtitle uses 4-char trait + separator");
                    string shellFile = Fixture("shell-" + choice.Id);
                    service.Apply(shellFile, choice.Bytes());
                    Assert(SimilarShellIcon(shellFile, NativeLink.Read(shellFile).IconPath), choice.Id + " Windows Shell resolves the actual new shortcut icon");
                }
                Assert(IconStore.BuiltIn[6].Group == "Doubao" && IconStore.BuiltIn[7].Group == "Doubao" &&
                    !string.IsNullOrEmpty(IconStore.BuiltIn[6].SkinLabel) && !string.IsNullOrEmpty(IconStore.BuiltIn[7].SkinLabel),
                    "Doubao classic and work skins share card group");

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
                Assert(IconStore.Suggest("ZCode") == IconStore.BuiltIn[5] && IconStore.Suggest("Z-Code") == IconStore.BuiltIn[5] && IconStore.Suggest("GLM") == IconStore.BuiltIn[5] && IconStore.Suggest("智谱") == IconStore.BuiltIn[5], "name suggestions match ZCode");
                Assert(IconStore.Suggest("豆包") == IconStore.BuiltIn[6] && IconStore.Suggest("Doubao") == IconStore.BuiltIn[6], "name suggestions match Doubao classic");
                Assert(IconStore.Suggest("豆包工作") == IconStore.BuiltIn[7] && IconStore.Suggest("豆包工作台") == IconStore.BuiltIn[7] && IconStore.Suggest("DoubaoWork") == IconStore.BuiltIn[7], "name suggestions match DoubaoWork");
                Assert(IconStore.Suggest("MiniMax") == IconStore.BuiltIn[8] && IconStore.Suggest("MiniMax Agent") == IconStore.BuiltIn[8] && IconStore.Suggest("海螺AI") == IconStore.BuiltIn[8], "name suggestions match MiniMax");
                Assert(IconStore.Suggest("WorkBuddy") == IconStore.BuiltIn[9] && IconStore.Suggest("Work Buddy") == IconStore.BuiltIn[9] && IconStore.Suggest("腾讯混元") == IconStore.BuiltIn[9] && IconStore.Suggest("Hunyuan") == IconStore.BuiltIn[9] && IconStore.Suggest("腾讯元宝") == IconStore.BuiltIn[9], "name suggestions match WorkBuddy Hunyuan");
                Assert(IconStore.Suggest("Kimi") == IconStore.BuiltIn[10] && IconStore.Suggest("Moonshot") == IconStore.BuiltIn[10] && IconStore.Suggest("月之暗面") == IconStore.BuiltIn[10], "name suggestions match Kimi");

                TestMatchKnownCases(service);
                TestCardInteractionAndStatusSync(service);
                TestWindowBoundsDragAndJitter(service);
                TestSmallViewportAndHitTesting(service);

                Assert(!Directory.GetFiles(directory, ".mascot-*.lnk").Any(), "staged shortcuts cleaned after successes and failures");
                Assert(!File.Exists(Path.Combine(directory, "nonexistent harmless target.exe")), "test targets remain nonexistent and were never invoked");

                string report = "PASS: " + count + " checks. Test fixtures only; no AI application launched.\nArtifacts: " + directory;
                File.WriteAllText(Path.Combine(directory, "result.txt"), report);
                File.WriteAllText(Path.Combine(Path.GetFullPath(args[0]), "latest.txt"), directory);
                Console.WriteLine(report);
                app.Shutdown(0);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
