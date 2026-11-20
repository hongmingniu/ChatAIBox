#if WINDOWS
using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.UI;
namespace ChatAIBox;
public static class NativeWindowAppearance
{
    private static bool attached;private static Window? target;
    private static readonly SubclassCallback callback=WindowMessage;
    private static IntPtr nativeBackdrop;private static bool hostVisible;
#if APP_CHECKS
    public static object Inspect(Window window){var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);GetWindowRect(hwnd,out var rect);var point=new NativePoint();ClientToScreen(hwnd,ref point);return new{caption=(GetWindowLongPtr(hwnd,-16).ToInt64()&0x00c00000L)!=0,topInset=point.Y-rect.Top,leftInset=point.X-rect.Left,hostBackdrop=nativeBackdrop!=IntPtr.Zero&&hostVisible,systemBackdrop=window.SystemBackdrop is not null};}
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
#endif
    
    
    public static bool Apply(Window window,Settings settings,bool dark,FrameworkElement host)
    {
        target=window;var reduced=settings.WindowBackdrop=="opaque"||new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast||(settings.WindowBackdrop=="system"&&!new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled);var custom=!reduced&&Environment.OSVersion.Version.Build<22000&&HostBackdrop(window,host);var glass=custom||!reduced&&DesktopAcrylicController.IsSupported();
        hostVisible=custom;
        if(custom){
            // A null SystemBackdrop restores WinUI's opaque window background over the native glass.
            if(window.SystemBackdrop is not TransparentHostBackdrop)window.SystemBackdrop=new TransparentHostBackdrop();
            HostBackdrop(window,host);
        }else if(glass){
            var keepActive=settings.WindowBackdrop=="acrylic";
            if(window.SystemBackdrop is not ClearDesktopAcrylicBackdrop backdrop||backdrop.KeepActive!=keepActive)window.SystemBackdrop=new ClearDesktopAcrylicBackdrop(keepActive);
        }else window.SystemBackdrop=null;
        var foreground=dark?Color.FromArgb(255,238,232,224):Color.FromArgb(255,37,42,48);window.AppWindow.TitleBar.ButtonForegroundColor=foreground;window.AppWindow.TitleBar.ButtonBackgroundColor=Color.FromArgb(0,0,0,0);window.AppWindow.TitleBar.ButtonInactiveBackgroundColor=Color.FromArgb(0,0,0,0);
        if(!attached){attached=true;var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);SetWindowSubclass(hwnd,callback,1,0);var style=GetWindowLongPtr(hwnd,-16).ToInt64();SetWindowLongPtr(hwnd,-16,new IntPtr((style&~0x00c00000L)|0x000f0000L));var extended=GetWindowLongPtr(hwnd,-20).ToInt64();SetWindowLongPtr(hwnd,-20,new IntPtr(extended&~0x00020201L));SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x37);window.AppWindow.Changed+=(_,e)=>{if(e.DidSizeChange||e.DidPresenterChange)Corners(window);};window.Activated+=(_,_)=>{if(hostVisible){HostBackdrop(window,host);Corners(window);}};}Corners(window);return glass;
    }
    private static bool HostBackdrop(Window window,FrameworkElement host)
    {
        try{
            var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);
            if(nativeBackdrop==IntPtr.Zero){nativeBackdrop=CreateBackdrop(hwnd);if(nativeBackdrop==IntPtr.Zero)return false;window.Closed+=(_,_)=>{if(nativeBackdrop!=IntPtr.Zero){DestroyBackdrop(nativeBackdrop);nativeBackdrop=IntPtr.Zero;}};}
            var accent=new AccentPolicy{State=5};var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());try{Marshal.StructureToPtr(accent,pointer,false);var data=new CompositionData{Attribute=19,Data=pointer,Size=(nuint)Marshal.SizeOf<AccentPolicy>()};return SetWindowCompositionAttribute(hwnd,ref data);}finally{Marshal.FreeHGlobal(pointer);}
        }catch{return false;}
    }
    private static IntPtr WindowMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data)
    {
        if(message==0x0083&&wParam!=IntPtr.Zero){
            if(IsZoomed(hwnd)){var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))Marshal.StructureToPtr(monitor.Work,lParam,false);}
            return IntPtr.Zero;
        }
        if(message==0x0084&&GetWindowRect(hwnd,out var rect)){
            var packed=lParam.ToInt64();var x=(short)(packed&0xffff);var y=(short)((packed>>16)&0xffff);var scale=GetDpiForWindow(hwnd)/96d;var edge=(int)Math.Ceiling(7*scale);
            if(!IsZoomed(hwnd)){var left=x<rect.Left+edge;var right=x>=rect.Right-edge;var top=y<rect.Top+edge;var bottom=y>=rect.Bottom-edge;var hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;if(hit!=0)return new IntPtr(hit);}
            if(y<rect.Top+(int)Math.Round(44*scale)&&x<rect.Right-(int)Math.Round(138*scale))return new IntPtr(2);
        }
        if(message==0x0082)RemoveWindowSubclass(hwnd,callback,id);
        return DefSubclassProc(hwnd,message,wParam,lParam);
    }
    private static void Corners(Window window)
    {
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);var max=window.AppWindow.Presenter is OverlappedPresenter{State:OverlappedPresenterState.Maximized};var value=max?1:2;if(nativeBackdrop!=IntPtr.Zero&&GetWindowRect(hwnd,out var bounds))ResizeBackdrop(nativeBackdrop,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,max?0:(float)(12*GetDpiForWindow(hwnd)/96d),hostVisible?1:0);
        if(Environment.OSVersion.Version.Build>=22000&&DwmSetWindowAttribute(hwnd,33,ref value,4)==0){SetWindowRgn(hwnd,IntPtr.Zero,true);return;}
        if(max){SetWindowRgn(hwnd,IntPtr.Zero,true);return;}if(!GetWindowRect(hwnd,out var r))return;var radius=(int)Math.Round(24*(GetDpiForWindow(hwnd)/96d));var region=CreateRoundRectRgn(0,0,r.Right-r.Left+1,r.Bottom-r.Top+1,radius,radius);if(region!=IntPtr.Zero&&SetWindowRgn(hwnd,region,true)==0)DeleteObject(region);
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("backdrop.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr CreateBackdrop(IntPtr hwnd);
    [DllImport("backdrop.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void ResizeBackdrop(IntPtr backdrop,float width,float height,float radius,int visible);
    [DllImport("backdrop.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void DestroyBackdrop(IntPtr backdrop);
    [StructLayout(LayoutKind.Sequential)]private struct AccentPolicy{public int State;public uint Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)]private struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [DllImport("user32.dll")]private static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate IntPtr SubclassCallback(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool SetWindowSubclass(IntPtr hwnd,SubclassCallback callback,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool RemoveWindowSubclass(IntPtr hwnd,SubclassCallback callback,nuint id);
    [DllImport("comctl32.dll")]private static extern IntPtr DefSubclassProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("user32.dll")]private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
#endif
