using Microsoft.Web.WebView2.Wpf;

namespace QwenWorkOverlay;
public sealed class QwenPageAdapter
{
    private readonly WebView2 _browser;
    public bool Ready { get; private set; }
    public QwenPageAdapter(WebView2 browser)=>_browser=browser;
    public async Task InitializeAsync()
    {
        var profile=Path.Combine(SettingsService.Root,"WebViewProfile"); Directory.CreateDirectory(profile);
        var env=await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,profile);
        await _browser.EnsureCoreWebView2Async(env);
        _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;
        _browser.CoreWebView2.Settings.AreDevToolsEnabled=true;
        _browser.CoreWebView2.PermissionRequested += (_,e)=> { if(e.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone) e.State=Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow; };
        _browser.CoreWebView2.Navigate("https://qwen.ai/"); Ready=true;
    }
    public async Task InsertClipboardTextAsync(string text)
    {
        var payload=System.Text.Json.JsonSerializer.Serialize(text);
        const string body="(()=>{const e=document.querySelector('textarea,[contenteditable=\"true\"]');if(!e)return false;e.focus();if('value'in e){e.value=__TEXT__;e.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:__TEXT__}));}else{document.execCommand('insertText',false,__TEXT__);}return true;})()";
        await _browser.CoreWebView2.ExecuteScriptAsync(body.Replace("__TEXT__",payload));
    }
    public void PasteClipboard() { _browser.Focus(); Native.keybd_event(Native.VK_CONTROL,0,0,UIntPtr.Zero); Native.keybd_event(Native.VK_V,0,0,UIntPtr.Zero); Native.keybd_event(Native.VK_V,0,Native.KEYEVENTF_KEYUP,UIntPtr.Zero); Native.keybd_event(Native.VK_CONTROL,0,Native.KEYEVENTF_KEYUP,UIntPtr.Zero); }
}
public sealed class QwenAudioAdapter
{
    // Web standards do not expose a native host PCM stream as a MediaStream. The app therefore preserves Qwen's normal getUserMedia flow.
    // The optional native mixer fallback feeds a selected virtual cable, whose paired microphone is chosen in Qwen by the user.
    public string State { get; private set; } = "Normal Qwen getUserMedia preserved; optional virtual-cable fallback available";
    public async Task InitializeAsync(WebView2 browser) => await browser.CoreWebView2.ExecuteScriptAsync("window.__qwoAudioAdapter={version:2,state:'normal-getUserMedia-preserved; optional-virtual-cable-fallback'};true");
}
