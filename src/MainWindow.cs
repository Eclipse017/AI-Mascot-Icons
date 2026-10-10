using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MascotIcons
{
    internal sealed class ShortcutRow : INotifyPropertyChanged
    {
        private bool enabled;
        private IconChoice choice;
        private string state;
        public string Path { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public bool Checked { get { return enabled; } set { enabled = value; Changed("Checked"); } }
        public IconChoice Choice { get { return choice; } set { choice = value; Changed("Choice"); } }
        public string State { get { return state; } set { state = value; Changed("State"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class MainWindow : Window
    {
        internal readonly ObservableCollection<ShortcutRow> Rows = new ObservableCollection<ShortcutRow>();
        private readonly ObservableCollection<IconChoice> choices = new ObservableCollection<IconChoice>(IconStore.BuiltIn);
        private readonly ShortcutService service;
        private readonly DataGrid grid;
        private readonly TextBlock status;
        private readonly bool preview;
        private IconChoice selected = IconStore.BuiltIn[0];
        private readonly List<Border> cards = new List<Border>();
        internal string LastReport;
        private static SolidColorBrush Color(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        private static TextBlock Text(string text, double size, string color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = Color(color), TextWrapping = TextWrapping.Wrap }; }

        internal MainWindow(ShortcutService service, bool preview, string fixtureFile = null)
        {
            this.service = service; this.preview = preview;
            Title = "AI 娘图标换装 · v1.0.0";
            Width = 1040; Height = 830; MinWidth = 860; MinHeight = 700;
            MaxHeight = SystemParameters.WorkArea.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            Background = Color("#F6F5FB"); Foreground = Color("#26243C");
            Icon = IconStore.BuiltIn[0].Preview();
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) AddFiles((string[])e.Data.GetData(DataFormats.FileDrop), "手动添加");
                e.Handled = true;
            };

            DockPanel root = new DockPanel { Margin = new Thickness(28, 22, 28, 20), LastChildFill = true };
            Content = root;
            StackPanel header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            DockPanel titleRow = new DockPanel();
            Border badge = new Border { Background = Color("#E8F5EE"), CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 6, 12, 6), VerticalAlignment = VerticalAlignment.Center, Child = Text("离线使用 · 免费工具", 12, "#397454") };
            DockPanel.SetDock(badge, Dock.Right); titleRow.Children.Add(badge);
            TextBlock title = Text("让桌面上的 AI，变成喜欢的样子", 25, "#26243C"); title.FontWeight = FontWeights.Bold;
            titleRow.Children.Add(title); header.Children.Add(titleRow);
            TextBlock tagline = Text("勾选快捷方式，选一位 AI 娘，再点应用。每次换装都可以恢复。", 13, "#716B87");
            tagline.Margin = new Thickness(0, 9, 0, 17); header.Children.Add(tagline);

            UniformGrid cardGrid = new UniformGrid { Columns = 5, Margin = new Thickness(-5, 0, -5, 14) };
            foreach (IconChoice choice in IconStore.BuiltIn)
            {
                IconChoice captured = choice;
                StackPanel body = new StackPanel { Margin = new Thickness(8, 9, 8, 10) };
                body.Children.Add(new Image { Source = choice.Preview(), Width = 116, Height = 116, Stretch = Stretch.Uniform });
                TextBlock label = Text(choice.Name, 14, "#343048"); label.FontWeight = FontWeights.SemiBold; label.TextAlignment = TextAlignment.Center; body.Children.Add(label);
                TextBlock subtitle = Text(choice.Subtitle, 10.5, "#807A92"); subtitle.Margin = new Thickness(0, 4, 0, 0); subtitle.TextAlignment = TextAlignment.Center; body.Children.Add(subtitle);
                Border border = new Border { Background = Brushes.White, BorderBrush = Color("#E4E0EF"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Child = body, Margin = new Thickness(5, 0, 5, 0) };
                Button button = new Button { Content = border, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "将勾选的快捷方式设为 " + choice.Name };
                ControlTemplate template = new ControlTemplate(typeof(Button));
                template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); button.Template = template;
                button.Click += delegate { SelectChoice(captured); };
                cards.Add(border); cardGrid.Children.Add(button);
            }
            header.Children.Add(cardGrid);
            cards[0].BorderBrush = Color("#8770D6"); cards[0].Background = Color("#F1ECFF");

            WrapPanel toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 11) };
            toolbar.Children.Add(ActionButton("扫描桌面", delegate { Scan(false); }, false));
            toolbar.Children.Add(ActionButton("添加快捷方式…", AddDialog, false));
            toolbar.Children.Add(ActionButton("添加开始菜单", delegate { Scan(true); }, false));
            toolbar.Children.Add(ActionButton("自定义 ICO…", ImportIcon, false));
            toolbar.Children.Add(ActionButton("匹配常见 AI", MatchKnown, false));
            header.Children.Add(toolbar);

            StackPanel footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            status = Text("", 12, "#716B87"); status.Margin = new Thickness(0, 10, 0, 10); footer.Children.Add(status);
            DockPanel actions = new DockPanel();
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(ActionButton("恢复勾选项", delegate { RunOperation(true); }, false));
            right.Children.Add(ActionButton("应用 AI 娘图标", delegate { RunOperation(false); }, true));
            DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
            Button help = ActionButton("使用说明", ShowHelp, false); help.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(help); footer.Children.Add(actions);
            TextBlock note = Text("支持 Windows .lnk 快捷方式。应用后的图标与备份会保存在本机；关闭窗口即退出。", 11, "#928BA3");
            note.Margin = new Thickness(0, 10, 0, 0); footer.Children.Add(note);

            grid = new DataGrid { ItemsSource = Rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false, RowHeaderWidth = 0, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Color("#F0EDF7"), BorderBrush = Color("#E4E0EF"), BorderThickness = new Thickness(1), Background = Brushes.White, RowHeight = 42, ColumnHeaderHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, AlternatingRowBackground = Color("#FCFBFF"), IsReadOnly = false };
            DataTemplate checkTemplate = new DataTemplate();
            FrameworkElementFactory checkbox = new FrameworkElementFactory(typeof(CheckBox));
            checkbox.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Checked") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkbox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); checkTemplate.VisualTree = checkbox;
            grid.Columns.Add(new DataGridTemplateColumn { Header = "选择", Width = new DataGridLength(52), CellTemplate = checkTemplate });
            grid.Columns.Add(new DataGridTextColumn { Header = "快捷方式", Binding = new Binding("Name"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160 });
            grid.Columns.Add(new DataGridTextColumn { Header = "位置", Binding = new Binding("Location"), IsReadOnly = true, Width = 112 });
            DataTemplate comboTemplate = new DataTemplate();
            FrameworkElementFactory combo = new FrameworkElementFactory(typeof(ComboBox));
            combo.SetValue(ItemsControl.ItemsSourceProperty, choices);
            combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Name");
            combo.SetBinding(Selector.SelectedItemProperty, new Binding("Choice") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            combo.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 5, 8, 5));
            comboTemplate.VisualTree = combo;
            grid.Columns.Add(new DataGridTemplateColumn { Header = "准备替换为", CellTemplate = comboTemplate, Width = 190 });
            grid.Columns.Add(new DataGridTextColumn { Header = "备份状态", Binding = new Binding("State"), IsReadOnly = true, Width = 105 });
            Style rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("Path"))); grid.RowStyle = rowStyle;
            root.Children.Add(grid);
            if (preview)
            {
                string[] names = { "ChatGPT", "Claude", "Antigravity", "Grok Bot", "DeepSeek" };
                for (int i = 0; i < names.Length; ++i) Rows.Add(new ShortcutRow { Name = names[i], Path = "预览示例 / " + names[i] + ".lnk", Location = "桌面 · 示例", Choice = IconStore.BuiltIn[i], Checked = true, State = "示例" });
                status.Text = "界面预览 · 这里是示例快捷方式，不会修改你的桌面。";
            }
            else if (fixtureFile != null) AddFiles(new[] { fixtureFile }, "隔离测试");
            else Scan(false);
        }

        private Button ActionButton(string caption, Action action, bool primary)
        {
            Button button = new Button { Content = caption, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 8, 0), Background = Color(primary ? "#7861BD" : "#FFFFFF"), Foreground = primary ? Brushes.White : Color("#534B6D"), BorderBrush = Color(primary ? "#7861BD" : "#DED9EC"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 37 };
            button.Click += delegate
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            return button;
        }
        private void SelectChoice(IconChoice choice)
        {
            selected = choice;
            int count = 0;
            foreach (ShortcutRow row in Rows.Where(r => r.Checked)) { row.Choice = choice; ++count; }
            for (int i = 0; i < cards.Count; ++i)
            {
                bool active = IconStore.BuiltIn[i] == choice;
                cards[i].BorderBrush = Color(active ? "#8770D6" : "#E4E0EF");
                cards[i].Background = Color(active ? "#F1ECFF" : "#FFFFFF");
            }
            status.Text = count == 0 ? "已选择 " + choice.Name + "。先勾选快捷方式，再点角色卡片即可设置。" : "已为 " + count + " 个勾选项选择 " + choice.Name + "，点“应用”后生效。";
        }
        internal void AddFiles(IEnumerable<string> files, string location)
        {
            List<string> failures = new List<string>();
            foreach (string file in files)
            {
                try
                {
                    string path = ShortcutService.Normalize(file);
                    if (Rows.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                    NativeLink.Read(path); // Validate without resolving or launching the target.
                    string name = Path.GetFileNameWithoutExtension(path);
                    Rows.Add(new ShortcutRow { Path = path, Name = name, Location = location, Choice = IconStore.Suggest(name) ?? selected, Checked = false, State = service.State(path) });
                }
                catch (Exception ex) { failures.Add(Path.GetFileName(file) + "：" + ex.Message); }
            }
            status.Text = "已找到 " + Rows.Count + " 个快捷方式。勾选需要修改的项目；角色名称只是建议，可以手动更换。";
            if (failures.Count > 0) { status.Text += " 已跳过 " + failures.Count + " 项。"; LastReport = string.Join(Environment.NewLine, failures); status.ToolTip = LastReport; }
        }
        private void Scan(bool startMenu)
        {
            if (preview) return;
            List<Tuple<string, string>> roots = new List<Tuple<string, string>>();
            if (startMenu)
            {
                roots.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "开始菜单"));
                roots.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "公共开始菜单"));
            }
            else
            {
                // Keep manual additions and choices when refreshing.
                roots.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面"));
                roots.Add(Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "公共桌面"));
            }
            foreach (Tuple<string, string> item in roots)
                if (!string.IsNullOrEmpty(item.Item1) && Directory.Exists(item.Item1)) AddFiles(EnumerateLinks(item.Item1, startMenu, 0), item.Item2);
            foreach (ShortcutRow row in Rows) row.State = File.Exists(row.Path) ? service.State(row.Path) : "已移动或删除";
            if (Rows.Count == 0) status.Text = "没有找到桌面快捷方式。可以点“添加快捷方式”，或把 .lnk 文件拖进窗口。";
        }
        private static IEnumerable<string> EnumerateLinks(string directory, bool recurse, int depth)
        {
            List<string> result = new List<string>();
            if (depth > 12) return result;
            try
            {
                result.AddRange(Directory.GetFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly));
                if (recurse) foreach (string child in Directory.GetDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) result.AddRange(EnumerateLinks(child, true, depth + 1));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return result;
        }
        private void AddDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog { Title = "选择快捷方式（不会打开应用）", Filter = "Windows 快捷方式 (*.lnk)|*.lnk", Multiselect = true, DereferenceLinks = false, CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames, "手动添加");
        }
        private void ImportIcon()
        {
            OpenFileDialog dialog = new OpenFileDialog { Title = "选择自定义图标", Filter = "ICO 图标 (*.ico)|*.ico", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            IconStore.Validate(File.ReadAllBytes(dialog.FileName));
            IconChoice choice = new IconChoice("custom-" + Guid.NewGuid().ToString("N"), "自定义 · " + Path.GetFileNameWithoutExtension(dialog.FileName), "自定义 ICO", dialog.FileName);
            choices.Add(choice); SelectChoice(choice);
        }
        private void MatchKnown()
        {
            int count = 0;
            foreach (ShortcutRow row in Rows)
            {
                IconChoice match = IconStore.Suggest(row.Name);
                row.Checked = match != null;
                if (match != null) { row.Choice = match; ++count; }
            }
            status.Text = "按名称匹配了 " + count + " 项；请检查列表后点“应用”。Gemini Notebook 不自动匹配。";
        }
        private void RunOperation(bool restore)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            List<ShortcutRow> rows = Rows.Where(r => r.Checked).ToList();
            if (rows.Count == 0) { status.Text = "请先勾选至少一个快捷方式。"; return; }
            if (preview) { status.Text = "这是界面预览，所有示例都不会写入。"; return; }
            string details = string.Join(Environment.NewLine, rows.Take(12).Select(r => "• " + r.Name + (restore ? "" : " → " + r.Choice.Name)));
            if (rows.Count > 12) details += Environment.NewLine + "… 共 " + rows.Count + " 项";
            string message = (restore ? "将恢复到本工具第一次修改前的快捷方式。" : "将为以下快捷方式更换图标，并先保存原始备份。") + Environment.NewLine + Environment.NewLine + details;
            if (MessageBox.Show(this, message, restore ? "确认恢复" : "确认换装", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            int success = 0; List<string> failures = new List<string>();
            foreach (ShortcutRow row in rows)
            {
                try
                {
                    if (restore) service.Restore(row.Path); else service.Apply(row.Path, row.Choice.Bytes());
                    row.State = service.State(row.Path); ++success;
                }
                catch (Exception ex) { row.State = "未完成"; failures.Add(row.Name + "：" + FriendlyError(ex)); }
            }
            status.Text = (restore ? "恢复完成 " : "换装完成 ") + success + " 项" + (failures.Count > 0 ? "，跳过 " + failures.Count + " 项。" : "。若桌面还没更新，请在桌面按 F5。");
            LastReport = status.Text + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, failures);
            if (failures.Count > 0) MessageBox.Show(this, LastReport, "操作结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private static string FriendlyError(Exception ex)
        {
            if (ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005))
                return "没有写入权限。请把这个快捷方式复制到你自己的桌面后再添加；无需以管理员身份运行。";
            return ex.Message;
        }
        private void ShowHelp()
        {
            MessageBox.Show(this,
                "① 勾选快捷方式。可扫描桌面、添加开始菜单，或拖入 .lnk。\n② 点击角色卡片，或在每一行选择不同的角色。\n③ 点“应用 AI 娘图标”。不喜欢时，勾选原项目并点“恢复”。\n\n" +
                "也可以点“匹配常见 AI”，核对建议后批量应用。\n自定义图标支持 ICO；应用后原始 ICO 可以移动。\n\n" +
                "仅更换快捷方式图标；不会运行目标应用，也不修改程序文件。\n应用窗口和任务栏正在运行的图标由应用决定，可能仍是原图。\n没有桌面快捷方式时，可先从开始菜单创建一个。\n\n" +
                "图标和原始备份保存在：\n" + service.Root + "\n恢复前请保留这个目录；下载文件夹则可以移动。\n\n" +
                "代码采用 MIT；随附角色素材另见 ASSETS.md。这是非官方爱好项目。",
                "使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        internal void SavePreview(string path)
        {
            UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) encoder.Save(file);
        }
    }
}
