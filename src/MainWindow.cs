using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
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
        internal sealed class RoleCard
        {
            public Border Border;
            public Image PreviewImage;
            public TextBlock TitleText;
            public TextBlock SubtitleText;
            public Button MainButton;
            public IconChoice[] Skins;
            public Button[] SkinButtons;
            public Border[] SkinBorders;
            public TextBlock[] SkinLabels;
            public int ActiveSkinIndex;
            public IconChoice Current { get { return Skins[ActiveSkinIndex]; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        internal readonly ObservableCollection<ShortcutRow> Rows = new ObservableCollection<ShortcutRow>();
        private readonly ObservableCollection<IconChoice> choices = new ObservableCollection<IconChoice>(IconStore.BuiltIn);
        private readonly HashSet<ShortcutRow> trackedRows = new HashSet<ShortcutRow>();
        private readonly ShortcutService service;
        private readonly Grid viewportHost;
        private readonly DockPanel root;
        private readonly DockPanel titleRow;
        private readonly TextBlock titleText;
        private readonly Border badgeBorder;
        private readonly DockPanel taglineRow;
        private readonly TextBlock taglineText;
        private readonly ScrollViewer cardScroll;
        private readonly UniformGrid cardGrid;
        private readonly WrapPanel toolbar;
        private readonly StackPanel footer;
        private readonly DataGrid grid;
        private readonly TextBlock status;
        private readonly TextBlock note;
        private readonly Button toggleCardsButton;
        private readonly Button matchKnownButton;
        private readonly Button restoreButton;
        private readonly Button applyButton;
        private readonly List<Button> chromeButtons = new List<Button>();
        private readonly bool preview;
        private readonly Rect? simulatedWorkArea;
        private Size lastWorkAreaSize;
        private bool applyingBounds;
        private bool inSizeMove;
        private bool boundsUpdateQueued;
        private bool? userCardsCollapsed;
        private bool bulkUpdating;
        private IconChoice selected = IconStore.BuiltIn[0];
        private readonly List<RoleCard> cards = new List<RoleCard>();
        internal string LastReport;

        internal FrameworkElement RootPanel { get { return root; } }
        internal ScrollViewer CardScroller { get { return cardScroll; } }
        internal UniformGrid CardGrid { get { return cardGrid; } }
        internal DataGrid ShortcutGrid { get { return grid; } }
        internal Button ApplyButton { get { return applyButton; } }
        internal Button RestoreButton { get { return restoreButton; } }
        internal Button MatchKnownButton { get { return matchKnownButton; } }
        internal Button ToggleCardsButton { get { return toggleCardsButton; } }
        internal string StatusText { get { return status.Text; } }
        internal RoleCard[] RoleCards { get { return cards.ToArray(); } }

        private static SolidColorBrush Color(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        private static TextBlock Text(string text, double size, string color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = Color(color), TextWrapping = TextWrapping.Wrap }; }

        internal MainWindow(ShortcutService service, bool preview, string fixtureFile = null, bool autoScan = true, Rect? simulatedWorkArea = null)
        {
            this.service = service; this.preview = preview; this.simulatedWorkArea = simulatedWorkArea;
            Title = "AI 娘图标换装 · v1.1.0";
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
            Rows.CollectionChanged += Rows_CollectionChanged;

            viewportHost = new Grid();
            root = new DockPanel { Margin = new Thickness(28, 22, 28, 20), LastChildFill = true };
            viewportHost.Children.Add(root);
            Content = viewportHost;

            // Dock footer first so action buttons remain visible on short or high-DPI viewports.
            footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            status = Text("", 12, "#716B87"); status.Margin = new Thickness(0, 10, 0, 10); footer.Children.Add(status);
            DockPanel actions = new DockPanel();
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
            restoreButton = ActionButton("恢复勾选项", delegate { RunOperation(true); }, false);
            applyButton = ActionButton("应用 AI 娘图标", delegate { RunOperation(false); }, true);
            right.Children.Add(restoreButton);
            right.Children.Add(applyButton);
            DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
            Button help = ActionButton("使用说明", ShowHelp, false); help.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(help); footer.Children.Add(actions);
            note = Text("支持 Windows .lnk 快捷方式。应用后的图标与备份会保存在本机；关闭窗口即退出。", 11, "#928BA3");
            note.Margin = new Thickness(0, 10, 0, 0); footer.Children.Add(note);

            StackPanel header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            titleRow = new DockPanel();
            badgeBorder = new Border { Background = Color("#E8F5EE"), CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 6, 12, 6), VerticalAlignment = VerticalAlignment.Center, Child = Text("离线使用 · 免费工具", 12, "#397454") };
            DockPanel.SetDock(badgeBorder, Dock.Right); titleRow.Children.Add(badgeBorder);
            titleText = Text("让桌面上的 AI，变成喜欢的样子", 25, "#26243C"); titleText.FontWeight = FontWeights.Bold;
            titleRow.Children.Add(titleText); header.Children.Add(titleRow);

            taglineRow = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
            toggleCardsButton = new Button { Content = "收起角色", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), FontSize = 11.5, Background = Brushes.White, Foreground = Color("#534B6D"), BorderBrush = Color("#DED9EC"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            toggleCardsButton.Click += delegate
            {
                bool currentlyCollapsed = cardScroll.Visibility == Visibility.Collapsed;
                userCardsCollapsed = !currentlyCollapsed;
                UpdateResponsiveLayout();
            };
            DockPanel.SetDock(toggleCardsButton, Dock.Right); taglineRow.Children.Add(toggleCardsButton);
            taglineText = Text("勾选快捷方式，选一位 AI 娘，再点应用。每次换装都可以恢复。", 13, "#716B87");
            taglineText.VerticalAlignment = VerticalAlignment.Center;
            taglineRow.Children.Add(taglineText); header.Children.Add(taglineRow);

            cardGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0) };
            List<List<IconChoice>> groups = new List<List<IconChoice>>();
            Dictionary<string, List<IconChoice>> groupedByName = new Dictionary<string, List<IconChoice>>();
            foreach (IconChoice choice in IconStore.BuiltIn)
            {
                if (choice.Group == null)
                {
                    groups.Add(new List<IconChoice> { choice });
                }
                else
                {
                    List<IconChoice> list;
                    if (!groupedByName.TryGetValue(choice.Group, out list))
                    {
                        list = new List<IconChoice>();
                        groupedByName[choice.Group] = list;
                        groups.Add(list);
                    }
                    list.Add(choice);
                }
            }
            foreach (List<IconChoice> group in groups)
            {
                IconChoice[] groupSkins = group.ToArray();
                IconChoice initial = groupSkins[0];
                Image previewImage = new Image { Width = 76, Height = 76, Stretch = Stretch.Uniform };
                TextBlock label = Text(initial.Name, 13, "#343048"); label.FontWeight = FontWeights.SemiBold; label.TextAlignment = TextAlignment.Center;
                TextBlock subtitle = Text(initial.Subtitle, 10, "#807A92"); subtitle.Margin = new Thickness(0, 2, 0, 0); subtitle.TextAlignment = TextAlignment.Center;
                StackPanel body = new StackPanel { Margin = new Thickness(6, 6, 6, 7) };
                body.Children.Add(previewImage);
                body.Children.Add(label);
                body.Children.Add(subtitle);

                FrameworkElementFactory hitSurface = new FrameworkElementFactory(typeof(Border));
                hitSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                hitSurface.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
                hitSurface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                ControlTemplate template = new ControlTemplate(typeof(Button));
                template.VisualTree = hitSurface;
                Button button = new Button
                {
                    Content = body,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "将勾选的快捷方式设为 " + initial.Name,
                    Template = template
                };

                Grid cardContent = new Grid();
                cardContent.Children.Add(button);
                Border border = new Border { Background = Brushes.White, BorderBrush = Color("#E4E0EF"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Child = cardContent, Margin = new Thickness(5, 0, 5, 8) };

                RoleCard card = new RoleCard
                {
                    Border = border,
                    PreviewImage = previewImage,
                    TitleText = label,
                    SubtitleText = subtitle,
                    MainButton = button,
                    Skins = groupSkins,
                    SkinButtons = new Button[groupSkins.Length],
                    SkinBorders = new Border[groupSkins.Length],
                    SkinLabels = new TextBlock[groupSkins.Length],
                    ActiveSkinIndex = 0
                };
                button.Click += delegate { SelectChoice(card.Current); };

                if (groupSkins.Length > 1)
                {
                    StackPanel pills = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0) };
                    for (int s = 0; s < groupSkins.Length; ++s)
                    {
                        int skinIndex = s;
                        IconChoice skin = groupSkins[s];
                        TextBlock pillText = Text(skin.SkinLabel, 9, "#6D6585");
                        pillText.TextAlignment = TextAlignment.Center;
                        Border pillBorder = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 1.5, 5, 1.5), Margin = new Thickness(0, 0, 0, 3), BorderThickness = new Thickness(1), Child = pillText };
                        ControlTemplate pillTemplate = new ControlTemplate(typeof(Button));
                        pillTemplate.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
                        Button pillButton = new Button { Content = pillBorder, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "切换为 " + skin.Name, Template = pillTemplate };
                        pillButton.Click += delegate
                        {
                            if (card.ActiveSkinIndex != skinIndex)
                            {
                                card.ActiveSkinIndex = skinIndex;
                                UpdateCardVisual(card);
                            }
                            SelectChoice(groupSkins[skinIndex]);
                        };
                        card.SkinButtons[s] = pillButton;
                        card.SkinBorders[s] = pillBorder;
                        card.SkinLabels[s] = pillText;
                        pills.Children.Add(pillButton);
                    }
                    cardContent.Children.Add(pills);
                }

                UpdateCardVisual(card);
                cards.Add(card);
                cardGrid.Children.Add(border);
            }
            cardScroll = new ScrollViewer
            {
                Content = cardGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Margin = new Thickness(-5, 0, -5, 4)
            };
            header.Children.Add(cardScroll);
            cards[0].Border.BorderBrush = Color("#8770D6"); cards[0].Border.Background = Color("#F1ECFF");

            toolbar = new WrapPanel { Margin = new Thickness(0, 2, 0, 11) };
            toolbar.Children.Add(ActionButton("扫描桌面", delegate { Scan(false); }, false));
            toolbar.Children.Add(ActionButton("添加快捷方式…", AddDialog, false));
            toolbar.Children.Add(ActionButton("添加开始菜单", delegate { Scan(true); }, false));
            toolbar.Children.Add(ActionButton("自定义 ICO…", ImportIcon, false));
            matchKnownButton = ActionButton("匹配常见 AI", MatchKnown, false);
            toolbar.Children.Add(matchKnownButton);
            header.Children.Add(toolbar);

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

            ApplyWorkAreaSizeBounds(GetCurrentMonitorWorkArea(), true);
            SourceInitialized += delegate
            {
                HwndSource hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                if (hwndSource != null) hwndSource.AddHook(WndProc);
                Rect wa = GetCurrentMonitorWorkArea();
                ApplyWorkAreaSizeBounds(wa, false);
                if (!simulatedWorkArea.HasValue) EnsureWindowReachable(wa);
            };
            LocationChanged += delegate
            {
                if (applyingBounds || inSizeMove || !IsLoaded) return;
                QueueBoundsUpdate();
            };
            viewportHost.SizeChanged += delegate { UpdateResponsiveLayout(); };

            if (preview)
            {
                bulkUpdating = true;
                try
                {
                    string[] names = { "ChatGPT", "Claude", "Antigravity", "Grok Bot", "DeepSeek", "ZCode", "豆包", "豆包工作", "MiniMax", "WorkBuddy", "Kimi" };
                    for (int i = 0; i < names.Length; ++i) Rows.Add(new ShortcutRow { Name = names[i], Path = "预览示例 / " + names[i] + ".lnk", Location = "桌面 · 示例", Choice = IconStore.BuiltIn[i], Checked = true, State = "示例" });
                }
                finally { bulkUpdating = false; }
                status.Text = "界面预览 · 这里是示例快捷方式，不会修改你的桌面。";
            }
            else if (fixtureFile != null) AddFiles(new[] { fixtureFile }, "隔离测试");
            else if (autoScan) Scan(false);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0231) // WM_ENTERSIZEMOVE
            {
                inSizeMove = true;
            }
            else if (msg == 0x0232) // WM_EXITSIZEMOVE
            {
                inSizeMove = false;
                QueueBoundsUpdate();
            }
            else if (msg == 0x02E0 || msg == 0x007E || msg == 0x001A) // DPI, display or work area changed
            {
                if (!inSizeMove) QueueBoundsUpdate();
            }
            return IntPtr.Zero;
        }

        private void QueueBoundsUpdate()
        {
            if (boundsUpdateQueued) return;
            boundsUpdateQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate
            {
                boundsUpdateQueued = false;
                if (IsLoaded) FinishMoveOrDisplayChange();
            }));
        }

        private static bool TryGetPhysicalWorkArea(IntPtr hwnd, out Rect work)
        {
            work = Rect.Empty;
            if (hwnd == IntPtr.Zero) return false;
            IntPtr monitor = MonitorFromWindow(hwnd, 2);
            MONITORINFO info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info)) return false;
            RECT r = info.rcWork;
            if (r.Right <= r.Left || r.Bottom <= r.Top) return false;
            work = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return true;
        }

        private Rect GetCurrentMonitorWorkArea()
        {
            if (simulatedWorkArea.HasValue) return simulatedWorkArea.Value;
            Rect physical;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null &&
                TryGetPhysicalWorkArea(new WindowInteropHelper(this).Handle, out physical))
            {
                // Layout only needs the size. Native screen coordinates stay in pixels.
                Vector size = source.CompositionTarget.TransformFromDevice.Transform(new Vector(physical.Width, physical.Height));
                return new Rect(0, 0, size.X, size.Y);
            }
            return SystemParameters.WorkArea;
        }

        internal void ApplyWorkAreaSizeBounds(Rect workArea, bool initial = false)
        {
            if (applyingBounds) return;
            applyingBounds = true;
            try
            {
                double maxW = Math.Max(320, workArea.Width);
                double maxH = Math.Max(320, workArea.Height);
                MaxWidth = maxW;
                MaxHeight = maxH;
                MinWidth = Math.Min(600, maxW);
                MinHeight = Math.Min(360, maxH);
                lastWorkAreaSize = new Size(maxW, maxH);
                if (initial)
                {
                    Width = Math.Min(1060, maxW);
                    Height = Math.Min(860, maxH);
                    if (simulatedWorkArea.HasValue)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
                        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
                    }
                }
                else if (!inSizeMove)
                {
                    if (Width > maxW) Width = maxW;
                    if (Height > maxH) Height = maxH;
                }
                UpdateResponsiveLayout();
            }
            finally { applyingBounds = false; }
        }

        internal static Point ReachableOrigin(Rect window, Rect workArea, double scaleX, double scaleY)
        {
            Rect titleStrip = new Rect(window.Location, new Size(window.Width, Math.Min(40 * scaleY, window.Height)));
            Rect visible = Rect.Intersect(titleStrip, workArea);
            if (!visible.IsEmpty && visible.Width >= Math.Min(80 * scaleX, window.Width * 0.25) &&
                visible.Height >= 16 * scaleY && window.Top >= workArea.Top - 8 * scaleY) return window.Location;
            return new Point(
                Math.Max(workArea.Left, Math.Min(window.Left, workArea.Right - window.Width)),
                Math.Max(workArea.Top, Math.Min(window.Top, workArea.Bottom - window.Height)));
        }

        private void EnsureWindowReachable(Rect workArea)
        {
            if (applyingBounds || inSizeMove || WindowState != WindowState.Normal) return;
            applyingBounds = true;
            try
            {
                if (simulatedWorkArea.HasValue)
                {
                    Rect window = new Rect(Left, Top, ActualWidth, ActualHeight);
                    Point origin = ReachableOrigin(window, workArea, 1, 1);
                    if (origin != window.Location) { Left = origin.X; Top = origin.Y; }
                    return;
                }
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Rect physicalWork;
                RECT r;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source == null || source.CompositionTarget == null || !TryGetPhysicalWorkArea(hwnd, out physicalWork) ||
                    !GetWindowRect(hwnd, out r) || r.Right <= r.Left || r.Bottom <= r.Top) return;
                Rect bounds = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                Matrix scale = source.CompositionTarget.TransformToDevice;
                Point target = ReachableOrigin(bounds, physicalWork, scale.M11, scale.M22);
                if (target != bounds.Location)
                    SetWindowPos(hwnd, IntPtr.Zero, (int)target.X, (int)target.Y, 0, 0, 0x0015); // keep size, Z order and focus
            }
            finally { applyingBounds = false; }
        }

        internal void FinishMoveOrDisplayChange()
        {
            if (inSizeMove) return;
            Rect wa = GetCurrentMonitorWorkArea();
            if (Math.Abs(wa.Width - lastWorkAreaSize.Width) > 0.5 || Math.Abs(wa.Height - lastWorkAreaSize.Height) > 0.5)
                ApplyWorkAreaSizeBounds(wa, false);
            EnsureWindowReachable(wa);
        }

        internal void RefreshLayoutForTest()
        {
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            double clientWidth = viewportHost.ActualWidth > 0 ? viewportHost.ActualWidth : Math.Max(0, Width - 16);
            double clientHeight = viewportHost.ActualHeight > 0 ? viewportHost.ActualHeight : Math.Max(0, Height - 40);
            bool compactHeight = clientHeight > 0 && clientHeight < 680;
            bool ultraShort = clientHeight > 0 && clientHeight < 440;
            bool narrowWidth = clientWidth > 0 && clientWidth < 760;

            root.Margin = ultraShort ? new Thickness(14, 8, 14, 8) : (compactHeight ? new Thickness(20, 12, 20, 12) : new Thickness(28, 22, 28, 20));
            // Reserve space for the scrollbar and for a skin button beside the centered avatar.
            double cardWidth = clientWidth - root.Margin.Left - root.Margin.Right -
                cardScroll.Margin.Left - cardScroll.Margin.Right - SystemParameters.VerticalScrollBarWidth;
            int columns = Math.Max(1, Math.Min(5, (int)(cardWidth / 163)));
            if (cardGrid.Columns != columns) cardGrid.Columns = columns;
            titleText.FontSize = ultraShort ? 18 : (narrowWidth ? 21 : 25);
            badgeBorder.Padding = ultraShort ? new Thickness(8, 3, 8, 3) : new Thickness(12, 6, 12, 6);
            taglineText.FontSize = ultraShort ? 11.5 : 13;
            taglineRow.Margin = ultraShort ? new Thickness(0, 2, 0, 4) : (compactHeight ? new Thickness(0, 4, 0, 6) : new Thickness(0, 6, 0, 12));
            toolbar.Margin = ultraShort ? new Thickness(0, 2, 0, 4) : (compactHeight ? new Thickness(0, 4, 0, 6) : new Thickness(0, 2, 0, 11));
            status.Margin = ultraShort ? new Thickness(0, 4, 0, 4) : (compactHeight ? new Thickness(0, 6, 0, 6) : new Thickness(0, 10, 0, 10));
            note.Visibility = ultraShort ? Visibility.Collapsed : Visibility.Visible;
            note.Margin = compactHeight ? new Thickness(0, 6, 0, 0) : new Thickness(0, 10, 0, 0);

            Thickness btnPadding = ultraShort ? new Thickness(9, 4, 9, 4) : (narrowWidth ? new Thickness(10, 6, 10, 6) : new Thickness(14, 9, 14, 9));
            Thickness btnMargin = (ultraShort || narrowWidth) ? new Thickness(0, 0, 6, 2) : new Thickness(0, 0, 8, 0);
            double btnMinHeight = ultraShort ? 30 : (narrowWidth ? 33 : 37);
            foreach (Button btn in chromeButtons)
            {
                btn.Padding = btnPadding;
                btn.Margin = btnMargin;
                btn.MinHeight = btnMinHeight;
            }

            double nonCardHeight = titleRow.ActualHeight + taglineRow.ActualHeight + toolbar.ActualHeight + footer.ActualHeight;
            if (nonCardHeight <= 0) nonCardHeight = ultraShort ? 132 : (compactHeight ? 196 : 230);
            double fixedHeight = nonCardHeight + root.Margin.Top + root.Margin.Bottom + taglineRow.Margin.Top + taglineRow.Margin.Bottom + toolbar.Margin.Top + toolbar.Margin.Bottom + 4;
            double minGridHeight = ultraShort ? 92 : Math.Min(150, Math.Max(110, clientHeight * 0.28));
            double availForCards = clientHeight - fixedHeight - minGridHeight;

            int cardRows = (cards.Count + cardGrid.Columns - 1) / cardGrid.Columns;
            double fullCardsHeight = cardRows * 136.0;
            bool autoCollapse = clientHeight > 0 && availForCards < 96;
            bool collapsed = userCardsCollapsed.HasValue ? userCardsCollapsed.Value : autoCollapse;

            if (collapsed)
            {
                cardScroll.Visibility = Visibility.Collapsed;
                toggleCardsButton.Visibility = Visibility.Visible;
                toggleCardsButton.Content = "展开角色";
            }
            else
            {
                cardScroll.Visibility = Visibility.Visible;
                if (clientHeight > 0 && availForCards < fullCardsHeight)
                {
                    cardScroll.MaxHeight = Math.Max(72, availForCards);
                    toggleCardsButton.Visibility = Visibility.Visible;
                    toggleCardsButton.Content = "收起角色";
                }
                else
                {
                    cardScroll.MaxHeight = double.PositiveInfinity;
                    toggleCardsButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Rows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (ShortcutRow oldRow in trackedRows) oldRow.PropertyChanged -= Row_PropertyChanged;
                trackedRows.Clear();
                foreach (ShortcutRow currentRow in Rows)
                    if (trackedRows.Add(currentRow)) currentRow.PropertyChanged += Row_PropertyChanged;
                if (!bulkUpdating && IsLoaded) UpdateCheckedSummaryStatus(Rows.Where(r => r.Checked).ToList());
                return;
            }
            if (e.OldItems != null)
                foreach (ShortcutRow row in e.OldItems)
                    if (trackedRows.Remove(row)) row.PropertyChanged -= Row_PropertyChanged;
            if (e.NewItems != null)
                foreach (ShortcutRow row in e.NewItems)
                    if (trackedRows.Add(row)) row.PropertyChanged += Row_PropertyChanged;
        }

        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (bulkUpdating) return;
            ShortcutRow row = sender as ShortcutRow;
            if (row == null || !Rows.Contains(row)) return;
            if (e.PropertyName != "Choice" && e.PropertyName != "Checked") return;

            List<ShortcutRow> checkedRows = Rows.Where(r => r.Checked).ToList();
            if (e.PropertyName == "Choice")
            {
                if (row.Choice == null)
                {
                    status.Text = row.Checked
                        ? "已清除 " + row.Name + " 的目标图标；请为勾选项重新选择角色。"
                        : "已清除 " + row.Name + " 的目标图标。";
                    return;
                }
                if (row.Checked)
                {
                    if (checkedRows.All(r => r.Choice == row.Choice))
                        status.Text = "已将 " + row.Name + " 设为 " + row.Choice.Name + "（当前 " + checkedRows.Count + " 项将应用 " + row.Choice.Name + "），点“应用”后生效。";
                    else
                        status.Text = "已将 " + row.Name + " 设为 " + row.Choice.Name + "（当前 " + checkedRows.Count + " 个勾选项含多种角色），点“应用”后按每行设置生效。";
                }
                else
                {
                    status.Text = "已将 " + row.Name + " 设为 " + row.Choice.Name + "；勾选后点“应用”即可生效。";
                }
                return;
            }

            UpdateCheckedSummaryStatus(checkedRows);
        }

        private void UpdateCheckedSummaryStatus(List<ShortcutRow> checkedRows)
        {
            if (checkedRows.Count == 0)
            {
                status.Text = "当前未勾选快捷方式。勾选后可点角色卡片或在每行下拉选择图标。";
                return;
            }
            if (checkedRows.Any(r => r.Choice == null))
            {
                status.Text = "当前已勾选 " + checkedRows.Count + " 项（有项目尚未选择图标角色），请先选择角色。";
                return;
            }
            IconChoice first = checkedRows[0].Choice;
            if (checkedRows.All(r => r.Choice == first))
                status.Text = "当前已勾选 " + checkedRows.Count + " 项（准备应用 " + first.Name + "），点“应用”后生效。";
            else
                status.Text = "当前已勾选 " + checkedRows.Count + " 项（各行已选不同角色），点“应用”后按每行设置生效。";
        }

        private Button ActionButton(string caption, Action action, bool primary)
        {
            Button button = new Button { Content = caption, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 8, 0), Background = Color(primary ? "#7861BD" : "#FFFFFF"), Foreground = primary ? Brushes.White : Color("#534B6D"), BorderBrush = Color(primary ? "#7861BD" : "#DED9EC"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 37 };
            button.Click += delegate
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            chromeButtons.Add(button);
            return button;
        }
        private static void UpdateCardVisual(RoleCard card)
        {
            IconChoice current = card.Current;
            ImageSource src = current.Preview();
            if (!ReferenceEquals(card.PreviewImage.Source, src)) card.PreviewImage.Source = src;
            card.TitleText.Text = current.Name;
            card.SubtitleText.Text = current.Subtitle;
            card.MainButton.ToolTip = "将勾选的快捷方式设为 " + current.Name;
            if (card.Skins.Length <= 1) return;
            for (int s = 0; s < card.Skins.Length; ++s)
            {
                bool active = s == card.ActiveSkinIndex;
                card.SkinBorders[s].Background = Color(active ? "#7861BD" : "#F5F2FC");
                card.SkinBorders[s].BorderBrush = Color(active ? "#7861BD" : "#DED8F0");
                card.SkinLabels[s].Foreground = active ? Brushes.White : Color("#6D6585");
                card.SkinLabels[s].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        private void SelectChoice(IconChoice choice)
        {
            selected = choice;
            int count = 0;
            bulkUpdating = true;
            try
            {
                foreach (ShortcutRow row in Rows.Where(r => r.Checked)) { row.Choice = choice; ++count; }
            }
            finally { bulkUpdating = false; }
            foreach (RoleCard card in cards)
            {
                int idx = Array.IndexOf(card.Skins, choice);
                bool active = idx >= 0;
                if (active && card.ActiveSkinIndex != idx)
                {
                    card.ActiveSkinIndex = idx;
                    UpdateCardVisual(card);
                }
                card.Border.BorderBrush = Color(active ? "#8770D6" : "#E4E0EF");
                card.Border.Background = Color(active ? "#F1ECFF" : "#FFFFFF");
            }
            status.Text = count == 0 ? "已选择 " + choice.Name + "。先勾选快捷方式，再点角色卡片即可设置。" : "已为 " + count + " 个勾选项选择 " + choice.Name + "，点“应用”后生效。";
        }
        internal void AddFiles(IEnumerable<string> files, string location)
        {
            List<string> failures = new List<string>();
            bulkUpdating = true;
            try
            {
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
            }
            finally { bulkUpdating = false; }
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
            ImportCustomIconFile(dialog.FileName);
        }
        internal IconChoice ImportCustomIconFile(string fileName)
        {
            IconStore.Validate(File.ReadAllBytes(fileName));
            IconChoice choice = new IconChoice("custom-" + Guid.NewGuid().ToString("N"), "自定义 · " + Path.GetFileNameWithoutExtension(fileName), "自定义 ICO", fileName);
            choices.Add(choice); SelectChoice(choice);
            return choice;
        }
        private void MatchKnown()
        {
            int count = 0;
            bulkUpdating = true;
            try
            {
                foreach (ShortcutRow row in Rows)
                {
                    IconChoice match = IconStore.Suggest(row.Name);
                    row.Checked = match != null;
                    if (match != null) { row.Choice = match; ++count; }
                }
            }
            finally { bulkUpdating = false; }
            status.Text = "按名称匹配了 " + count + " 项；请检查列表后点“应用”。Gemini Notebook 及冲突或非产品组合名称不自动匹配。";
        }
        private void RunOperation(bool restore)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            List<ShortcutRow> rows = Rows.Where(r => r.Checked).ToList();
            if (rows.Count == 0) { status.Text = "请先勾选至少一个快捷方式。"; return; }
            if (!restore && rows.Any(r => r.Choice == null)) { status.Text = "有勾选项尚未选择图标角色，请先选择角色后再点“应用”。"; return; }
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
                "① 勾选快捷方式。可扫描桌面、添加开始菜单，或拖入 .lnk。\n② 点击角色卡片（豆包支持切换“经典 / 工作”皮肤），或在每一行选择不同的角色。\n③ 点“应用 AI 娘图标”。不喜欢时，勾选原项目并点“恢复”。\n\n" +
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
