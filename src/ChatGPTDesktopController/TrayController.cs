using System.Drawing;

namespace ChatGPTDesktopController;

public sealed class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private string? _lastEffectiveState;
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

    public void UpdatePrivacy(PrivacyGuardSnapshot privacy, PrivacyTransitionSnapshot? primary = null)
    {
        var primaryRequired = primary?.TargetTracked == true;
        var primaryVerified = primary?.PrimaryVerified == true;
        var effectiveProtected = privacy.State == PrivacyGuardState.Protected && (!primaryRequired || primaryVerified);

        var stateText = effectiveProtected
            ? "privacy protected"
            : privacy.State switch
            {
                PrivacyGuardState.Waiting when !primaryRequired => "privacy waiting",
                PrivacyGuardState.Unsupported => "privacy unsupported",
                PrivacyGuardState.Failed => "privacy FAILED",
                _ => "privacy NOT verified"
            };

        var detail = primaryRequired && !primaryVerified
            ? primary!.Detail
            : privacy.Detail;
        var effectiveState = effectiveProtected ? "protected" : stateText;

        var tooltip = "ChatGPT Controller — " + stateText;
        _icon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

        if (string.Equals(_lastEffectiveState, effectiveState, StringComparison.Ordinal) &&
            string.Equals(_lastPrivacyDetail, detail, StringComparison.Ordinal)) return;
        _lastEffectiveState = effectiveState;
        _lastPrivacyDetail = detail;

        if (privacy.State == PrivacyGuardState.Waiting && !primaryRequired) return;
        _icon.BalloonTipTitle = effectiveProtected
            ? "ChatGPT capture privacy protected"
            : "ChatGPT capture privacy NOT verified";
        _icon.BalloonTipText = detail.Length <= 240 ? detail : detail[..240];
        _icon.ShowBalloonTip(3500);
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
