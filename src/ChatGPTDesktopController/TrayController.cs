using System.Drawing;

namespace ChatGPTDesktopController;

public sealed class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private PrivacyGuardState? _lastPrivacyState;
    private string? _lastPrivacyDetail;

    public TrayController(Action show, Action diagnostics, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show controller", null, (_, _) => show());
        menu.Items.Add("Diagnostics", null, (_, _) => diagnostics());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit safely", null, (_, _) => exit());
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "ChatGPT Classic Controller",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void UpdatePrivacy(PrivacyGuardSnapshot privacy)
    {
        var stateText = privacy.State switch
        {
            PrivacyGuardState.Protected => "privacy protected",
            PrivacyGuardState.Partial => "privacy NOT verified",
            PrivacyGuardState.Failed => "privacy FAILED",
            PrivacyGuardState.Unsupported => "privacy unsupported",
            _ => "privacy waiting"
        };
        var tooltip = "ChatGPT Controller — " + stateText;
        _icon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

        if (_lastPrivacyState == privacy.State && string.Equals(_lastPrivacyDetail, privacy.Detail, StringComparison.Ordinal)) return;
        _lastPrivacyState = privacy.State;
        _lastPrivacyDetail = privacy.Detail;

        if (privacy.State == PrivacyGuardState.Waiting) return;
        _icon.BalloonTipTitle = privacy.State == PrivacyGuardState.Protected
            ? "ChatGPT capture privacy protected"
            : "ChatGPT capture privacy NOT verified";
        _icon.BalloonTipText = privacy.Detail.Length <= 240 ? privacy.Detail : privacy.Detail[..240];
        _icon.ShowBalloonTip(3500);
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
