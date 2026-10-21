using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
namespace EShield.Services;
public static class SignatureService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint Flags, Context;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    public static (string Text, bool TrustedSystem) Inspect(string path)
    {
        if (!System.IO.File.Exists(path))
            return ("Signature unavailable", false);
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(file, ptr, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = ptr, Flags = 0x1010 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0)
                return ("Unsigned or signature not verified", false);
            // WinVerifyTrust above validates the file. Signed PE extraction still
            // requires this legacy entry point; the new loader accepts DER data.
#pragma warning disable SYSLIB0057
            using var signedCertificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var cert = X509CertificateLoader.LoadCertificate(signedCertificate.GetRawCertData());
            var signer = cert.GetNameInfo(X509NameType.SimpleName, false);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar;
            return ($"Valid signature · {signer}", path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) && signer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        }
        catch { return ("Signature unavailable", false); }
        finally { Marshal.DestroyStructure<FileInfo>(ptr); Marshal.FreeHGlobal(ptr); }
    }
}
