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

        // NumericUpDown 默认只在点上下箭头时才更新 Value（手输后按 Enter / 点到别处都不提交），所以在窗口层统一兜底；KeyDown / PointerPressed 是冒泡事件，AddHandler 必须用 RoutingStrategies.Bubble（写 Tunnel 永不触发）并加 handledEventsToo: true
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsOnChromeButton(e.Source)) return;
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

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab)) return;
        if (FindNumericUpDown(e.Source as Visual) is { } numeric) CommitNumericText(numeric);
    }

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
            box.Text = currentText;
            return;
        }

        if (value < numeric.Minimum) value = numeric.Minimum;
        if (value > numeric.Maximum) value = numeric.Maximum;

        if (numeric.Value != value) numeric.Value = value;

        string normalized = value.ToString(CultureInfo.CurrentCulture);
        if (box.Text != normalized) box.Text = normalized;
    }
}
