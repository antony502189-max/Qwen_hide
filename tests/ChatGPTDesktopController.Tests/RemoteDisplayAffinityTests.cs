using ChatGPTDesktopController;
using Xunit;

namespace ChatGPTDesktopController.Tests;

public sealed class RemoteDisplayAffinityTests
{
    [Fact]
    public void Exclude_from_capture_constant_is_the_documented_0x11_value()
    {
        Assert.Equal(0x00000011u, PrivacyGuardService.WdaExcludeFromCapture);
    }

    [Fact]
    public void X64_stub_encodes_hwnd_affinity_and_function_using_windows_calling_convention()
    {
        var hwnd = new IntPtr(unchecked((long)0x1122334455667788));
        var function = new IntPtr(unchecked((long)0x8877665544332211));
        var code = RemoteDisplayAffinity.BuildX64CallStub(hwnd, PrivacyGuardService.WdaExcludeFromCapture, function);

        // mov rcx, imm64 (HWND)
        Assert.Equal(0x48, code[0]);
        Assert.Equal(0xB9, code[1]);
        Assert.Equal(hwnd.ToInt64(), BitConverter.ToInt64(code, 2));

        // mov edx, imm32 (DWORD affinity)
        Assert.Equal(0xBA, code[10]);
        Assert.Equal(PrivacyGuardService.WdaExcludeFromCapture, BitConverter.ToUInt32(code, 11));

        // mov rax, imm64 (SetWindowDisplayAffinity)
        Assert.Equal(0x48, code[15]);
        Assert.Equal(0xB8, code[16]);
        Assert.Equal(function.ToInt64(), BitConverter.ToInt64(code, 17));

        // sub rsp,28h; call rax; add rsp,28h; ret
        Assert.Equal(new byte[]
        {
            0x48,0x83,0xEC,0x28,
            0xFF,0xD0,
            0x48,0x83,0xC4,0x28,
            0xC3
        }, code[25..]);
    }
}
