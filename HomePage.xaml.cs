using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// 本文件同时用到 IO 的 Path 和矢量图形 Path，映射别名避免歧义
using ShapePath = System.Windows.Shapes.Path;

namespace WidgetNest;

public partial class HomePage : UserControl {
    private const int Rows = 3;
    private const int Cols = 6;

    // 这几支底色跟着主题走：主题切换时整块重建，所以用属性现取而不是缓存字段
    private static Brush EmptySlotBackground => ThemeManager.InkBrush(0x0F);
    private static Brush EmptySlotBorder => ThemeManager.InkBrush(0x33);
    private static Brush DropHighlight => ThemeManager.InkBrush(0x33);
    private static Brush WidgetCard => ThemeManager.Brush(ThemeManager.WidgetCard);

    private readonly List<AppItem> _items = new();
    private int _currentPage;

    // 图标位图缓存：RefreshPage 很频繁，避免每次重建都重新解码 PNG
    private readonly Dictionary<string, ImageSource?> _iconCache = new();

    // 小组件视图缓存：重建面板时复用实例，时钟/传感器不会因此断流
    private readonly Dictionary<AppItem, WidgetView> _widgets = new();

    // 加载完成前不要回写，避免用空布局覆盖已有数据
    private bool _ready;

    // 图标名称开关的当前状态：变了才重建格子
    private bool _iconLabelsShown = true;

    // 拖拽状态
    private AppItem? _dragItem;
    private Point _dragStart;
    private bool _isDragging;
    private FrameworkElement? _dragVisual;
    private (int row, int col)? _dropTarget;

    // 宿主窗口（用于兜底接收面板外的鼠标松开）
    private Window? _hostWindow;

    // ===== 边缘自动翻页 =====
    // 触控板拖拽时无法同时使用滚轮，因此在网格上/下边缘设置停留区：
    // 停留超过 EdgeDwellMs 自动翻一页，持续停留则连续翻页。
    private const int EdgeDwellMs = 450;
    private const double EdgeZoneMinPx = 8;
    private const double EdgeZoneMaxPx = 20;

    private readonly DispatcherTimer _edgeTimer = new();

    /// <summary>-1 = 上边缘（上一页），1 = 下边缘（下一页），0 = 未触发。</summary>
    private int _edgeDir;

    // ===== 启动应用 =====
    // 单击即可打开，但用户习惯性双击时不能把同一个程序启动两次
    private const int LaunchDebounceMs = 500;

    private string _lastLaunchPath = "";
    private long _lastLaunchTick;

    // ===== 编辑模式（多选删除） =====
    private static Brush SelectorIdleBackground => ThemeManager.Frozen(
        ThemeManager.IsLight ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x1A, 0x1A, 0x1A));
    private static Brush SelectorIdleBorder => ThemeManager.InkBrush(0xE6);
    private static Brush SelectorActiveBackground => ThemeManager.Brush(ThemeManager.Accent);
    private static Brush SelectedSlotBackground => ThemeManager.InkBrush(0x24);
    private static Brush SelectedSlotBorder => ThemeManager.InkBrush(0x66);

    private readonly HashSet<AppItem> _selectedItems = new();

    public HomePage() {
        InitializeComponent();

        _edgeTimer.Interval = TimeSpan.FromMilliseconds(EdgeDwellMs);
        _edgeTimer.Tick += EdgeTimer_Tick;

        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        PreviewMouseWheel += OnPreviewMouseWheel;
        LostMouseCapture += OnLostMouseCapture;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 主题切换：小组件视图里有些笔是建的时候算的色，缓存的实例要丢掉重建
        ThemeManager.ThemeChanged += OnThemeChanged;

        // 偏好设置里改了页码开关：面板本身不用重建，刷一下指示器就够了
        PrefStore.Changed += OnPreferencesChanged;

        // 偏好设置里的开关：能就地刷新的就地刷新，影响格子内容的整体重建
        _iconLabelsShown = PrefStore.ShowIconLabels;
        LoadState();

        // 空态提示里的组合键跟着偏好设置走
        HotkeyRun.Text = PrefStore.Hotkey.Display;
    }

    /// <summary>主题变了：丢掉小组件视图缓存，让它们用新配色重建。</summary>
    private void OnThemeChanged(object? sender, EventArgs e) {
        foreach (var view in _widgets.Values)
            WidgetRuntime.Untrack(view);

        _widgets.Clear();
        _iconCache.Clear();

        RefreshPage(persist: false);
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) {
        HotkeyRun.Text = PrefStore.Hotkey.Display;
        UpdatePageIndicator();

        // 图标名称开关：格子里少一行文字，得重建一次（不落盘，只是画面变化）
        if (_iconLabelsShown != PrefStore.ShowIconLabels) {
            _iconLabelsShown = PrefStore.ShowIconLabels;
            RefreshPage(persist: false);
        }
    }

    /// <summary>
    /// 添加一个应用到第一个空槽，所有页已满时自动新建一页。
    /// 图标本体从 <paramref name="sourcePath"/> 提取后落盘到 FilesDate。
    /// </summary>
    public void AddApp(string sourcePath) {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        string name = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(name))
            name = "应用";

        var item = new AppItem {
            Name = name,
            Kind = AppItem.KindApp,
            TargetPath = sourcePath,
            IconFile = AppStorage.TryCacheIcon(sourcePath) ?? ""
        };

        PlaceNewItem(item);
    }

    /// <summary>
    /// 从小组件市场添加一个小组件，按它声明的行列跨度找位置。
    /// </summary>
    public bool AddWidget(WidgetDef def) {
        var item = new AppItem {
            Name = def.Name,
            Kind = AppItem.KindWidget,
            WidgetId = def.Id,
            IconPath = def.IconPath,
            RowSpan = def.Rows,
            ColSpan = def.Cols
        };

        return PlaceNewItem(item);
    }

    /// <summary>
    /// 自定义组件被卸载时，把它在面板上的实例一起摘掉，避免留下点不开的空壳。
    /// familyKey 形如 "u:xxx"，变体 id 是 "u:xxx" 或 "u:xxx-2x2"，所以按前缀匹配。
    /// </summary>
    public int RemoveWidgetFamily(string familyKey) {
        if (string.IsNullOrEmpty(familyKey))
            return 0;

        var targets = _items
            .Where(item => item.IsWidget &&
                           item.WidgetId.StartsWith(familyKey, StringComparison.Ordinal))
            .ToList();

        if (targets.Count == 0)
            return 0;

        foreach (var item in targets) {
            _items.Remove(item);
            _selectedItems.Remove(item);
        }

        RemoveEmptyPages();

        if (_items.Count == 0 && IsEditMode) {
            IsEditMode = false;
            WidgetRuntime.EditMode = false;
            EditModeChanged?.Invoke(this, EventArgs.Empty);
        }

        RefreshPage();
        return targets.Count;
    }

    private bool PlaceNewItem(AppItem item) {
        int maxPage = _items.Count == 0 ? 0 : _items.Max(i => i.Page);

        for (int page = 0; page <= maxPage; page++) {
            if (FindFreeSpot(page, item) is not { } spot)
                continue;

            item.Page = page;
            item.Row = spot.Row;
            item.Col = spot.Col;
            _items.Add(item);
            _currentPage = page;
            RefreshPage();
            return true;
        }

        // 所有现有页都放不下（比如整页组件），新开一页
        item.Page = maxPage + 1;
        item.Row = 0;
        item.Col = 0;
        _items.Add(item);
        _currentPage = item.Page;
        RefreshPage();
        return true;
    }

    // ===== 网格占位计算 =====

    private AppItem? ItemAt(int page, int row, int col) {
        foreach (var item in _items) {
            if (item.Page != page)
                continue;

            if (row >= item.Row && row < item.Row + item.SafeRowSpan &&
                col >= item.Col && col < item.Col + item.SafeColSpan)
                return item;
        }

        return null;
    }

    /// <summary>指定区域是否空闲（<paramref name="ignore"/> 自身不参与阻挡）。</summary>
    private bool RegionFree(int page, int row, int col, int rowSpan, int colSpan, AppItem? ignore) {
        if (row < 0 || col < 0 || row + rowSpan > Rows || col + colSpan > Cols)
            return false;

        for (int r = row; r < row + rowSpan; r++) {
            for (int c = col; c < col + colSpan; c++) {
                var occupant = ItemAt(page, r, c);
                if (occupant != null && !ReferenceEquals(occupant, ignore))
                    return false;
            }
        }

        return true;
    }

    private (int Row, int Col)? FindFreeSpot(int page, AppItem item) {
        int rowSpan = item.SafeRowSpan;
        int colSpan = item.SafeColSpan;

        for (int row = 0; row + rowSpan <= Rows; row++) {
            for (int col = 0; col + colSpan <= Cols; col++) {
                if (RegionFree(page, row, col, rowSpan, colSpan, item))
                    return (row, col);
            }
        }

        return null;
    }

    // ===== 编辑模式对外接口 =====

    public bool IsEditMode { get; private set; }

    public int SelectedCount => _selectedItems.Count;

    /// <summary>选中集合变化（含进入/退出编辑模式时的清空）。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>编辑模式开关变化。</summary>
    public event EventHandler? EditModeChanged;

    /// <summary>
    /// 进入/退出编辑模式。退出或进入时都会清空已有选中项。
    /// </summary>
    public void SetEditMode(bool on) {
        if (on) {
            // 切进编辑模式前先把可能进行中的拖拽收尾
            FinishDrag(applyDrop: false);
        }

        bool changed = IsEditMode != on;
        IsEditMode = on;
        WidgetRuntime.EditMode = on;
        _selectedItems.Clear();

        RefreshPage(persist: false);

        if (changed)
            EditModeChanged?.Invoke(this, EventArgs.Empty);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 删除当前选中的图标/小组件，返回删除数量。
    /// 只移除面板条目与不再被引用的图标文件，不会动用户的原始文件。
    /// </summary>
    public int RemoveSelected() {
        if (_selectedItems.Count == 0)
            return 0;

        var removed = _selectedItems.ToList();
        _selectedItems.Clear();

        foreach (var item in removed)
            _items.Remove(item);

        foreach (var item in removed) {
            if (item.IsWidget || string.IsNullOrWhiteSpace(item.IconFile))
                continue;

            // 图标文件是内容寻址的（按源路径哈希命名），可能被多个条目共用，确认无人引用再删
            bool stillUsed = _items.Any(i =>
                string.Equals(i.IconFile, item.IconFile, StringComparison.OrdinalIgnoreCase));

            if (stillUsed)
                continue;

            AppStorage.DeleteIcon(item.IconFile);
            _iconCache.Remove(item.IconFile);
        }

        RemoveEmptyPages();

        // 全删空后自动退出编辑模式，否则会停在没有任何内容的编辑态
        if (_items.Count == 0 && IsEditMode) {
            IsEditMode = false;
            WidgetRuntime.EditMode = false;
            EditModeChanged?.Invoke(this, EventArgs.Empty);
        }

        RefreshPage();
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        return removed.Count;
    }

    // ===== 组合：相邻小组件视觉合并 =====

    /// <summary>当前选择能做的组合动作，决定底栏按钮显示「组合」还是「拆分」。</summary>
    public CombineAction SelectionCombineAction => ResolveCombineAction();

    /// <summary>
    /// 执行底栏按钮的动作：组合两个相邻小组件，或把已组合的两块拆开。
    /// 组合只是打标记（不改坐标），拆开只是清标记，两块各自回到原来的样子。
    /// </summary>
    public bool ApplyCombineAction() {
        var action = ResolveCombineAction();

        if (action == CombineAction.None)
            return false;

        var pair = _selectedItems.ToList();

        if (action == CombineAction.Split) {
            pair[0].GroupId = "";
            pair[1].GroupId = "";
        }
        else {
            // 任一方原本在别的组里：先解散旧组，保证一块只属于一个组合
            ReleaseGroup(pair[0]);
            ReleaseGroup(pair[1]);

            string groupId = Guid.NewGuid().ToString("N");
            pair[0].GroupId = groupId;
            pair[1].GroupId = groupId;
        }

        RefreshPage();
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    private CombineAction ResolveCombineAction() {
        if (_selectedItems.Count != 2)
            return CombineAction.None;

        var pair = _selectedItems.ToList();
        var first = pair[0];
        var second = pair[1];

        if (!first.IsWidget || !second.IsWidget)
            return CombineAction.None;

        if (!AreAdjacent(first, second))
            return CombineAction.None;

        bool alreadyGrouped = !string.IsNullOrEmpty(first.GroupId) &&
                              string.Equals(first.GroupId, second.GroupId, StringComparison.Ordinal);

        return alreadyGrouped ? CombineAction.Split : CombineAction.Combine;
    }

    /// <summary>
    /// 左右相邻要求同高（同一行、同样行跨度），上下相邻要求同宽（同一列、同样列跨度）。
    /// </summary>
    private static bool AreAdjacent(AppItem a, AppItem b) {
        if (a.Page != b.Page)
            return false;

        bool sideBySide =
            a.Row == b.Row && a.SafeRowSpan == b.SafeRowSpan &&
            (a.Col + a.SafeColSpan == b.Col || b.Col + b.SafeColSpan == a.Col);

        bool stacked =
            a.Col == b.Col && a.SafeColSpan == b.SafeColSpan &&
            (a.Row + a.SafeRowSpan == b.Row || b.Row + b.SafeRowSpan == a.Row);

        return sideBySide || stacked;
    }

    /// <summary>同一组里的另一块（每人最多一个伙伴）。</summary>
    private AppItem? GroupPartnerOf(AppItem item) {
        if (string.IsNullOrEmpty(item.GroupId))
            return null;

        return _items.FirstOrDefault(other =>
            !ReferenceEquals(other, item) &&
            other.Page == item.Page &&
            string.Equals(other.GroupId, item.GroupId, StringComparison.Ordinal));
    }

    /// <summary>本块哪几条边要和伙伴拼在一起（只用于渲染：去外边距 + 去圆角）。</summary>
    private (bool left, bool right, bool top, bool bottom) GroupEdgesOf(AppItem item) {
        var partner = GroupPartnerOf(item);

        if (partner == null)
            return (false, false, false, false);

        bool left =
            partner.Row == item.Row &&
            partner.Col + partner.SafeColSpan == item.Col;

        bool right =
            partner.Row == item.Row &&
            item.Col + item.SafeColSpan == partner.Col;

        bool top =
            partner.Col == item.Col &&
            partner.Row + partner.SafeRowSpan == item.Row;

        bool bottom =
            partner.Col == item.Col &&
            item.Row + item.SafeRowSpan == partner.Row;

        return (left, right, top, bottom);
    }

    private static CornerRadius MergedCorners((bool left, bool right, bool top, bool bottom) edges) {
        const double r = 8;

        double topLeft = r;
        double topRight = r;
        double bottomRight = r;
        double bottomLeft = r;

        if (edges.left) {
            topLeft = 0;
            bottomLeft = 0;
        }

        if (edges.right) {
            topRight = 0;
            bottomRight = 0;
        }

        if (edges.top) {
            topLeft = 0;
            topRight = 0;
        }

        if (edges.bottom) {
            bottomLeft = 0;
            bottomRight = 0;
        }

        return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
    }

    /// <summary>把某一块从它的组合里摘出来（伙伴一并清掉标记）。</summary>
    private void ReleaseGroup(AppItem item) {
        if (string.IsNullOrEmpty(item.GroupId))
            return;

        string groupId = item.GroupId;

        foreach (var member in _items.Where(i =>
                     string.Equals(i.GroupId, groupId, StringComparison.Ordinal)))
            member.GroupId = "";
    }

    /// <summary>
    /// 收拾失效的组合：伙伴被删、被拖开、被换位之后，剩下的单块不该再算组合。
    /// 每次重建面板前跑一遍，保证画面与数据一致。
    /// </summary>
    private void NormalizeGroups() {
        var groups = _items
            .Where(i => !string.IsNullOrEmpty(i.GroupId))
            .GroupBy(i => i.GroupId, StringComparer.Ordinal)
            .ToList();

        foreach (var group in groups) {
            var members = group.ToList();

            bool valid = members.Count == 2 &&
                         members.All(m => m.IsWidget) &&
                         AreAdjacent(members[0], members[1]);

            if (valid)
                continue;

            foreach (var member in members)
                member.GroupId = "";
        }
    }

    // ===== 持久化 =====

    private void LoadState() {
        AppStorage.EnsureCreated();

        var state = AppStorage.Load();

        _items.Clear();

        foreach (var item in state.Items) {
            if (string.IsNullOrWhiteSpace(item.Kind))
                item.Kind = AppItem.KindApp;

            // 未知小组件（版本回退等）直接丢弃，避免渲染空白块
            if (item.IsWidget && WidgetCatalog.Find(item.WidgetId) == null)
                continue;

            int rowSpan = item.SafeRowSpan;
            int colSpan = item.SafeColSpan;

            // 丢弃非法坐标与越界占位，避免旧数据把排版搞乱
            if (item.Page < 0 || item.Row < 0 || item.Col < 0)
                continue;

            if (item.Row + rowSpan > Rows || item.Col + colSpan > Cols)
                continue;

            if (!RegionFreeInLoad(item, rowSpan, colSpan))
                continue;

            item.RowSpan = rowSpan;
            item.ColSpan = colSpan;
            _items.Add(item);
        }

        _currentPage = Math.Max(0, state.CurrentPage);

        // 归位页码，防止历史数据里有空洞
        RemoveEmptyPages();
        RefreshPage();

        _ready = true;
    }

    /// <summary>加载阶段的占位检查：只看已经收进来的条目。</summary>
    private bool RegionFreeInLoad(AppItem candidate, int rowSpan, int colSpan) {
        foreach (var other in _items) {
            if (other.Page != candidate.Page)
                continue;

            bool overlap =
                candidate.Row < other.Row + other.SafeRowSpan &&
                other.Row < candidate.Row + rowSpan &&
                candidate.Col < other.Col + other.SafeColSpan &&
                other.Col < candidate.Col + colSpan;

            if (overlap)
                return false;
        }

        return true;
    }

    private void Persist() {
        if (!_ready)
            return;

        AppStorage.Save(new AppState {
            CurrentPage = _currentPage,
            Items = new List<AppItem>(_items)
        });
    }

    private void RefreshPage(bool persist = true) {
        // 先对齐小组件实例：被删掉的停止心跳并释放
        SyncWidgets();

        // 伙伴被删/被拖开之后，残留的单个标记先清掉，否则会按「有组合」去收边距
        NormalizeGroups();

        ItemGrid.Children.Clear();
        SlotLayer.Children.Clear();

        // 小格子先放，大组件后放，避免遮挡边缘
        foreach (var item in _items
                     .Where(i => i.Page == _currentPage)
                     .OrderBy(i => i.SafeRowSpan * i.SafeColSpan)) {
            var host = CreateItemHost(item);

            Grid.SetRow(host, item.Row);
            Grid.SetColumn(host, item.Col);
            Grid.SetRowSpan(host, item.SafeRowSpan);
            Grid.SetColumnSpan(host, item.SafeColSpan);

            ItemGrid.Children.Add(host);
        }

        // 空格提示（拖拽时才显示）
        for (int row = 0; row < Rows; row++) {
            for (int col = 0; col < Cols; col++) {
                if (ItemAt(_currentPage, row, col) != null)
                    continue;

                var slot = new Border {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(8),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Tag = (row, col)
                };

                Grid.SetRow(slot, row);
                Grid.SetColumn(slot, col);
                SlotLayer.Children.Add(slot);
            }
        }

        SlotLayer.Visibility = _isDragging ? Visibility.Visible : Visibility.Collapsed;

        UpdatePageIndicator();
        HighlightTarget();

        EmptyHint.Visibility = _items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 兜底：非拖拽状态下，清掉任何可能残留的拖拽视觉
        if (!_isDragging)
            RemoveDragVisual();

        // 布局/页码任何变化都落盘（加载阶段由 _ready 拦住）
        if (persist)
            Persist();
    }

    /// <summary>停掉已从面板移除的小组件，保留仍在用的实例。</summary>
    private void SyncWidgets() {
        WidgetRuntime.DeactivateAll();

        var alive = new HashSet<AppItem>(_items);
        var dead = _widgets.Keys.Where(item => !alive.Contains(item)).ToList();

        foreach (var item in dead) {
            WidgetRuntime.Untrack(_widgets[item]);
            _widgets.Remove(item);
        }
    }

    private FrameworkElement GetWidgetRoot(AppItem item) {
        if (_widgets.TryGetValue(item, out var cached)) {
            cached.IsActive = true;

            // 重建面板后旧宿主还在引用它，先摘下来才能挂到新卡片上
            DetachFromParent(cached.Root);
            return cached.Root;
        }

        var def = WidgetCatalog.Find(item.WidgetId);
        var view = def == null
            ? null
            : WidgetFactory.Create(def, new WidgetBox(item.SafeRowSpan, item.SafeColSpan));

        if (view == null) {
            // 理论上不会发生（加载时已过滤），兜底显示名称
            return new TextBlock {
                Text = item.Name,
                Foreground = W.DimBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        WidgetRuntime.Track(view);
        view.IsActive = true;
        _widgets[item] = view;

        return view.Root;
    }

    /// <summary>把元素从上一个父级摘下来（复用小组件视图时必须做）。</summary>
    private static void DetachFromParent(FrameworkElement element) {
        switch (VisualTreeHelper.GetParent(element)) {
            case Decorator decorator:
                if (ReferenceEquals(decorator.Child, element))
                    decorator.Child = null;
                break;

            case Panel panel:
                panel.Children.Remove(element);
                break;

            case ContentControl content:
                if (ReferenceEquals(content.Content, element))
                    content.Content = null;
                break;
        }
    }

    /// <summary>
    /// 单元格宿主：图标是透明的点击区，小组件是带底色的卡片。
    /// 已组合的两块会把贴合边的外边距与圆角收掉，视觉上连成一整块。
    /// </summary>
    private FrameworkElement CreateItemHost(AppItem item) {
        bool selected = IsEditMode && _selectedItems.Contains(item);

        var edges = GroupEdgesOf(item);

        double gap = item.IsWidget ? 3 : 4;

        var host = new Border {
            Margin = new Thickness(
                edges.left ? 0 : gap,
                edges.top ? 0 : gap,
                edges.right ? 0 : gap,
                edges.bottom ? 0 : gap),
            CornerRadius = MergedCorners(edges),
            Background = item.IsWidget ? WidgetCard : Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Tag = item,
            // 编辑模式整格可拖动，给个挪动光标提示
            Cursor = IsEditMode ? Cursors.SizeAll : (item.IsWidget ? Cursors.Arrow : Cursors.Hand)
        };

        if (selected) {
            host.Background = SelectedSlotBackground;
            host.BorderBrush = SelectedSlotBorder;
        }

        FrameworkElement content = item.IsWidget
            ? GetWidgetRoot(item)
            : CreateIconContent(item);

        if (IsEditMode) {
            var wrapper = new Grid();
            wrapper.Children.Add(content);
            wrapper.Children.Add(CreateSelector(selected));
            host.Child = wrapper;

            // 编辑模式下整格都是拖动把手：走预览隧道，滑块/输入框不会先抢走鼠标
            host.PreviewMouseLeftButtonDown += ItemHost_PreviewMouseLeftButtonDown;
        }
        else {
            host.Child = content;

            // 常规模式只响应单击启动，不提供拖动
            host.MouseLeftButtonDown += ItemHost_MouseLeftButtonDown;
        }

        return host;
    }

    private FrameworkElement CreateSelector(bool selected) {
        var circle = new Border {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, -3, 0),
            Background = selected ? SelectorActiveBackground : SelectorIdleBackground,
            BorderBrush = selected ? SelectorActiveBackground : SelectorIdleBorder,
            BorderThickness = new Thickness(1.4),
            IsHitTestVisible = false
        };

        if (selected) {
            circle.Child = new ShapePath {
                Data = Geometry.Parse("M9,16.17 L4.83,12 L3.41,13.41 L9,19 L21,7 L19.59,5.59 Z"),
                Fill = Brushes.White,
                Stretch = Stretch.Uniform,
                Width = 10,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return circle;
    }

    private FrameworkElement CreateIconContent(AppItem item) {
        var stack = new StackPanel {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var cached = GetCachedIcon(item.IconFile);

        if (cached != null) {
            stack.Children.Add(new Image {
                Source = cached,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        else {
            // 取不到位图时退回矢量图标
            stack.Children.Add(new ShapePath {
                Data = Geometry.Parse(string.IsNullOrWhiteSpace(item.IconPath)
                    ? AppItem.DefaultIconPath
                    : item.IconPath),
                Fill = ThemeManager.InkBrush(0xCC),
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        // 偏好设置里关掉「显示图标名称」后，格子里只留图标，靠 StackPanel 居中自动对齐
        if (PrefStore.ShowIconLabels) {
            stack.Children.Add(new TextBlock {
                Text = item.Name,
                Foreground = ThemeManager.InkBrush(0xCC),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 52,
                TextAlignment = TextAlignment.Center
            });
        }

        return stack;
    }

    /// <summary>
    /// 从 FilesDate 读取图标位图，按文件名缓存；文件缺失或损坏返回 null。
    /// </summary>
    private ImageSource? GetCachedIcon(string iconFile) {
        if (string.IsNullOrWhiteSpace(iconFile))
            return null;

        if (_iconCache.TryGetValue(iconFile, out var cached))
            return cached;

        ImageSource? result = null;

        try {
            string path = AppStorage.ResolveIconPath(iconFile);
            if (File.Exists(path)) {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;  // 读完即释放文件句柄
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();

                result = bitmap;
            }
        }
        catch {
            result = null;
        }

        _iconCache[iconFile] = result;
        return result;
    }

    private void UpdatePageIndicator() {
        int totalPages = _items.Count == 0 ? 1 : _items.Max(i => i.Page) + 1;
        if (_currentPage >= totalPages)
            totalPages = _currentPage + 1;

        PageIndicator.Text = $"{_currentPage + 1}/{totalPages}";

        // 只有一页时页码没有意义；用户在偏好设置里关掉后也不显示
        bool show = totalPages > 1 && PrefStore.ShowPageIndicator;
        PageIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HighlightTarget() {
        if (!_isDragging) {
            SlotLayer.Visibility = Visibility.Collapsed;
            return;
        }

        SlotLayer.Visibility = Visibility.Visible;

        foreach (var child in SlotLayer.Children.OfType<Border>()) {
            child.Background = EmptySlotBackground;
            child.BorderBrush = EmptySlotBorder;
        }

        // 落点区域整块高亮（可能压在已有条目上，所以用一块跨格的幽灵框）
        if (_dropTarget is not { } target || _dragItem == null)
            return;

        var ghost = new Border {
            Background = DropHighlight,
            BorderBrush = EmptySlotBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4),
            Tag = "ghost"
        };

        Grid.SetRow(ghost, target.row);
        Grid.SetColumn(ghost, target.col);
        Grid.SetRowSpan(ghost, _dragItem.SafeRowSpan);
        Grid.SetColumnSpan(ghost, _dragItem.SafeColSpan);

        SlotLayer.Children.Add(ghost);
    }

    private AppItem? FindItem(int page, int row, int col) => ItemAt(page, row, col);

    // ===== 图标拖拽（仅编辑模式） =====

    /// <summary>
    /// 编辑模式：整格按下只记录拖拽起点，不立即切换选中。
    /// 用预览事件是为了在滑块、输入框等子控件之前接管鼠标，避免拖动被它们吃掉。
    /// 松开时若没移动过，才按「单击 = 切换选中」处理。
    /// </summary>
    private void ItemHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not Border host || host.Tag is not AppItem item)
            return;

        // 防御：进新一轮按下前，先把上一次可能卡住的残留状态清干净
        if (_isDragging || _dragVisual != null)
            FinishDrag(applyDrop: false);

        _dragItem = item;
        _dragStart = e.GetPosition(this);
        _isDragging = false;

        // 捕获延后到真正开始拖动时再做，期间不让子控件拿到这次按下
        e.Handled = true;
    }

    /// <summary>
    /// 常规模式：单击启动程序，不参与拖动。
    /// </summary>
    private void ItemHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not Border host || host.Tag is not AppItem item)
            return;

        // 小组件里的输入框/滑块/自绘按钮自己处理点击
        if (item.IsWidget && IsInteractiveSource(e.OriginalSource as DependencyObject, host))
            return;

        // 防御：进新一轮点击前，先把上一次可能卡住的残留状态清干净
        if (_isDragging || _dragVisual != null) {
            _isDragging = false;
            RemoveDragVisual();
            _dropTarget = null;
        }

        _dragItem = item;
        _dragStart = e.GetPosition(this);
        _isDragging = false;

        // 捕获延后到真正开始拖动时再做：否则组件的点击事件会被抢走
        e.Handled = true;
    }

    /// <summary>按下点是否落在可交互控件上（文本/滑块/按钮/滚动区）。</summary>
    private static bool IsInteractiveSource(DependencyObject? source, DependencyObject stopAt) {
        while (source != null && !ReferenceEquals(source, stopAt)) {
            if (source is TextBox or PasswordBox or Slider or ButtonBase or ScrollViewer or ListBox)
                return true;

            // 自绘控件（滑块 / 勾选圈 / 行内按钮）自己打了标记
            if (source is FrameworkElement element && element.Tag is W.InteractiveTag)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ToggleSelection(AppItem item) {
        if (!_selectedItems.Remove(item))
            _selectedItems.Add(item);

        // 选中状态只影响画面，不需要回写磁盘
        RefreshPage(persist: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e) {
        if (_dragItem == null && _dragVisual == null)
            return;

        // 兜底：万一 MouseUp 被别的窗口/系统吃掉，靠按键状态判断松手并收尾
        if (e.LeftButton != MouseButtonState.Pressed) {
            FinishDrag(applyDrop: _isDragging);
            return;
        }

        if (!_isDragging) {
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance) {
                // 只有编辑模式允许拖动；常规模式下这次按下直接作废，松手也不启动程序
                if (IsEditMode)
                    StartDrag();
                else {
                    _dragItem = null;
                    _dropTarget = null;
                }
            }
        }

        if (_isDragging && _dragVisual != null) {
            var pos = e.GetPosition(DragLayer);
            Canvas.SetLeft(_dragVisual, pos.X - _dragVisual.Width / 2);
            Canvas.SetTop(_dragVisual, pos.Y - _dragVisual.Height / 2);

            var posInGrid = e.GetPosition(ItemGrid);
            UpdateDropTarget(posInGrid);
            UpdateEdgeZone(posInGrid);
        }
    }

    private void StartDrag() {
        if (_dragItem is not { } dragged) {
            _isDragging = false;
            return;
        }

        // 防御：若已有拖拽视觉（理论不该发生），先清掉
        RemoveDragVisual();

        // 拖动组合里的一块 = 解散这一组，另一块留在原地。
        // 放在捕获之前：即使随后捕获失败，组合也已经按「用户开始拖动」处理了。
        if (dragged.GroupId.Length > 0) {
            ReleaseGroup(dragged);
            RefreshPage(persist: false);
        }

        _isDragging = true;

        // 拖拽期间确保捕获在我们身上，才能拿到松手事件
        if (Mouse.Captured != this)
            Mouse.Capture(this);

        // 捕获可能立刻被系统抢走，那样拖拽已经被 FinishDrag 收尾了，不能再往下建视觉
        if (!_isDragging || !ReferenceEquals(_dragItem, dragged)) {
            RemoveDragVisual();
            return;
        }

        _dragVisual = CreateDragVisual(dragged);
        DragLayer.Children.Add(_dragVisual);

        var pos = Mouse.GetPosition(DragLayer);
        Canvas.SetLeft(_dragVisual, pos.X - _dragVisual.Width / 2);
        Canvas.SetTop(_dragVisual, pos.Y - _dragVisual.Height / 2);

        // 立即点亮格子分割线，即使鼠标暂时在网格外也能看到可放置提示
        HighlightTarget();
        UpdateDropTarget(Mouse.GetPosition(ItemGrid));
        ResetEdgeZone();
    }

    private FrameworkElement CreateDragVisual(AppItem item) {
        double cellW = ItemGrid.ActualWidth / Cols;
        double cellH = ItemGrid.ActualHeight / Rows;

        return new Border {
            Width = Math.Max(52, cellW * item.SafeColSpan - 10),
            Height = Math.Max(52, cellH * item.SafeRowSpan - 10),
            Background = ThemeManager.Brush(ThemeManager.PopupBackground),
            BorderBrush = ThemeManager.InkBrush(0x33),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Opacity = 0.85,
            Child = CreateIconContent(item)
        };
    }

    private void UpdateDropTarget(Point posInGrid) {
        if (ItemGrid.ActualWidth <= 0 || ItemGrid.ActualHeight <= 0) {
            _dropTarget = null;
            HighlightTarget();
            return;
        }

        if (posInGrid.X < 0 || posInGrid.Y < 0 ||
            posInGrid.X >= ItemGrid.ActualWidth || posInGrid.Y >= ItemGrid.ActualHeight) {
            if (_dropTarget != null) {
                _dropTarget = null;
                HighlightTarget();
            }
            return;
        }

        int colSpan = _dragItem?.SafeColSpan ?? 1;
        int rowSpan = _dragItem?.SafeRowSpan ?? 1;

        int col = Math.Clamp((int)(posInGrid.X / (ItemGrid.ActualWidth / Cols)), 0, Cols - colSpan);
        int row = Math.Clamp((int)(posInGrid.Y / (ItemGrid.ActualHeight / Rows)), 0, Rows - rowSpan);

        var target = (row, col);
        if (_dropTarget != target) {
            _dropTarget = target;
            HighlightTarget();
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (CompleteInteraction(e.GetPosition(this)))
            e.Handled = true;
    }

    /// <summary>
    /// 鼠标松开时的统一出口：区分「拖拽」与「单击」并各自收尾。
    /// 拖拽（仅编辑模式）= 落位/交换；编辑模式单击 = 切换选中；常规模式单击 = 打开程序。
    /// </summary>
    /// <param name="releasePoint">松手位置（本控件坐标系），用于判断是否算一次面板内的单击。</param>
    /// <returns>本次是否消费了这次松手（用于决定是否标记 Handled）。</returns>
    private bool CompleteInteraction(Point releasePoint) {
        // 正在拖拽：无论哪种模式，松手就是落位（非编辑模式下不会进入拖拽）
        if (_isDragging) {
            FinishDrag(applyDrop: true);
            return true;
        }

        // 编辑模式：按下后没有移动过 = 单击切换选中
        if (IsEditMode) {
            var clicked = _dragItem;

            _dragItem = null;
            _dropTarget = null;
            RemoveDragVisual();

            if (Mouse.Captured == this)
                Mouse.Capture(null);

            if (clicked == null)
                return false;

            // 松手点已经离开面板（比如滑到底栏按钮上）就不算单击，别把按钮的点击吞掉
            if (!new Rect(0, 0, ActualWidth, ActualHeight).Contains(releasePoint))
                return false;

            ToggleSelection(clicked);
            return true;
        }

        if (_dragItem == null && _dragVisual == null)
            return false;

        var clickedItem = _dragItem;

        // 小组件是「点一下不做事」的，但也不能重建面板，
        // 否则组件自己的 MouseLeftButtonUp（静音、勾选、点日期）就收不到了
        if (clickedItem is { IsWidget: true }) {
            _dragItem = null;
            _isDragging = false;
            _dropTarget = null;
            RemoveDragVisual();

            if (Mouse.Captured == this)
                Mouse.Capture(null);

            return false;
        }

        FinishDrag(applyDrop: false);

        if (clickedItem != null)
            Launch(clickedItem);

        return true;
    }

    /// <summary>
    /// 打开图标对应的目标（exe / 文件夹 / 文档 / 网址），走系统默认关联。
    /// </summary>
    private void Launch(AppItem item) {
        string target = item.TargetPath;

        if (string.IsNullOrWhiteSpace(target)) {
            Notify($"「{item.Name}」没有可打开的目标。");
            return;
        }

        // 双击的手速很快，拦掉短时间内的重复启动
        long now = Environment.TickCount64;
        if (target.Equals(_lastLaunchPath, StringComparison.OrdinalIgnoreCase) &&
            now - _lastLaunchTick < LaunchDebounceMs) {
            return;
        }

        try {
            var startInfo = new ProcessStartInfo {
                FileName = target,
                UseShellExecute = true   // 交给系统按默认关联打开
            };

            // 便携类程序依赖自己的工作目录，能取到就带上
            string? workDir = SafeGetDirectory(target);
            if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
                startInfo.WorkingDirectory = workDir;

            Process.Start(startInfo);

            _lastLaunchPath = target;
            _lastLaunchTick = now;
        }
        catch (Exception ex) {
            Notify($"无法打开「{item.Name}」。\n{ex.Message}");
        }
    }

    private static string? SafeGetDirectory(string path) {
        try {
            return Path.GetDirectoryName(path);
        }
        catch {
            return null;
        }
    }

    private void Notify(string message) {
        MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// 统一收尾：应用落点 → 移除跟随视觉 → 复位状态与高亮 → 重建网格。
    /// 所有结束路径（鼠标松开、丢失捕获、窗口失焦、按键状态探测）都走这里，保证画面完全复位。
    /// 幂等：重复调用不会有副作用。
    /// </summary>
    private void FinishDrag(bool applyDrop) {
        if (!_isDragging && _dragItem == null && _dragVisual == null)
            return;

        // 先落状态，避免后续 Mouse.Capture(null) 触发的 LostMouseCapture 递归进来
        _isDragging = false;

        // 停止边缘自动翻页计时，隐藏提示
        ResetEdgeZone();

        if (applyDrop && _dropTarget is { } target)
            PerformDrop(target.row, target.col);

        RemoveDragVisual();

        _dragItem = null;
        _dropTarget = null;

        if (Mouse.Captured == this)
            Mouse.Capture(null);

        RemoveEmptyPages();
        RefreshPage();
    }

    /// <summary>
    /// 移除跟随鼠标的拖拽图标：先从父级脱离，再清空拖拽层，杜绝残留残影。
    /// </summary>
    private void RemoveDragVisual() {
        if (_dragVisual != null) {
            if (VisualTreeHelper.GetParent(_dragVisual) is Panel parent)
                parent.Children.Remove(_dragVisual);

            _dragVisual = null;
        }

        if (DragLayer.Children.Count > 0)
            DragLayer.Children.Clear();
    }

    /// <summary>
    /// 鼠标捕获异常丢失（拖到其他窗口上松手、被别的控件抢走捕获等）时兜底结束拖拽。
    /// </summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e) {
        if (!_isDragging && _dragVisual == null)
            return;

        FinishDrag(applyDrop: _isDragging);
    }

    // ===== 宿主窗口兜底 =====
    // 底栏等区域是 HomePage 的兄弟节点，拖拽松手落在那里时 HomePage 自己收不到 MouseUp，
    // 必须由窗口层拦截，否则拖拽状态会一直卡在“进行中”。

    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (_hostWindow != null)
            return;

        _hostWindow = Window.GetWindow(this);
        if (_hostWindow == null)
            return;

        _hostWindow.PreviewMouseUp += OnHostPreviewMouseUp;
        _hostWindow.Deactivated += OnHostDeactivated;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
        if (_hostWindow == null)
            return;

        _hostWindow.PreviewMouseUp -= OnHostPreviewMouseUp;
        _hostWindow.Deactivated -= OnHostDeactivated;
        _hostWindow = null;

        // 卸载时若仍在拖拽，直接丢弃
        ResetEdgeZone();
        FinishDrag(applyDrop: false);
    }

    private void OnHostPreviewMouseUp(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // 窗口层比控件自身更早收到松手事件：拖拽收尾/单击启动都在这里统一处理，
        // 事件被消费后就不会再触发底栏按钮
        if (CompleteInteraction(e.GetPosition(this)))
            e.Handled = true;
    }

    private void OnHostDeactivated(object? sender, EventArgs e) {
        if (!_isDragging && _dragItem == null && _dragVisual == null)
            return;

        // 窗口失焦：放弃本次拖拽，条目回到原位
        FinishDrag(applyDrop: false);
    }

    private void PerformDrop(int row, int col) {
        if (_dragItem == null)
            return;

        var dragged = _dragItem;

        if (dragged.Page == _currentPage && dragged.Row == row && dragged.Col == col)
            return;

        int rowSpan = dragged.SafeRowSpan;
        int colSpan = dragged.SafeColSpan;

        var target = ItemAt(_currentPage, row, col);

        if (target == null) {
            if (RegionFree(_currentPage, row, col, rowSpan, colSpan, dragged)) {
                dragged.Page = _currentPage;
                dragged.Row = row;
                dragged.Col = col;
            }

            return;
        }

        if (ReferenceEquals(target, dragged))
            return;

        // 同尺寸：直接交换（例如把时钟和日期换个位置）
        if (target.Page == _currentPage &&
            target.SafeRowSpan == rowSpan && target.SafeColSpan == colSpan) {
            int oldPage = dragged.Page;
            int oldRow = dragged.Row;
            int oldCol = dragged.Col;

            dragged.Page = target.Page;
            dragged.Row = target.Row;
            dragged.Col = target.Col;

            target.Page = oldPage;
            target.Row = oldRow;
            target.Col = oldCol;
            return;
        }

        // 尺寸不同：能塞进落点就塞，塞不进就找最近的空位
        if (RegionFree(_currentPage, row, col, rowSpan, colSpan, dragged)) {
            dragged.Page = _currentPage;
            dragged.Row = row;
            dragged.Col = col;
        }
        else if (FindFreeSpot(_currentPage, dragged) is { } spot) {
            dragged.Page = _currentPage;
            dragged.Row = spot.Row;
            dragged.Col = spot.Col;
        }
    }

    private void RemoveEmptyPages() {
        var pagesWithItems = _items
            .Select(i => i.Page)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (pagesWithItems.Count == 0) {
            _currentPage = 0;
            return;
        }

        var map = new Dictionary<int, int>();
        for (int i = 0; i < pagesWithItems.Count; i++)
            map[pagesWithItems[i]] = i;

        int oldPage = _currentPage;

        foreach (var item in _items)
            item.Page = map[item.Page];

        if (map.TryGetValue(oldPage, out int newPage)) {
            _currentPage = newPage;
        }
        else {
            int candidate = pagesWithItems
                .Where(p => p <= oldPage)
                .DefaultIfEmpty(pagesWithItems[0])
                .Max();

            _currentPage = map[candidate];
        }
    }

    // ===== 拖拽边缘自动翻页 =====

    /// <summary>当前已有条目的最大页索引。</summary>
    private int MaxPageIndex => _items.Count == 0 ? 0 : _items.Max(i => i.Page);

    /// <summary>
    /// 根据鼠标在网格内的纵向位置判断是否进入上/下边缘停留区。
    /// 上边缘 = 上一页，下边缘 = 下一页（最后一页时允许拖出一个新页）。
    /// </summary>
    private void UpdateEdgeZone(Point posInGrid) {
        int dir = 0;
        double height = ItemGrid.ActualHeight;

        if (height > 0) {
            double zone = Math.Clamp(height * 0.12, EdgeZoneMinPx, EdgeZoneMaxPx);

            // 横向允许略微超出网格，避免手一抖就退出边缘区
            bool insideX = posInGrid.X >= -EdgeZoneMaxPx &&
                           posInGrid.X <= ItemGrid.ActualWidth + EdgeZoneMaxPx;

            if (insideX) {
                if (posInGrid.Y < zone)
                    dir = -1;
                else if (posInGrid.Y > height - zone)
                    dir = 1;
            }
        }

        // 翻不动就别提示、别计时
        if (!CanTurnPage(dir))
            dir = 0;

        if (dir == _edgeDir)
            return;

        _edgeDir = dir;
        UpdateEdgeHint();

        if (dir == 0)
            _edgeTimer.Stop();
        else
            _edgeTimer.Start();
    }

    private bool CanTurnPage(int dir) {
        if (dir < 0)
            return _currentPage > 0;

        if (dir > 0)
            return _currentPage <= MaxPageIndex;  // 最多拖出一个新的空页

        return false;
    }

    private void EdgeTimer_Tick(object? sender, EventArgs e) {
        if (!_isDragging || !CanTurnPage(_edgeDir)) {
            ResetEdgeZone();
            return;
        }

        // 翻页：清掉落点、重建网格、再按当前鼠标位置重算高亮
        _currentPage += _edgeDir;
        _dropTarget = null;
        RefreshPage();
        UpdateDropTarget(Mouse.GetPosition(ItemGrid));
    }

    /// <summary>退出边缘区：停止计时并隐藏提示。</summary>
    private void ResetEdgeZone() {
        _edgeTimer.Stop();

        if (_edgeDir == 0)
            return;

        _edgeDir = 0;
        UpdateEdgeHint();
    }

    private void UpdateEdgeHint() {
        FadeEdgeHint(TopEdgeHint, _edgeDir < 0);
        FadeEdgeHint(BottomEdgeHint, _edgeDir > 0);
    }

    private static void FadeEdgeHint(UIElement hint, bool show) {
        hint.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // ===== 滚轮换页 =====

    /// <summary>
    /// 滚轮是否该交给指针下的控件。
    /// 只有「确实还能往这个方向滚」的滚动区、以及文本输入框才让位；
    /// 静态卡片、滑块、行内按钮拿到滚轮也没用，照常翻页，
    /// 否则整页只有一个小组件时（指针必然落在组件上）就翻不动了。
    /// </summary>
    private static bool ShouldDeferWheel(DependencyObject? source, DependencyObject stopAt, int delta) {
        for (var node = source; node != null && !ReferenceEquals(node, stopAt); node = VisualTreeHelper.GetParent(node)) {
            // 输入框只在「正在输入」或「多行」时才需要滚轮；单行且没焦点纯属路过
            if (node is TextBox or PasswordBox) {
                if (node is TextBox textBox && textBox.AcceptsReturn)
                    return true;

                if (node is Control control && control.IsKeyboardFocusWithin)
                    return true;

                continue;
            }

            if (node is ScrollViewer viewer) {
                bool room = delta > 0
                    ? viewer.VerticalOffset > 0.5
                    : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;

                // 滚到头就继续往外层找：让滚动链自然接到翻页上
                if (room)
                    return true;
            }
        }

        return false;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        // 只有真正需要滚动的组件（例如待办列表还有内容）才吃掉滚轮
        if (!_isDragging && ShouldDeferWheel(e.OriginalSource as DependencyObject, this, e.Delta))
            return;

        if (TryScrollPage(e.Delta))
            e.Handled = true;
    }

    /// <summary>
    /// 按滚轮方向翻一页，返回是否真的翻了。
    /// 面板里落在底栏、外边距这些本页之外区域的滚轮，由 MainWindow 转进来。
    /// </summary>
    public bool TryScrollPage(int delta) {
        if (_isDragging) {
            // 拖拽过程中滚轮换页，允许进入一个临时新页
            if (delta > 0)
                _currentPage = Math.Max(0, _currentPage - 1);
            else
                _currentPage++;

            _dropTarget = null;
            RefreshPage();
            UpdateDropTarget(Mouse.GetPosition(ItemGrid));
            return true;
        }

        if (delta > 0) {
            if (_currentPage <= 0)
                return false;

            _currentPage--;
            RefreshPage();
            return true;
        }

        int maxPage = _items.Count == 0 ? 0 : _items.Max(i => i.Page);

        if (_currentPage >= maxPage)
            return false;

        _currentPage++;
        RefreshPage();
        return true;
    }
}
