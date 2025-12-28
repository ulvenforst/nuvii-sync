using System;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Provider;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Handles custom context menu commands for cloud files.
    /// Based on CloudMirror sample ContextMenus.cpp
    ///
    /// Implements IExplorerCommand to provide custom verbs in File Explorer.
    /// </summary>
    [ComVisible(true)]
    [Guid(ShellServiceGuids.ContextMenuHandler)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ContextMenuHandler : IExplorerCommand
    {
        public int GetTitle(IShellItemArray? psiItemArray, out string? ppszName)
        {
            ppszName = "Nuvii Sync Command";
            return HResults.S_OK;
        }

        public int GetIcon(IShellItemArray? psiItemArray, out string? ppszIcon)
        {
            ppszIcon = null; // Use default icon
            return HResults.E_NOTIMPL;
        }

        public int GetToolTip(IShellItemArray? psiItemArray, out string? ppszInfotip)
        {
            ppszInfotip = "Execute Nuvii Sync command on selected files";
            return HResults.S_OK;
        }

        public int GetCanonicalName(out Guid pguidCommandName)
        {
            pguidCommandName = new Guid(ShellServiceGuids.ContextMenuHandler);
            return HResults.S_OK;
        }

        public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pCmdState)
        {
            pCmdState = EXPCMDSTATE.ECS_ENABLED;
            return HResults.S_OK;
        }

        public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
        {
            System.Diagnostics.Trace.WriteLine("ContextMenuHandler.Invoke called");

            try
            {
                if (psiItemArray == null)
                    return HResults.S_OK;

                psiItemArray.GetCount(out var count);

                for (uint i = 0; i < count; i++)
                {
                    psiItemArray.GetItemAt(i, out var shellItem);
                    shellItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);

                    System.Diagnostics.Trace.WriteLine($"Context menu invoked on: {path}");

                    // Set a custom state on the file
                    try
                    {
                        var file = StorageFile.GetFileFromPathAsync(path).AsTask().Result;
                        var prop = new StorageProviderItemProperty
                        {
                            Id = 3,
                            Value = "Custom State",
                            IconResource = "shell32.dll,-16764" // Star icon
                        };
                        StorageProviderItemProperties.SetAsync(file, new[] { prop }).AsTask().Wait();

                        // Notify shell of change
                        ShellNativeMethods.SHChangeNotify(
                            SHCNE.SHCNE_UPDATEITEM,
                            SHCNF.SHCNF_PATH,
                            Marshal.StringToHGlobalUni(path),
                            IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"Error setting property: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ContextMenuHandler.Invoke error: {ex.Message}");
            }

            return HResults.S_OK;
        }

        public int GetFlags(out EXPCMDFLAGS pFlags)
        {
            pFlags = EXPCMDFLAGS.ECF_DEFAULT;
            return HResults.S_OK;
        }

        public int EnumSubCommands(out IEnumExplorerCommand? ppEnum)
        {
            ppEnum = null;
            return HResults.E_NOTIMPL;
        }
    }
}
