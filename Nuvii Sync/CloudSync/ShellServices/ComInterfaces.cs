using System;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Shared COM interface definitions used by Shell service providers.
    /// </summary>

    #region Shell Item Interfaces

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemBase
    {
        [PreserveSig]
        int BindToHandler(
            IntPtr pbc,
            [In] ref Guid bhid,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [PreserveSig]
        int GetParent(out IShellItemBase ppsi);

        [PreserveSig]
        int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItemBase psi, uint hint, out int piOrder);
    }

    #endregion

    #region Enums

    public enum SIGDN : uint
    {
        SIGDN_NORMALDISPLAY = 0x00000000,
        SIGDN_PARENTRELATIVEPARSING = 0x80018001,
        SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
        SIGDN_PARENTRELATIVEEDITING = 0x80031001,
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
        SIGDN_FILESYSPATH = 0x80058000,
        SIGDN_URL = 0x80068000
    }

    public enum WTS_ALPHATYPE
    {
        WTSAT_UNKNOWN = 0,
        WTSAT_RGB = 1,
        WTSAT_ARGB = 2
    }

    public enum EXPCMDSTATE
    {
        ECS_ENABLED = 0,
        ECS_DISABLED = 1,
        ECS_HIDDEN = 2,
        ECS_CHECKBOX = 4,
        ECS_CHECKED = 8,
        ECS_RADIOCHECK = 16
    }

    public enum EXPCMDFLAGS
    {
        ECF_DEFAULT = 0x0000,
        ECF_HASSUBCOMMANDS = 0x0001,
        ECF_HASSPLITBUTTON = 0x0002,
        ECF_HIDELABEL = 0x0004,
        ECF_ISSEPARATOR = 0x0008,
        ECF_HASLUASHIELD = 0x0010,
        ECF_SEPARATORBEFORE = 0x0020,
        ECF_SEPARATORAFTER = 0x0040,
        ECF_ISDROPDOWN = 0x0080,
        ECF_TOGGLEABLE = 0x0100,
        ECF_AUTOMENUICONS = 0x0200
    }

    public enum SHCNE : uint
    {
        SHCNE_UPDATEITEM = 0x00002000
    }

    public enum SHCNF : uint
    {
        SHCNF_PATH = 0x0005
    }

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    #endregion

    #region Constants

    public static class BHID
    {
        public static readonly Guid BHID_ThumbnailHandler = new Guid("7b2e650a-8e20-4f4a-b09e-6597afc72fb0");
    }

    public static class HResults
    {
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
        public const int E_NOINTERFACE = unchecked((int)0x80004002);
        public const int E_NOTIMPL = unchecked((int)0x80004001);
    }

    #endregion

    #region Native Methods

    internal static class ShellNativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(SHCNE wEventId, SHCNF uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }

    #endregion

    #region Provider Interfaces

    [ComImport]
    [Guid("e357fccd-a995-4576-b01f-234630154e96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        [PreserveSig]
        int GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
    }

    [ComImport]
    [Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithItem
    {
        [PreserveSig]
        int Initialize(IShellItemBase psi, uint grfMode);
    }

    [ComImport]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerCommand
    {
        [PreserveSig]
        int GetTitle(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string? ppszName);

        [PreserveSig]
        int GetIcon(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string? ppszIcon);

        [PreserveSig]
        int GetToolTip(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string? ppszInfotip);

        [PreserveSig]
        int GetCanonicalName(out Guid pguidCommandName);

        [PreserveSig]
        int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out EXPCMDSTATE pCmdState);

        [PreserveSig]
        int Invoke(IShellItemArray? psiItemArray, IntPtr pbc);

        [PreserveSig]
        int GetFlags(out EXPCMDFLAGS pFlags);

        [PreserveSig]
        int EnumSubCommands(out IEnumExplorerCommand? ppEnum);
    }

    [ComImport]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppvOut);

        [PreserveSig]
        int GetPropertyStore(int flags, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetPropertyDescriptionList([In] ref PROPERTYKEY keyType, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int GetCount(out uint pdwNumItems);

        [PreserveSig]
        int GetItemAt(uint dwIndex, out IShellItemBase ppsi);

        [PreserveSig]
        int EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport]
    [Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumExplorerCommand
    {
        [PreserveSig]
        int Next(uint celt, out IExplorerCommand pUICommand, out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumExplorerCommand ppenum);
    }

    #endregion
}
