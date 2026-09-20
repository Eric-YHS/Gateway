$code = @'
using System;
using System.Runtime.InteropServices;

public class DllTest {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    public static void TestLoad(string path) {
        IntPtr h = LoadLibrary(path);
        if (h == IntPtr.Zero) {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine("LoadLibrary FAILED. Error code: " + err);
            Console.WriteLine("Error: " + new System.ComponentModel.Win32Exception(err).Message);
        } else {
            Console.WriteLine("LoadLibrary SUCCESS. Handle: " + h);
            FreeLibrary(h);
        }
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
Write-Host "Testing webengine4.dll (64-bit)..."
[DllTest]::TestLoad("C:\Windows\Microsoft.NET\Framework64\v4.0.30319\webengine4.dll")
Write-Host ""
Write-Host "Testing webengine4.dll (32-bit)..."
[DllTest]::TestLoad("C:\Windows\Microsoft.NET\Framework\v4.0.30319\webengine4.dll")
