// Qwen Desktop Controller privacy diagnostic. It never writes image data: only 24x24 sampled
// pixels are compared in memory and aggregate statistics are printed to stdout.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <vector>

using Microsoft::WRL::ComPtr;

struct Rgb { BYTE r; BYTE g; BYTE b; };
struct Sample { std::vector<Rgb> pixels; double variance = 0; };

static void EnablePerMonitorDpi()
{
    typedef BOOL(WINAPI* SetProcessDpiAwarenessContextFn)(HANDLE);
    auto fn = reinterpret_cast<SetProcessDpiAwarenessContextFn>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetProcessDpiAwarenessContext"));
    if (fn) fn(reinterpret_cast<HANDLE>(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
}

static HRESULT GetOutputForMonitor(ID3D11Device* device, HMONITOR monitor, IDXGIOutput1** output)
{
    ComPtr<IDXGIDevice> dxgiDevice;
    HRESULT hr = device->QueryInterface(IID_PPV_ARGS(&dxgiDevice));
    if (FAILED(hr)) return hr;
    ComPtr<IDXGIAdapter> adapter;
    hr = dxgiDevice->GetAdapter(&adapter);
    if (FAILED(hr)) return hr;

    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIOutput> candidate;
        hr = adapter->EnumOutputs(index, &candidate);
        if (hr == DXGI_ERROR_NOT_FOUND) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        if (FAILED(hr)) return hr;
        DXGI_OUTPUT_DESC desc{};
        if (FAILED(candidate->GetDesc(&desc)) || desc.Monitor != monitor) continue;
        ComPtr<IDXGIOutput1> output1;
        hr = candidate.As(&output1);
        if (SUCCEEDED(hr)) *output = output1.Detach();
        return hr;
    }
}

static HRESULT CaptureSample(ID3D11Device* device, IDXGIOutputDuplication* duplication,
    const RECT& desktopRect, const RECT& hostRect, Sample* result)
{
    ComPtr<IDXGIResource> resource;
    DXGI_OUTDUPL_FRAME_INFO frame{};
    HRESULT hr = duplication->AcquireNextFrame(1000, &frame, &resource);
    if (FAILED(hr)) return hr;

    ComPtr<ID3D11Texture2D> texture;
    hr = resource.As(&texture);
    if (FAILED(hr)) { duplication->ReleaseFrame(); return hr; }
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    if (desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB)
    {
        duplication->ReleaseFrame();
        return E_NOTIMPL;
    }

    D3D11_TEXTURE2D_DESC staging = desc;
    staging.Usage = D3D11_USAGE_STAGING;
    staging.BindFlags = 0;
    staging.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    staging.MiscFlags = 0;
    ComPtr<ID3D11Texture2D> cpuTexture;
    hr = device->CreateTexture2D(&staging, nullptr, &cpuTexture);
    if (SUCCEEDED(hr))
    {
        ComPtr<ID3D11DeviceContext> context;
        device->GetImmediateContext(&context);
        context->CopyResource(cpuTexture.Get(), texture.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(cpuTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(hr))
        {
            result->pixels.clear();
            constexpr int grid = 24;
            for (int y = 0; y < grid; ++y)
            for (int x = 0; x < grid; ++x)
            {
                const int globalX = hostRect.left + ((2 * x + 1) * (hostRect.right - hostRect.left)) / (2 * grid);
                const int globalY = hostRect.top + ((2 * y + 1) * (hostRect.bottom - hostRect.top)) / (2 * grid);
                const int localX = std::clamp<int>(globalX - static_cast<int>(desktopRect.left), 0, static_cast<int>(desc.Width) - 1);
                const int localY = std::clamp<int>(globalY - static_cast<int>(desktopRect.top), 0, static_cast<int>(desc.Height) - 1);
                auto row = reinterpret_cast<const BYTE*>(mapped.pData) + localY * mapped.RowPitch;
                const BYTE* pixel = row + localX * 4;
                result->pixels.push_back({ pixel[2], pixel[1], pixel[0] });
            }
            context->Unmap(cpuTexture.Get(), 0);
        }
    }
    duplication->ReleaseFrame();
    if (FAILED(hr)) return hr;

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

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=usage\n");
        return 2;
    }
    EnablePerMonitorDpi();
    HWND host = reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 0));
    RECT hostRect{};
    if (!host || !IsWindow(host) || !IsWindowVisible(host) || !GetWindowRect(host, &hostRect))
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=host-not-visible\n");
        return 3;
    }

    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> ignoredContext;
    D3D_FEATURE_LEVEL feature{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
        D3D11_SDK_VERSION, &device, &feature, &ignoredContext);
    if (FAILED(hr))
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=d3d11-0x%08X\n", static_cast<unsigned>(hr));
        return 4;
    }
    const HMONITOR monitor = MonitorFromWindow(host, MONITOR_DEFAULTTONEAREST);
    ComPtr<IDXGIOutput1> output;
    hr = GetOutputForMonitor(device.Get(), monitor, &output);
    if (FAILED(hr))
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=output-0x%08X\n", static_cast<unsigned>(hr));
        return 5;
    }
    DXGI_OUTPUT_DESC outputDesc{};
    output->GetDesc(&outputDesc);
    ComPtr<IDXGIOutputDuplication> duplication;
    hr = output->DuplicateOutput(device.Get(), &duplication);
    if (FAILED(hr))
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=duplicate-0x%08X\n", static_cast<unsigned>(hr));
        return 6;
    }

    Sample visible, hidden;
    hr = CaptureSample(device.Get(), duplication.Get(), outputDesc.DesktopCoordinates, hostRect, &visible);
    if (SUCCEEDED(hr))
    {
        ShowWindowAsync(host, SW_HIDE);
        Sleep(120);
        hr = CaptureSample(device.Get(), duplication.Get(), outputDesc.DesktopCoordinates, hostRect, &hidden);
        ShowWindowAsync(host, SW_SHOWNOACTIVATE);
    }
    if (FAILED(hr) || visible.pixels.size() != hidden.pixels.size())
    {
        std::wprintf(L"RESULT DesktopDuplication=FAILED Detail=capture-0x%08X\n", static_cast<unsigned>(hr));
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
    std::wprintf(L"RESULT DesktopDuplication=%s Difference=%.1f VisibleVariance=%.1f HiddenVariance=%.1f\n",
        verdict, difference, visible.variance, hidden.variance);
    return 0;
}
