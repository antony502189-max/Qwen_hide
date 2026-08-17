// ChatGPT Classic privacy diagnostic for Windows Graphics Capture (monitor or window item).
// No image/video is persisted. Only a 24x24 in-memory sample and aggregate statistics are printed.
// Monitor mode temporarily hides/restores the target to obtain an underlying-frame control.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cwchar>
#include <vector>

using Microsoft::WRL::ComPtr;
using winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool;
using winrt::Windows::Graphics::Capture::GraphicsCaptureItem;
using winrt::Windows::Graphics::Capture::GraphicsCaptureSession;
using winrt::Windows::Graphics::DirectX::DirectXPixelFormat;
using winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;

struct Rgb { BYTE r, g, b; };
struct Sample { std::vector<Rgb> pixels; double variance=0; double mean=0; double blackRatio=0; };

class RestoreGuard
{
public:
    explicit RestoreGuard(HWND hwnd):m_hwnd(hwnd){}
    void Hide(){ ShowWindowAsync(m_hwnd, SW_HIDE); m_hidden=true; }
    ~RestoreGuard(){ if(m_hidden) ShowWindowAsync(m_hwnd, SW_SHOWNOACTIVATE); }
private: HWND m_hwnd{}; bool m_hidden=false;
};

static void EnablePerMonitorDpi()
{
    using Fn=BOOL(WINAPI*)(HANDLE);
    auto fn=reinterpret_cast<Fn>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetProcessDpiAwarenessContext"));
    if(fn) fn(reinterpret_cast<HANDLE>(-4));
}

static GraphicsCaptureItem CreateMonitorItem(HMONITOR monitor)
{
    auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
    GraphicsCaptureItem item{nullptr};
    winrt::check_hresult(interop->CreateForMonitor(monitor,winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),winrt::put_abi(item)));
    return item;
}

static GraphicsCaptureItem CreateWindowItem(HWND hwnd)
{
    auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
    GraphicsCaptureItem item{nullptr};
    winrt::check_hresult(interop->CreateForWindow(hwnd,winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),winrt::put_abi(item)));
    return item;
}

static IDirect3DDevice CreateDirect3DDevice(ID3D11Device* d3d)
{
    ComPtr<IDXGIDevice> dxgi; winrt::check_hresult(d3d->QueryInterface(IID_PPV_ARGS(&dxgi)));
    winrt::com_ptr<IInspectable> inspectable; winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
    return inspectable.as<IDirect3DDevice>();
}

static HRESULT CaptureSample(ID3D11Device* device, Direct3D11CaptureFramePool const& pool, HANDLE ready,
    const RECT& sourceRect, const RECT& sampleRect, Sample* result)
{
    if(WaitForSingleObject(ready,2500)!=WAIT_OBJECT_0) return HRESULT_FROM_WIN32(WAIT_TIMEOUT);
    auto frame=pool.TryGetNextFrame(); if(!frame) return E_FAIL;
    auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    ComPtr<ID3D11Texture2D> texture; HRESULT hr=access->GetInterface(IID_PPV_ARGS(&texture)); if(FAILED(hr)) return hr;
    D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
    if(desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM_SRGB) return E_NOTIMPL;

    auto staging=desc; staging.Usage=D3D11_USAGE_STAGING; staging.BindFlags=0; staging.CPUAccessFlags=D3D11_CPU_ACCESS_READ; staging.MiscFlags=0;
    ComPtr<ID3D11Texture2D> cpu; hr=device->CreateTexture2D(&staging,nullptr,&cpu); if(FAILED(hr)) return hr;
    ComPtr<ID3D11DeviceContext> ctx; device->GetImmediateContext(&ctx); ctx->CopyResource(cpu.Get(),texture.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{}; hr=ctx->Map(cpu.Get(),0,D3D11_MAP_READ,0,&mapped); if(FAILED(hr)) return hr;

    result->pixels.clear(); result->variance=0; result->mean=0; result->blackRatio=0;
    constexpr int grid=24;
    for(int y=0;y<grid;++y) for(int x=0;x<grid;++x)
    {
        int gx=sampleRect.left+((2*x+1)*(sampleRect.right-sampleRect.left))/(2*grid);
        int gy=sampleRect.top+((2*y+1)*(sampleRect.bottom-sampleRect.top))/(2*grid);
        int lx=std::clamp<int>(gx-sourceRect.left,0,(int)desc.Width-1);
        int ly=std::clamp<int>(gy-sourceRect.top,0,(int)desc.Height-1);
        const BYTE* row=(const BYTE*)mapped.pData+ly*mapped.RowPitch; const BYTE* p=row+lx*4;
        result->pixels.push_back({p[2],p[1],p[0]});
    }
    ctx->Unmap(cpu.Get(),0);
    for(auto const&p:result->pixels){ double l=(p.r+p.g+p.b)/3.0; result->mean+=l; if(p.r<10&&p.g<10&&p.b<10) result->blackRatio+=1; }
    result->mean/=result->pixels.empty()?1:result->pixels.size(); result->blackRatio/=result->pixels.empty()?1:result->pixels.size();
    for(auto const&p:result->pixels){ double l=(p.r+p.g+p.b)/3.0; result->variance+=(l-result->mean)*(l-result->mean); }
    result->variance/=result->pixels.empty()?1:result->pixels.size();
    return S_OK;
}

static void Drain(Direct3D11CaptureFramePool const& pool){ for(int i=0;i<8;++i){ auto frame=pool.TryGetNextFrame(); if(!frame) return; } }

int wmain(int argc,wchar_t**argv)
{
    if(argc!=3){ std::wprintf(L"RESULT WGC=UNSUPPORTED Detail=usage-mode-monitor-or-window-hwnd\n"); return 2; }
    try
    {
        EnablePerMonitorDpi(); winrt::init_apartment(winrt::apartment_type::multi_threaded);
        const bool windowMode=_wcsicmp(argv[1],L"window")==0; const bool monitorMode=_wcsicmp(argv[1],L"monitor")==0;
        if(!windowMode&&!monitorMode){ std::wprintf(L"RESULT WGC=UNSUPPORTED Detail=bad-mode\n"); return 2; }
        HWND target=reinterpret_cast<HWND>(_wcstoui64(argv[2],nullptr,0)); RECT targetRect{};
        if(!target||!IsWindow(target)||!IsWindowVisible(target)||!GetWindowRect(target,&targetRect))
        { std::wprintf(L"RESULT WGC=INCONCLUSIVE Detail=target-not-visible\n"); return 3; }
        if(!GraphicsCaptureSession::IsSupported()){ std::wprintf(L"RESULT WGC=UNSUPPORTED Detail=not-supported\n"); return 4; }

        HMONITOR monitor=MonitorFromWindow(target,MONITOR_DEFAULTTONEAREST); MONITORINFO mi{sizeof(mi)}; GetMonitorInfoW(monitor,&mi);
        GraphicsCaptureItem item=windowMode?CreateWindowItem(target):CreateMonitorItem(monitor);
        UINT flags=D3D11_CREATE_DEVICE_BGRA_SUPPORT; ComPtr<ID3D11Device>d3d; ComPtr<ID3D11DeviceContext>ignored; D3D_FEATURE_LEVEL level{};
        HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&d3d,&level,&ignored);
        if(FAILED(hr)){ std::wprintf(L"RESULT WGC=UNSUPPORTED Detail=d3d11-0x%08X\n",(unsigned)hr); return 5; }

        auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(CreateDirect3DDevice(d3d.Get()),DirectXPixelFormat::B8G8R8A8UIntNormalized,2,item.Size());
        auto session=pool.CreateCaptureSession(item); HANDLE ready=CreateEventW(nullptr,TRUE,FALSE,nullptr); if(!ready) return 6;
        auto token=pool.FrameArrived([ready](auto const&,auto const&){SetEvent(ready);}); session.StartCapture();

        if(windowMode)
        {
            RECT local{0,0,item.Size().Width,item.Size().Height}; Sample protectedWindow;
            hr=CaptureSample(d3d.Get(),pool,ready,local,local,&protectedWindow);
            pool.FrameArrived(token); CloseHandle(ready); session.Close(); pool.Close();
            // A failed/denied capture is not sufficient evidence that WDA caused the failure. Treat it
            // as inconclusive rather than turning an unrelated WGC/runtime error into a privacy PASS.
            if(FAILED(hr)){ std::wprintf(L"RESULT WGC_WINDOW=INCONCLUSIVE_CAPTURE_FAILED Detail=capture-0x%08X\n",(unsigned)hr); return 7; }
            const wchar_t* verdict=(protectedWindow.blackRatio>0.97 && protectedWindow.variance<6)?L"PASS_EXCLUDED_BLACK":L"INCONCLUSIVE_FRAME_RETURNED";
            std::wprintf(L"RESULT WGC_WINDOW=%s Mean=%.1f Variance=%.1f BlackRatio=%.3f\n",verdict,protectedWindow.mean,protectedWindow.variance,protectedWindow.blackRatio);
            return 0;
        }

        Sample protectedVisible,hiddenControl;
        hr=CaptureSample(d3d.Get(),pool,ready,mi.rcMonitor,targetRect,&protectedVisible);
        if(SUCCEEDED(hr))
        {
            Drain(pool); ResetEvent(ready); RestoreGuard restore(target); restore.Hide(); Sleep(140);
            hr=CaptureSample(d3d.Get(),pool,ready,mi.rcMonitor,targetRect,&hiddenControl);
        }
        pool.FrameArrived(token); CloseHandle(ready); session.Close(); pool.Close();
        if(FAILED(hr)||protectedVisible.pixels.size()!=hiddenControl.pixels.size())
        { std::wprintf(L"RESULT WGC_MONITOR=INCONCLUSIVE Detail=capture-0x%08X\n",(unsigned)hr); return 7; }

        double difference=0;
        for(size_t i=0;i<protectedVisible.pixels.size();++i){auto const&a=protectedVisible.pixels[i];auto const&b=hiddenControl.pixels[i];difference+=(std::abs((int)a.r-b.r)+std::abs((int)a.g-b.g)+std::abs((int)a.b-b.b))/3.0;}
        difference/=protectedVisible.pixels.empty()?1:protectedVisible.pixels.size();
        const wchar_t* verdict=L"INCONCLUSIVE";
        if(protectedVisible.blackRatio>0.97&&protectedVisible.variance<6&&hiddenControl.variance>=6&&difference>=18) verdict=L"PASS_EXCLUDED_BLACK";
        else if(difference<=6&&hiddenControl.variance>=6) verdict=L"PASS_EXCLUDED_UNDERLYING";
        else if(difference>=18&&protectedVisible.variance>=6) verdict=L"FAIL_VISIBLE_OR_DIFFERENT_COMPOSITE";
        std::wprintf(L"RESULT WGC_MONITOR=%s Difference=%.1f ProtectedVariance=%.1f HiddenVariance=%.1f ProtectedBlackRatio=%.3f\n",verdict,difference,protectedVisible.variance,hiddenControl.variance,protectedVisible.blackRatio);
        return 0;
    }
    catch(winrt::hresult_error const&e){ std::wprintf(L"RESULT WGC=UNSUPPORTED Detail=winrt-0x%08X\n",(unsigned)e.code().value); return 8; }
    catch(...){ std::wprintf(L"RESULT WGC=INCONCLUSIVE Detail=unexpected\n"); return 9; }
}
