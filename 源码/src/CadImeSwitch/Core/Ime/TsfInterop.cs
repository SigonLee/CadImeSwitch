using System;
using System.Runtime.InteropServices;

namespace CadImeSwitch.Core.Ime
{
    internal static class TsfGuids
    {
        public static readonly Guid CLSID_TF_ThreadMgr = new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");
        public static readonly Guid IID_ITfThreadMgr = new Guid("AA80E801-2021-11D2-93E0-0060B067B86E");
        public static readonly Guid CLSID_TF_InputProcessorProfiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");
        public static readonly Guid IID_ITfInputProcessorProfileMgr = new Guid("71C6E74C-0F28-11D8-A82A-00065B84435C");
        public static readonly Guid GUID_COMPARTMENT_KEYBOARD_OPENCLOSE = new Guid("58273AAD-01BB-4164-95C6-755BA0B5162D");
        public static readonly Guid GUID_COMPARTMENT_KEYBOARD_DISABLED = new Guid("71A5B253-1951-466B-9FBC-9C8808FA84F2");
    }

    [ComImport, Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfThreadMgr
    {
        void Activate(out uint ptid);
        void Deactivate();
        void CreateDocumentMgr(out IntPtr ppDim);
        void EnumDocumentMgrs(out IntPtr ppEnum);
        void GetFocus(out IntPtr ppDimFocus);
        void SetFocus(IntPtr pDimFocus);
        void AssociateFocus(IntPtr hwnd, IntPtr pDimNew, out IntPtr ppDimPrev);
        void IsThreadFocus(out bool pfThreadFocus);
        void GetFunctionProvider(ref Guid clsid, out IntPtr ppFuncProv);
        void EnumFunctionProviders(out IntPtr ppEnum);
        void GetGlobalCompartment(out ITfCompartmentMgr ppCompMgr);
    }

    [ComImport, Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartmentMgr
    {
        void GetCompartment(ref Guid guid, out ITfCompartment ppComp);
        void ClearCompartment(uint tid, ref Guid guid);
        void EnumCompartments(out IntPtr ppEnum);
    }

    [ComImport, Guid("BB08F7A9-607A-4384-8623-056892B64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartment
    {
        void SetValue(uint tid, [MarshalAs(UnmanagedType.Struct)] object pvarValue);
        [return: MarshalAs(UnmanagedType.Struct)] object GetValue();
    }

    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfInputProcessorProfileMgr
    {
        void ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
        void DeactivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
        void GetProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, IntPtr pProfile);
        void EnumProfiles(ushort langid, out IntPtr ppEnum);
        void ReleaseInputProcessor(ref Guid clsid, uint dwFlags);
        void RegisterProfile(ref Guid clsid, ushort langid, string pchDesc, uint cchDesc, string pchIconFile, uint cchFile, uint uIconIndex, IntPtr hkl, ref Guid guidProfile, uint dwPreferredLayout, uint dwEnabledByDefault, out uint pdwCookie);
        void UnregisterProfile(ref Guid clsid, ushort langid, ref Guid guidProfile, uint dwFlags);
        void GetActiveProfile(ref Guid rclsid, IntPtr pProfile);
    }

    internal static class ComNative
    {
        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint clsctx, ref Guid iid, out IntPtr obj);

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pv, uint coinit);

        public const uint CLSCTX_INPROC_SERVER = 1;

        public static T Create<T>(Guid clsid, Guid iid) where T : class
        {
            IntPtr p;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out p);
            if (hr != 0) throw new COMException("CoCreateInstance 失败", hr);
            return (T)Marshal.GetObjectForIUnknown(p);
        }
    }
}
