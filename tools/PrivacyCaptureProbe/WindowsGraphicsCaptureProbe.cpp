// Qwen Desktop Controller privacy diagnostic. This exercises Windows Graphics Capture for the
// monitor containing the controller-owned privacy host. It only retains 24x24 sampled pixels in
// memory and writes aggregate statistics to stdout.
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
#include <cstdlib>
#include <vector>

using Microsoft::WRL::ComPtr;
using winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool;
using winrt::Windows::Graphics::Capture::GraphicsCaptureItem;
using winrt::Windows::Graphics::Capture::GraphicsCaptureSession;
using winrt::Windows::Graphics::DirectX::DirectXPixelFormat;
using winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;

struct Rgb { BYTE r; BYTE g; BYTE b; };
struct Sample { std::vector<Rgb> pixels; double variance = 0; };

class HostRestoreGuard
{
public:
    explicit HostRestoreGuard(HWND hwnd) : m_hwnd(hwnd) {}
    void Hide() { ShowWindowAsync(m_hwnd, SW_HIDE); m_hidden = true; }
    ~HostRestoreGuard() { if (m_hidden) ShowWindowAsync(m_hwnd, SW_SHOWNOACTIVATE); }
private:
    HWND m_hwnd;
    bool m_hidden = false;
};

static void EnablePerMonitorDpi()
{
    typedef BOOL(WINAPI* SetProcessDpiAwarenessContextFn)(HANDLE);
    const auto fn = reinterpret_cast<SetProcessDpiAwarenessContextFn>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetProcessDpiAwarenessContext"));
    if (fn) fn(reinterpret_cast<HANDLE>(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
}

static GraphicsCaptureItem CreateMonitorItem(HMONITOR monitor)
{
    const auto interop = winrt::get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
    GraphicsCaptureItem item{ nullptr };
    winrt::check_hresult(interop->CreateForMonitor(monitor,
        winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(), winrt::put_abi(item)));
    return item;
}

static IDirect3DDevice CreateDirect3DDevice(ID3D11Device* d3dDevice)
{
    ComPtr<IDXGIDevice> dxgiDevice;
    winrt::check_hresult(d3dDevice->QueryInterface(IID_PPV_ARGS(&dxgiDevice)));
    winrt::com_ptr<IInspectable> inspectable;
    winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.Get(), inspectable.put()));
    return inspectable.as<IDirect3DDevice>();
}

static HRESULT CaptureFrameSample(ID3D11Device* device, Direct3D11CaptureFramePool const& pool,
    HANDLE frameReady, const RECT& monitorRect, const RECT& hostRect, Sample* result)
{
    if (WaitForSingleObject(frameReady, 2500) != WAIT_OBJECT_0) return HRESULT_FROM_WIN32(WAIT_TIMEOUT);
    const auto frame = pool.TryGetNextFrame();
    if (!frame) return E_FAIL;

    winrt::com_ptr<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess> access =
        frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    ComPtr<ID3D11Texture2D> texture;
    HRESULT hr = access->GetInterface(IID_PPV_ARGS(&texture));
    if (FAILED(hr)) return hr;
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    if (desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB) return E_NOTIMPL;

    D3D11_TEXTURE2D_DESC staging = desc;
    staging.Usage = D3D11_USAGE_STAGING;
    staging.BindFlags = 0;
    staging.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    staging.MiscFlags = 0;
    ComPtr<ID3D11Texture2D> cpuTexture;
    hr = device->CreateTexture2D(&staging, nullptr, &cpuTexture);
    if (FAILED(hr)) return hr;

    ComPtr<ID3D11DeviceContext> context;
    device->GetImmediateContext(&context);
    context->CopyResource(cpuTexture.Get(), texture.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    hr = context->Map(cpuTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) return hr;

    result->pixels.clear();
    constexpr int grid = 24;
    for (int y = 0; y < grid; ++y)
    for (int x = 0; x < grid; ++x)
    {
        const int globalX = hostRect.left + ((2 * x + 1) * (hostRect.right - hostRect.left)) / (2 * grid);
        const int globalY = hostRect.top + ((2 * y + 1) * (hostRect.bottom - hostRect.top)) / (2 * grid);
        const int localX = std::clamp<int>(globalX - monitorRect.left, 0, static_cast<int>(desc.Width) - 1);
        const int localY = std::clamp<int>(globalY - monitorRect.top, 0, static_cast<int>(desc.Height) - 1);
        const BYTE* row = static_cast<const BYTE*>(mapped.pData) + localY * mapped.RowPitch;
        const BYTE* pixel = row + localX * 4;
        result->pixels.push_back({ pixel[2], pixel[1], pixel[0] });
    }
    context->Unmap(cpuTexture.Get(), 0);

    double mean = 0;
    for (const auto& value : result->pixels) mean += (value.r + value.g + value.b) / 3.0;
    mean /= result->pixels.empty() ? 1 : result->pixels.size();
    for (const auto& value : result->pixels)
    {
        const double luminance = (value.r + value.g + value.b) / 3.0;
        result->variance += (luminance - mean) * (luminance - mean);
    }
    result->variance /= result->pixels.empty() ? 1 : result->pixels.size();
    return S_OK;
}

static void DrainQueuedFrames(Direct3D11CaptureFramePool const& pool)
{
    // A frame-pool slot can already contain the visible frame when the host is hidden. Drain
    // before resetting the event so the comparison cannot accidentally use that stale frame twice.
    for (int index = 0; index < 8; ++index)
    {
        const auto frame = pool.TryGetNextFrame();
        if (!frame) return;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=usage\n");
        return 2;
    }

    try
    {
        EnablePerMonitorDpi();
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        const HWND host = reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 0));
        RECT hostRect{};
        if (!host || !IsWindow(host) || !IsWindowVisible(host) || !GetWindowRect(host, &hostRect))
        {
            std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=host-not-visible\n");
            return 3;
        }
        if (!GraphicsCaptureSession::IsSupported())
        {
            std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=not-supported\n");
            return 4;
        }

        const HMONITOR monitor = MonitorFromWindow(host, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo{ sizeof(monitorInfo) };
        if (!GetMonitorInfoW(monitor, &monitorInfo))
        {
            std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=monitor\n");
            return 5;
        }

        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        ComPtr<ID3D11Device> d3dDevice;
        ComPtr<ID3D11DeviceContext> ignoredContext;
        D3D_FEATURE_LEVEL feature{};
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
            D3D11_SDK_VERSION, &d3dDevice, &feature, &ignoredContext);
        if (FAILED(hr))
        {
            std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=d3d11-0x%08X\n", static_cast<unsigned>(hr));
            return 6;
        }

        const auto item = CreateMonitorItem(monitor);
        const auto pool = Direct3D11CaptureFramePool::CreateFreeThreaded(CreateDirect3DDevice(d3dDevice.Get()),
            DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, item.Size());
        const auto session = pool.CreateCaptureSession(item);
        const HANDLE frameReady = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!frameReady) throw winrt::hresult_error(HRESULT_FROM_WIN32(GetLastError()));
        const auto token = pool.FrameArrived([frameReady](auto const&, auto const&) { SetEvent(frameReady); });
        session.StartCapture();

        Sample visible, hidden;
        hr = CaptureFrameSample(d3dDevice.Get(), pool, frameReady, monitorInfo.rcMonitor, hostRect, &visible);
        if (SUCCEEDED(hr))
        {
            DrainQueuedFrames(pool);
            ResetEvent(frameReady);
            HostRestoreGuard restore(host);
            restore.Hide();
            Sleep(120);
            hr = CaptureFrameSample(d3dDevice.Get(), pool, frameReady, monitorInfo.rcMonitor, hostRect, &hidden);
        }
        pool.FrameArrived(token);
        CloseHandle(frameReady);
        session.Close();
        pool.Close();
        if (FAILED(hr) || visible.pixels.size() != hidden.pixels.size())
        {
            std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=capture-0x%08X\n", static_cast<unsigned>(hr));
            return 7;
        }

        double difference = 0;
        for (size_t index = 0; index < visible.pixels.size(); ++index)
        {
            const auto& a = visible.pixels[index]; const auto& b = hidden.pixels[index];
            difference += (std::abs(static_cast<int>(a.r) - b.r) + std::abs(static_cast<int>(a.g) - b.g) + std::abs(static_cast<int>(a.b) - b.b)) / 3.0;
        }
        difference /= visible.pixels.size();
        const wchar_t* verdict = (visible.variance < 6 && hidden.variance < 6) ? L"INCONCLUSIVE"
            : difference <= 4 ? L"INCONCLUSIVE"
            : visible.variance < 6 && hidden.variance >= 6 && difference >= 18 ? L"REDACTED_PLACEHOLDER"
            : difference >= 18 ? L"EXPOSED" : L"INCONCLUSIVE";
        std::wprintf(L"RESULT WindowsGraphicsCapture=%s Difference=%.1f VisibleVariance=%.1f HiddenVariance=%.1f\n",
            verdict, difference, visible.variance, hidden.variance);
        return 0;
    }
    catch (const winrt::hresult_error& error)
    {
        std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=winrt-0x%08X\n", static_cast<unsigned>(error.code()));
        return 8;
    }
    catch (...)
    {
        std::wprintf(L"RESULT WindowsGraphicsCapture=FAILED Detail=unexpected\n");
        return 9;
    }
}
