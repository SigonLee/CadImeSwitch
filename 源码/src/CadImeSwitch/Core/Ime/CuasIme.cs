using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CadImeSwitch.Core.Ime
{
    /// <summary>
    /// CUAS / WM_IME_CONTROL 通道。
    /// TSF 的 open/close 是线程级的，AutoCAD 的命令行、文字对话框、在位编辑器
    /// 可能位于不同线程；把 WM_IME_CONTROL 发到前台窗口的默认 IME 窗口，
    /// 由系统转发到该窗口所属线程，能覆盖这些不同上下文。
    /// </summary>
    public static class CuasIme
    {
        private const uint WM_IME_CONTROL = 0x0283;
        private const uint IMC_GETCONVERSIONMODE = 0x0001;
        private const uint IMC_SETCONVERSIONMODE = 0x0002;
        private const uint IMC_GETOPENSTATUS = 0x0005;
        private const uint IMC_SETOPENSTATUS = 0x0006;
        private const uint IME_CMODE_NATIVE = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetConversionStatus(IntPtr hIMC, uint fdwConversion, uint fdwSentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conversion, out uint sentence);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        public static bool TrySet(bool chinese)
        {
            try
            {
                IntPtr hwnd = GetTargetWindow();
                if (hwnd == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) return false;
                IntPtr ime = ImmGetDefaultIMEWnd(hwnd);
                if (ime == IntPtr.Zero) return false;

                if (chinese)
                {
                    // 先切中文转换模式，再打开输入法；顺序对部分 TSF 输入法更稳。
                    Send(ime, IMC_SETCONVERSIONMODE, IME_CMODE_NATIVE);
                    Send(ime, IMC_SETOPENSTATUS, 1);
                }
                else
                {
                    Send(ime, IMC_SETOPENSTATUS, 0);
                }

                ImeState state = GetState();
                bool ok = chinese ? state == ImeState.Chinese : state == ImeState.English;

                if (!ok)
                {
                    // 某些天正/TSF 对话框不响应默认 IME 窗口的 WM_IME_CONTROL，
                    // 直接取焦点窗口的输入上下文再设置一次。
                    IntPtr himc = ImmGetContext(hwnd);
                    if (himc != IntPtr.Zero)
                    {
                        try
                        {
                            ImmSetOpenStatus(himc, chinese);
                            if (chinese)
                                ImmSetConversionStatus(himc, IME_CMODE_NATIVE, 0);
                        }
                        finally
                        {
                            ImmReleaseContext(hwnd, himc);
                        }
                        state = GetState();
                        ok = chinese ? state == ImeState.Chinese : state == ImeState.English;
                    }
                }

                Logger.Verbose("CUAS -> " + (chinese ? "中文" : "英文") +
                               " hwnd=0x" + hwnd.ToInt64().ToString("X") +
                               " ime=0x" + ime.ToInt64().ToString("X") +
                               " 实际=" + state +
                               (ok ? "" : " (未生效)"));
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Verbose("CUAS 切换失败: " + ex.Message);
                return false;
            }
        }

        public static ImeState GetState()
        {
            try
            {
                IntPtr hwnd = GetTargetWindow();
                if (hwnd == IntPtr.Zero) return ImeState.Unknown;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) return ImeState.Unknown;
                IntPtr ime = ImmGetDefaultIMEWnd(hwnd);
                if (ime == IntPtr.Zero) return ImeState.Unknown;

                IntPtr open = Send(ime, IMC_GETOPENSTATUS, 0);
                if (open == IntPtr.Zero)
                {
                    IntPtr himc = ImmGetContext(hwnd);
                    if (himc != IntPtr.Zero)
                    {
                        try
                        {
                            if (!ImmGetOpenStatus(himc)) return ImeState.English;
                            uint convMode;
                            uint sentenceMode;
                            if (ImmGetConversionStatus(himc, out convMode, out sentenceMode) &&
                                (convMode & IME_CMODE_NATIVE) != 0)
                                return ImeState.Chinese;
                        }
                        finally
                        {
                            ImmReleaseContext(hwnd, himc);
                        }
                    }
                    return ImeState.English;
                }

                IntPtr conv = Send(ime, IMC_GETCONVERSIONMODE, 0);
                return (((uint)conv.ToInt64()) & IME_CMODE_NATIVE) != 0
                    ? ImeState.Chinese
                    : ImeState.English;
            }
            catch { return ImeState.Unknown; }
        }

        private static IntPtr GetTargetWindow()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return fg;
            }

            // CAD 刚启动/焦点在启动画面/其他程序时，退回到 CAD 主窗口。
            try
            {
                IntPtr main = Process.GetCurrentProcess().MainWindowHandle;
                if (main != IntPtr.Zero) return main;
            }
            catch { }
            return fg;
        }

        private static IntPtr Send(IntPtr hwnd, uint command, long value)
        {
            IntPtr result;
            IntPtr ok = SendMessageTimeout(hwnd, WM_IME_CONTROL,
                (IntPtr)command, (IntPtr)value,
                SMTO_ABORTIFHUNG, 200, out result);
            return ok == IntPtr.Zero ? IntPtr.Zero : result;
        }
    }
}
