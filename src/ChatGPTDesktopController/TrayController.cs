using System.Drawing;

namespace ChatGPTDesktopController;

public sealed class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
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
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
