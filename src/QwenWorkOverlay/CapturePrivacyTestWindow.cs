using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace QwenWorkOverlay;

public sealed class CapturePrivacyTestWindow : Window
{
    private readonly TextBlock _result = new() { Margin = new Thickness(16, 8, 16, 16), TextWrapping = TextWrapping.Wrap };
    private readonly CaptureProtectionService _protection = new();

    public CapturePrivacyTestWindow()
    {
        Title = "Capture Privacy Test"; Width = 470; Height = 245; WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Capture Privacy Test", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 12, 12, 4) });
        panel.Children.Add(new TextBlock { Text = "This test window requests WDA_EXCLUDEFROMCAPTURE. Start a supported Windows capture tool and verify that this window is omitted. The API result below is genuine; capture software may choose a pipeline that does not honor it.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 4) });
        panel.Children.Add(_result); Content = panel; Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var enabled = _protection.Set(hwnd, true);
        _result.Text = enabled ? "API result: ON — test this window with your capture software." : "API result: FAILED — Windows rejected capture exclusion for this window.";
    }
}
