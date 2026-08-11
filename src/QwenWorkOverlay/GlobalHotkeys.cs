using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace QwenWorkOverlay;
public sealed class GlobalHotkeys : IDisposable
{
    private readonly IntPtr _hwnd; private readonly HwndSource _source; private readonly Action<int> _action; private readonly RightCtrlStateMachine _rightCtrl=new();
    private const int WM_HOTKEY=0x0312, WM_KEYDOWN=0x100, WM_KEYUP=0x101, MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id,LowLevelKeyboardProc p,IntPtr m,uint t);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h); [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? n); [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    private delegate IntPtr LowLevelKeyboardProc(int n,IntPtr w,IntPtr l); private readonly LowLevelKeyboardProc _hookProc; private readonly IntPtr _hook;
    public event Action<bool>? RightCtrlChanged;
    public GlobalHotkeys(Action<int> action)
    {
        _action=action; _source=new HwndSource(new HwndSourceParameters("QWO_Hotkeys") { Width=0,Height=0,ParentWindow=IntPtr.Zero,WindowStyle=0 }); _hwnd=_source.Handle; _source.AddHook(WndProc);
        // IDs: 1 hide, 2 click, 3 top, 4 privacy, 5 opacity+, 6 opacity-, 7 paste, 8 diag, 9 F6, 10 shift F6
        Reg(1,MOD_CONTROL|MOD_ALT,0x51);Reg(2,MOD_CONTROL|MOD_ALT,0x58);Reg(3,MOD_CONTROL|MOD_ALT,0x54);Reg(4,MOD_CONTROL|MOD_ALT,0x50);Reg(5,MOD_CONTROL|MOD_ALT,0x26);Reg(6,MOD_CONTROL|MOD_ALT,0x28);Reg(7,MOD_CONTROL|MOD_ALT,0x56);Reg(8,MOD_CONTROL|MOD_ALT,0x44);Reg(9,0,0x75);Reg(10,MOD_SHIFT,0x75);
        _hookProc=KeyboardHook; _hook=SetWindowsHookEx(13,_hookProc,GetModuleHandle(null),0);
    }
    private void Reg(int id,int mod,int key)=>RegisterHotKey(_hwnd,id,(uint)mod,(uint)key);
    private IntPtr WndProc(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) { if(msg==WM_HOTKEY){_action(w.ToInt32());handled=true;} return IntPtr.Zero; }
    private IntPtr KeyboardHook(int n,IntPtr w,IntPtr l) { if(n>=0){var vk=Marshal.ReadInt32(l); if(vk==0xA3){if(w.ToInt32()==WM_KEYDOWN&&_rightCtrl.OnDown())RightCtrlChanged?.Invoke(true);else if(w.ToInt32()==WM_KEYUP&&_rightCtrl.OnUp())RightCtrlChanged?.Invoke(false);}}return CallNextHookEx(_hook,n,w,l); }
    public void Dispose(){for(var i=1;i<=10;i++)UnregisterHotKey(_hwnd,i);if(_hook!=IntPtr.Zero)UnhookWindowsHookEx(_hook);_source.RemoveHook(WndProc);_source.Dispose();}
}
