using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CadImeSwitch.Core.Ime
{
    /// <summary>
    /// IMM32 后端，作为老式输入法/非 TSF 场景的兜底。
    /// 注意：AutoCAD 2026 绘图区通常返回 NULL 的 IMM 上下文，这时会自动转到 TSF。
    /// </summary>
    public sealed class Imm32Backend : IImeBackend
    {
        private const uint IME_CMODE_NATIVE = 0x0001;

        public string Name => "IMM32";
        public bool IsAvailable { get; private set; }

        public bool Initialize()
        {
            try
            {
                IntPtr hwnd = GetTargetWindow();
                IntPtr ctx = hwnd == IntPtr.Zero ? IntPtr.Zero : ImmGetContext(hwnd);
                IsAvailable = ctx != IntPtr.Zero;
                if (ctx != IntPtr.Zero) ImmReleaseContext(hwnd, ctx);
                Logger.Info($"IMM32 可用性: {IsAvailable} (hwnd=0x{hwnd.ToInt64():X})");
                return IsAvailable;
            }
            catch (Exception ex)
            {
                Logger.Error("IMM32 初始化失败", ex);
                IsAvailable = false;
                return false;
            }
        }

        public bool SetChinese()
        {
            IntPtr hwnd = GetTargetWindow();
            IntPtr ctx = ImmGetContext(hwnd);
            if (ctx == IntPtr.Zero) return false;
            try
            {
                bool ok = ImmSetOpenStatus(ctx, true);
                // 明确切到中文转换模式（对部分 IMM 输入法有效；搜狗通常忽略该标志）。
                ImmSetConversionStatus(ctx, IME_CMODE_NATIVE, 0);
                Logger.Verbose($"IMM32 -> 中文 ok={ok} hwnd=0x{hwnd.ToInt64():X}");
                return ok;
            }
            finally
            {
                ImmReleaseContext(hwnd, ctx);
            }
        }

        public bool SetEnglish()
        {
            IntPtr hwnd = GetTargetWindow();
            IntPtr ctx = ImmGetContext(hwnd);
            if (ctx == IntPtr.Zero) return false;
            try
            {
                bool ok = ImmSetOpenStatus(ctx, false);
                Logger.Verbose($"IMM32 -> 英文 ok={ok} hwnd=0x{hwnd.ToInt64():X}");
                return ok;
            }
            finally
            {
                ImmReleaseContext(hwnd, ctx);
            }
        }

        public ImeState GetState()
        {
            IntPtr hwnd = GetTargetWindow();
            IntPtr ctx = ImmGetContext(hwnd);
            if (ctx == IntPtr.Zero) return ImeState.Unknown;
            try
            {
                if (!ImmGetOpenStatus(ctx)) return ImeState.English;
                if (!ImmGetConversionStatus(ctx, out uint conv, out _)) return ImeState.Chinese;
                return (conv & IME_CMODE_NATIVE) != 0 ? ImeState.Chinese : ImeState.English;
            }
            finally
            {
                ImmReleaseContext(hwnd, ctx);
            }
        }

        public void Dispose()
        {
        }

        private IntPtr GetContext()
        {
            IntPtr hwnd = GetTargetWindow();
            return hwnd == IntPtr.Zero ? IntPtr.Zero : ImmGetContext(hwnd);
        }

        private IntPtr GetTargetWindow()
        {
            try
            {
                IntPtr main = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle;
                uint pid;
                uint tid = GetWindowThreadProcessId(main, out pid);
                var gti = new GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero)
                    return gti.hwndFocus;
                return main;
            }
            catch
            {
                return GetForegroundWindow();
            }
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conversion, out uint sentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetConversionStatus(IntPtr hIMC, uint conversion, uint sentence);

        #endregion
    }
}
