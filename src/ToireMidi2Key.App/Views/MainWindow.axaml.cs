using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ToireMidi2Key.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Avalonia 的 NumericUpDown 默认只在点上下箭头时才更新 Value：
        // 手动输入数字后按 Enter、或者点到别处，都不会提交。
        // 这里在窗口层统一兜底，对所有数值框（包括以后新增的）都生效。
        // 注意：KeyDown / PointerPressed 是"冒泡"事件，必须用 Bubble
        //（写 Tunnel 永远不会触发），并用 handledEventsToo 保证子控件已处理时仍能收到。
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // ================= 自绘标题栏 =================

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsOnChromeButton(e.Source)) return;                        // 点在窗口按钮上就不拖动
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsOnChromeButton(e.Source)) return;
        ToggleMaximize();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static bool IsOnChromeButton(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button) return true;
        }
        return false;
    }

    // ================= 数值框提交 =================

    /// <summary>Enter / Tab = 确认输入。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab)) return;
        if (FindNumericUpDown(e.Source as Visual) is { } numeric) CommitNumericText(numeric);
    }

    /// <summary>鼠标点到别处 = 确认输入（把当前焦点所在数值框的内容落下去）。</summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is Visual visual && FindNumericUpDown(visual) is { } numeric)
            CommitNumericText(numeric);
    }

    private static NumericUpDown? FindNumericUpDown(Visual? source)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is NumericUpDown numeric) return numeric;
        }
        return null;
    }

    /// <summary>把输入框文本解析并写回 Value：越界夹到范围内，非法输入还原成原值。</summary>
    private static void CommitNumericText(NumericUpDown numeric)
    {
        TextBox? box = numeric.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (box is null) return;

        string text = (box.Text ?? string.Empty).Trim();
        string currentText = numeric.Value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

        if (text.Length == 0)
        {
            box.Text = currentText;
            return;
        }

        bool parsed = decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value)
                   || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        if (!parsed)
        {
            box.Text = currentText;      // 非法输入：还原，别让框里留着一个假数字
            return;
        }

        if (value < numeric.Minimum) value = numeric.Minimum;
        if (value > numeric.Maximum) value = numeric.Maximum;

        if (numeric.Value != value) numeric.Value = value;

        string normalized = value.ToString(CultureInfo.CurrentCulture);
        if (box.Text != normalized) box.Text = normalized;
    }
}
