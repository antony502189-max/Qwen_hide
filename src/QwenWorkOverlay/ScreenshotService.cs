using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QwenWorkOverlay;
public static class ScreenshotService
{
    public static bool CaptureForegroundToClipboard() { var h=Native.GetForegroundWindow(); if(h==IntPtr.Zero||!Native.GetWindowRect(h,out var r))return false; return Copy(new Rectangle(r.Left,r.Top,Math.Max(1,r.Right-r.Left),Math.Max(1,r.Bottom-r.Top))); }
    public static bool CaptureMonitorToClipboard() { var p=System.Windows.Forms.Cursor.Position; var s=System.Windows.Forms.Screen.FromPoint(p).Bounds; return Copy(new Rectangle(s.Left,s.Top,s.Width,s.Height)); }
    private static bool Copy(Rectangle rect)
    { try { using var bmp=new Bitmap(rect.Width,rect.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(bmp))g.CopyFromScreen(rect.Left,rect.Top,0,0,rect.Size,CopyPixelOperation.SourceCopy);var h=bmp.GetHbitmap();try{var source=Imaging.CreateBitmapSourceFromHBitmap(h,IntPtr.Zero,System.Windows.Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();System.Windows.Clipboard.SetImage(source);return true;}finally{DeleteObject(h);}}catch{return false;} }
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr h);
}
