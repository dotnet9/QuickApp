using System;
using System.IO;

namespace QuickApp.Platform.Windows;

/// <summary>
/// Resolves a shortcut's icon resource without asking Shell for a composed shortcut icon.
/// Calls the stable Shell Link COM ABI directly, avoiding runtime COM interop in NativeAOT.
/// </summary>
internal static unsafe class ShellLinkIconResolver
{
    private const int MaxPath = 260;
    private const int RpcChangedMode = unchecked((int)0x80010106);

    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterfaceId = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterfaceId = new("0000010B-0000-0000-C000-000000000046");

    internal static bool TryGetIconResource(string shortcutPath, out string iconFile, out int iconIndex)
    {
        iconFile = string.Empty;
        iconIndex = 0;

        int initializeResult = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoInitMultithreaded);
        bool uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != RpcChangedMode)
        {
            return false;
        }

        IntPtr shellLink = IntPtr.Zero;
        IntPtr persistFile = IntPtr.Zero;
        try
        {
            int result = NativeMethods.CoCreateInstance(
                ShellLinkClassId,
                IntPtr.Zero,
                NativeMethods.ClsctxInprocServer,
                ShellLinkInterfaceId,
                out shellLink);
            if (result < 0 || shellLink == IntPtr.Zero)
            {
                return false;
            }

            void** shellLinkVtable = *(void***)shellLink;
            Guid persistFileId = PersistFileInterfaceId;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)shellLinkVtable[0])(
                shellLink, &persistFileId, &persistFile);
            if (result < 0 || persistFile == IntPtr.Zero)
            {
                return false;
            }

            void** persistFileVtable = *(void***)persistFile;
            fixed (char* path = shortcutPath)
            {
                result = ((delegate* unmanaged[Stdcall]<IntPtr, char*, uint, int>)persistFileVtable[5])(
                    persistFile, path, NativeMethods.StgmRead);
            }

            if (result < 0)
            {
                return false;
            }

            char* buffer = stackalloc char[MaxPath];
            new Span<char>(buffer, MaxPath).Clear();
            result = ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr, uint, int>)shellLinkVtable[3])(
                shellLink, buffer, MaxPath, IntPtr.Zero, 0);
            if (result >= 0)
            {
                string targetPath = Environment.ExpandEnvironmentVariables(new string(buffer));
                if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                {
                    iconFile = targetPath;
                    iconIndex = 0;
                    return true;
                }
            }

            new Span<char>(buffer, MaxPath).Clear();
            int resourceIndex = 0;
            uint flags = 0;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int*, uint*, int>)shellLinkVtable[16])(
                shellLink, buffer, MaxPath, &resourceIndex, &flags);
            if (result >= 0)
            {
                string resourcePath = Environment.ExpandEnvironmentVariables(new string(buffer));
                if (!string.IsNullOrWhiteSpace(resourcePath) && File.Exists(resourcePath))
                {
                    iconFile = resourcePath;
                    iconIndex = resourceIndex;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Release(persistFile);
            Release(shellLink);
            if (uninitialize)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    private static void Release(IntPtr comObject)
    {
        if (comObject == IntPtr.Zero)
        {
            return;
        }

        void** vtable = *(void***)comObject;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2])(comObject);
    }
}
