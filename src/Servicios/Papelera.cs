using System.IO;
using System.Runtime.InteropServices;

namespace MojarraTube;

/// <summary>Enviar archivos a la papelera de reciclaje (se pueden recuperar).</summary>
public static class Papelera
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    public static void Enviar(string ruta)
    {
        const uint FO_DELETE = 3;
        const ushort FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMATION = 0x10, FOF_SILENT = 0x4, FOF_NOERRORUI = 0x400;
        var op = new SHFILEOPSTRUCT { wFunc = FO_DELETE, pFrom = ruta + "\0\0", fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI };
        var r = SHFileOperation(ref op);
        if (r != 0) throw new IOException($"Windows no pudo mover el archivo (código {r}).");
    }
}
