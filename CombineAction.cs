namespace WidgetNest;

/// <summary>
/// 底栏「组合」按钮当前能做什么：不能组合时整颗按钮隐藏。
/// </summary>
public enum CombineAction {
    /// <summary>当前选择不满足组合条件（不是两个相邻小组件）。</summary>
    None,

    /// <summary>两个相邻小组件还没组合，按钮显示「组合」。</summary>
    Combine,

    /// <summary>选中的正是同一组的两块，按钮显示「拆分」。</summary>
    Split
}
