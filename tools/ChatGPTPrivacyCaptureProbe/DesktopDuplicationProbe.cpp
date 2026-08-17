// ChatGPT Classic privacy diagnostic. No image data is persisted: only 24x24 samples and aggregate
// statistics are kept in memory. The target is temporarily hidden to obtain an underlying-frame control
// and is restored before exit. Run only against redacted/test content.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>

using Microsoft::WRL::ComPtr;
struct Rgb { BYTE r, g, b; };
struct Sample { std::vector<Rgb> pixels; double variance = 0; };

static void EnablePerMonitorDpi()
{
    using Fn = BOOL(WINAPI*)(HANDLE);
    auto fn = reinterpret_cast<Fn>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetProcessDpiAwarenessContext"));
    if (fn) fn(reinterpret_cast<HANDLE>(-4));
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
    const RECT& desktopRect, const RECT& targetRect, Sample* result, LARGE_INTEGER* presentTime)
{
    ComPtr<IDXGIResource> resource;
    DXGI_OUTDUPL_FRAME_INFO frame{};
    HRESULT hr = duplication->AcquireNextFrame(1500, &frame, &resource);
    if (FAILED(hr)) return hr;
    if (presentTime) *presentTime = frame.LastPresentTime;

    ComPtr<ID3D11Texture2D> texture;
    hr = resource.As(&texture);
    if (FAILED(hr)) { duplication->ReleaseFrame(); return hr; }
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    if (desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB)
    {
        duplication->ReleaseFrame(); return E_NOTIMPL;
    }

    D3D11_TEXTURE2D_DESC staging = desc;
    staging.Usage = D3D11_USAGE_STAGING; staging.BindFlags = 0;
    staging.CPUAccessFlags = D3D11_CPU_ACCESS_READ; staging.MiscFlags = 0;
    ComPtr<ID3D11Texture2D> cpuTexture;
    hr = device->CreateTexture2D(&staging, nullptr, &cpuTexture);
    if (SUCCEEDED(hr))
    {
        ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
        context->CopyResource(cpuTexture.Get(), texture.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(cpuTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(hr))
        {
            result->pixels.clear(); result->variance = 0;
            constexpr int grid = 24;
            for (int y = 0; y < grid; ++y)
            for (int x = 0; x < grid; ++x)
            {
                const int gx = targetRect.left + ((2*x+1)*(targetRect.right-targetRect.left))/(2*grid);
                const int gy = targetRect.top + ((2*y+1)*(targetRect.bottom-targetRect.top))/(2*grid);
                const int lx = std::clamp<int>(gx - desktopRect.left, 0, static_cast<int>(desc.Width)-1);
                const int ly = std::clamp<int>(gy - desktopRect.top, 0, static_cast<int>(desc.Height)-1);
                const BYTE* row = static_cast<const BYTE*>(mapped.pData) + ly * mapped.RowPitch;
                const BYTE* pixel = row + lx * 4;
                result->pixels.push_back({pixel[2], pixel[1], pixel[0]});
            }
            context->Unmap(cpuTexture.Get(), 0);
        }
    }
    duplication->ReleaseFrame();
    if (FAILED(hr)) return hr;

    double mean = 0;
    for (auto const& p : result->pixels) mean += (p.r+p.g+p.b)/3.0;
    mean /= result->pixels.empty() ? 1 : result->pixels.size();
    for (auto const& p : result->pixels) { double l=(p.r+p.g+p.b)/3.0; result->variance += (l-mean)*(l-mean); }
    result->variance /= result->pixels.empty() ? 1 : result->pixels.size();
    return S_OK;
}

static void FlushDwm()
{
    using Fn = HRESULT(WINAPI*)();
    HMODULE module = LoadLibraryW(L"dwmapi.dll");
    auto fn = module ? reinterpret_cast<Fn>(GetProcAddress(module, "DwmFlush")) : nullptr;
    if (fn) fn();
    if (module) FreeLibrary(module);
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) { std::wprintf(L"RESULT DXGI=UNSUPPORTED Detail=usage-hwnd-required\n"); return 2; }
    EnablePerMonitorDpi();
    HWND target = reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 0));
    RECT targetRect{};
    if (!target || !IsWindow(target) || !IsWindowVisible(target) || !GetWindowRect(target, &targetRect))
    { std::wprintf(L"RESULT DXGI=INCONCLUSIVE Detail=target-not-visible\n"); return 3; }

    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context; D3D_FEATURE_LEVEL level{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, &device, &level, &context);
    if (FAILED(hr)) { std::wprintf(L"RESULT DXGI=UNSUPPORTED Detail=d3d11-0x%08X\n", (unsigned)hr); return 4; }

    HMONITOR monitor = MonitorFromWindow(target, MONITOR_DEFAULTTONEAREST);
    ComPtr<IDXGIOutput1> output; hr = GetOutputForMonitor(device.Get(), monitor, &output);
    if (FAILED(hr)) { std::wprintf(L"RESULT DXGI=UNSUPPORTED Detail=output-0x%08X\n", (unsigned)hr); return 5; }
    DXGI_OUTPUT_DESC outputDesc{}; output->GetDesc(&outputDesc);
    ComPtr<IDXGIOutputDuplication> duplication; hr = output->DuplicateOutput(device.Get(), &duplication);
    if (FAILED(hr)) { std::wprintf(L"RESULT DXGI=UNSUPPORTED Detail=duplicate-0x%08X\n", (unsigned)hr); return 6; }

    Sample protectedVisible, hiddenControl; LARGE_INTEGER visiblePresent{}, hiddenPresent{}, hideRequested{};
    hr = CaptureSample(device.Get(), duplication.Get(), outputDesc.DesktopCoordinates, targetRect, &protectedVisible, &visiblePresent);
    if (SUCCEEDED(hr))
    {
        QueryPerformanceCounter(&hideRequested);
        ShowWindowAsync(target, SW_HIDE); FlushDwm(); Sleep(140);
        hr = CaptureSample(device.Get(), duplication.Get(), outputDesc.DesktopCoordinates, targetRect, &hiddenControl, &hiddenPresent);
        ShowWindowAsync(target, SW_SHOWNOACTIVATE); FlushDwm();
    }
    if (FAILED(hr) || protectedVisible.pixels.size() != hiddenControl.pixels.size())
    { std::wprintf(L"RESULT DXGI=INCONCLUSIVE Detail=capture-0x%08X\n", (unsigned)hr); return 7; }
    if (hiddenPresent.QuadPart == 0 || hiddenPresent.QuadPart <= hideRequested.QuadPart)
    { std::wprintf(L"RESULT DXGI=INCONCLUSIVE Detail=stale-pre-hide-frame\n"); return 8; }

    double difference = 0;
    for (size_t i=0;i<protectedVisible.pixels.size();++i)
    {
        auto const& a=protectedVisible.pixels[i]; auto const& b=hiddenControl.pixels[i];
        difference += (std::abs((int)a.r-b.r)+std::abs((int)a.g-b.g)+std::abs((int)a.b-b.b))/3.0;
    }
    difference /= protectedVisible.pixels.empty()?1:protectedVisible.pixels.size();

    const wchar_t* verdict = L"INCONCLUSIVE";
    if (protectedVisible.variance < 6 && hiddenControl.variance >= 6 && difference >= 18)
        verdict = L"PASS_EXCLUDED_PLACEHOLDER";
    else if (difference <= 6 && hiddenControl.variance >= 6)
        verdict = L"PASS_EXCLUDED_UNDERLYING";
    else if (difference >= 18 && protectedVisible.variance >= 6)
        verdict = L"FAIL_VISIBLE_OR_DIFFERENT_COMPOSITE";

    std::wprintf(L"RESULT DXGI=%s Difference=%.1f ProtectedVariance=%.1f HiddenVariance=%.1f\n",
        verdict, difference, protectedVisible.variance, hiddenControl.variance);
    return 0;
}
