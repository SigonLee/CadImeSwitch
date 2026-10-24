using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CadImeSwitch.Core.Ime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace CadImeSwitch.Core
{
    /// <summary>
    /// 天正双击进入“在位编辑”时不会设置 CMDNAMES，命令白名单无法识别。
    /// 这里在消息层捕获双击，立即切中文，并维持一个“中文租约”：
    ///   - 编辑器有 caret / 文本仍被选中时保持中文；
    ///   - caret 消失、超时或下一条命令开始时结束租约。
    /// 这样保留天正原有的双击在位编辑体验。
    /// </summary>
    public sealed class DoubleClickWatcher : IDisposable
    {
        private readonly AppConfig _config;
        private readonly ImeController _ime;
        private readonly CommandMonitor _monitor;
        private readonly System.Windows.Forms.Timer _leaseTimer;
        private readonly System.Windows.Forms.Timer _mouseTimer;

        private DateTime _lastLeftDownUtc = DateTime.MinValue;
        private DateTime _lastMouseDownUtc = DateTime.MinValue;
        private Point _lastLeftDownPoint;
        private Point _lastMouseDownPoint;
        private Point? _doubleClickPoint;
        private bool _mouseDown;
        private DateTime _leaseStartedUtc;
        private DateTime _lastCaretSeenUtc;
        private bool _sawEditor;
        private bool _started;

        public DoubleClickWatcher(AppConfig config, ImeController ime, CommandMonitor monitor)
        {
            _config = config;
            _ime = ime;
            _monitor = monitor;
            _leaseTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _leaseTimer.Tick += (_, __) => OnLeaseTick();
            _mouseTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _mouseTimer.Tick += (_, __) => OnMouseTick();
            _ime.TextSessionStarted += OnSessionStarted;
            _ime.TextSessionEnded += OnSessionEnded;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                if (_config.EnableDoubleClickSupport)
                {
                    AcApp.BeginDoubleClick += OnBeginDoubleClick;
                    AcApp.PreTranslateMessage += OnPreTranslateMessage;
                }
                _leaseTimer.Start();
                _mouseTimer.Start();
                Logger.Info("文字会话监听已启用（双击支持=" + _config.EnableDoubleClickSupport + "）");
            }
            catch (Exception ex)
            {
                Logger.Warn("订阅双击事件失败: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (!_started) return;
            _started = false;
            _leaseTimer.Stop();
            _leaseTimer.Dispose();
            _mouseTimer.Stop();
            _mouseTimer.Dispose();
            _ime.TextSessionStarted -= OnSessionStarted;
            _ime.TextSessionEnded -= OnSessionEnded;
            try
            {
                AcApp.BeginDoubleClick -= OnBeginDoubleClick;
                AcApp.PreTranslateMessage -= OnPreTranslateMessage;
            }
            catch { }
        }

        private void OnSessionStarted()
        {
            _leaseStartedUtc = DateTime.UtcNow;
            _lastCaretSeenUtc = DateTime.UtcNow;
            _sawEditor = false;
            _leaseTimer.Start();
        }

        private void OnSessionEnded()
        {
            // 定时器继续运行，用于发现“工具栏文字图标”等不经过 CMDNAMES 的文字对话框。
            _doubleClickPoint = null;
        }

        private void OnMouseTick()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;

                string cls = GetClassName(fg);
                bool drawingArea = cls.IndexOf("AfxMDIFrame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   cls.IndexOf(":28:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   cls.IndexOf("AfxFrameOrView", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!drawingArea) return;

                bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (down && !_mouseDown)
                {
                    POINT p;
                    if (GetCursorPos(out p))
                    {
                        DateTime now = DateTime.UtcNow;
                        double elapsed = (now - _lastMouseDownUtc).TotalMilliseconds;
                        int dx = Math.Abs(p.X - _lastMouseDownPoint.X);
                        int dy = Math.Abs(p.Y - _lastMouseDownPoint.Y);

                        if (elapsed > 0 && elapsed <= GetDoubleClickTime() && dx <= 4 && dy <= 4)
                        {
                            _doubleClickPoint = new Point(p.X, p.Y);
                            Logger.Info($"鼠标双击: {p.X},{p.Y}, 间隔 {elapsed:F0}ms");
                            _ime.BeginTextSession("鼠标双击");
                        }
                        else if (_ime.TextSessionActive && _doubleClickPoint.HasValue &&
                                 Math.Abs(p.X - _doubleClickPoint.Value.X) > 150)
                        {
                            EndSession("点击到编辑区外");
                        }

                        _lastMouseDownUtc = now;
                        _lastMouseDownPoint = new Point(p.X, p.Y);
                    }
                }
                _mouseDown = down;
            }
            catch (Exception ex)
            {
                Logger.Verbose("鼠标双击检测异常: " + ex.Message);
            }
        }

        private void OnBeginDoubleClick(object sender, BeginDoubleClickEventArgs e)
        {
            try
            {
                if (!_config.EnableDoubleClickSupport) return;
                Logger.Info("BeginDoubleClick 位置=" + e.Location);
                POINT p;
                if (GetCursorPos(out p)) _doubleClickPoint = new Point(p.X, p.Y);
                // 天正双击时光标下的对象识别并不可靠（可能还没选中、自定义对象选择集为空），
                // 只要 CAD 报告了双击，就先进入文字会话，由后续的对话框/caret 判断去留。
                _ime.BeginTextSession("BeginDoubleClick");
            }
            catch (Exception ex)
            {
                Logger.Error("处理 BeginDoubleClick 失败", ex);
            }
        }

        private void OnPreTranslateMessage(object sender, PreTranslateMessageEventArgs e)
        {
            try
            {
                if (!_config.EnableDoubleClickSupport) return;

                // PreTranslateMessageEventArgs.Message 的类型来自 WPF WindowsBase，
                // 与 .NET 8 的 WindowsBase 存在版本冲突，这里用反射读取，避免程序集绑定问题。
                object msg = e.GetType().GetProperty("Message")?.GetValue(e);
                if (msg == null) return;
                Type t = msg.GetType();
                int id = Convert.ToInt32(t.GetField("message")?.GetValue(msg) ?? 0);
                if (id != 0x0201) return; // WM_LBUTTONDOWN

                long lp = Convert.ToInt64(t.GetField("lParam")?.GetValue(msg) ?? 0);
                int x = (short)(lp & 0xFFFF);
                int y = (short)((lp >> 16) & 0xFFFF);
                Point pt = new Point(x, y);

                DateTime now = DateTime.UtcNow;
                double elapsed = (now - _lastLeftDownUtc).TotalMilliseconds;
                int dx = Math.Abs(pt.X - _lastLeftDownPoint.X);
                int dy = Math.Abs(pt.Y - _lastLeftDownPoint.Y);

                if (elapsed > 0 && elapsed <= GetDoubleClickTime() && dx <= 4 && dy <= 4)
                {
                    Logger.Info($"消息层双击: {pt.X},{pt.Y}, 间隔 {elapsed:F0}ms");
                    _ime.BeginTextSession("PreTranslateMessage");
                    _lastLeftDownUtc = DateTime.MinValue;
                }
                else
                {
                    _lastLeftDownUtc = now;
                    _lastLeftDownPoint = pt;
                }
            }
            catch (Exception ex)
            {
                Logger.Verbose("PreTranslateMessage 处理异常: " + ex.Message);
            }
        }

        private void OnLeaseTick()
        {
            try
            {
                if (!_ime.TextSessionActive)
                {
                    // 工具栏“单行文字/多行文字”等可能不经过 CMDNAMES，直接弹文字对话框。
                    if (IsTextDialogActive())
                        _ime.BeginTextSession("文字对话框");
                    return;
                }

                DateTime now = DateTime.UtcNow;
                bool editor = IsEditorActive();
                if (editor)
                {
                    _lastCaretSeenUtc = now;
                    _sawEditor = true;
                }

                double totalMs = (now - _leaseStartedUtc).TotalMilliseconds;
                double sinceEditorMs = (now - _lastCaretSeenUtc).TotalMilliseconds;

                if (totalMs > _config.DoubleClickLeaseTimeoutMs)
                {
                    EndSession("文字会话超时");
                    return;
                }

                if (totalMs <= _config.DoubleClickCaretGraceMs) return;

                if (!_sawEditor)
                {
                    // 命令结束后一直没有等到对话框/编辑器：可能只是取消命令，回到英文。
                    // 等待时间不宜过长，否则用户紧接着输入命令时会被中文截获。
                    if (totalMs > Math.Max(1200, _config.DoubleClickCaretGraceMs))
                        EndSession("未检测到文字编辑器");
                    return;
                }

                // 编辑器曾出现过，现在消失：快速回到英文，避免用户开始输入命令时被中文截获。
                if (!editor && sinceEditorMs > 150)
                    EndSession("文字编辑器已关闭");
            }
            catch (Exception ex)
            {
                Logger.Verbose("文字会话检查异常: " + ex.Message);
            }
        }

        private void EndSession(string reason)
        {
            _leaseTimer.Stop();
            _ime.EndTextSession(reason);
            _monitor.CheckNow("text-session-end", true);
        }

        private bool IsTextDialogActive()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
                if (GetClassName(fg) != "#32770") return false;
                string title = GetWindowText(fg);
                return title.IndexOf("文字", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       title.IndexOf("文本", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       title.IndexOf("单行", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       title.IndexOf("多行", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool IsEditorActive()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(fg, out pid);
                    if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                    {
                        string cls = GetClassName(fg);
                        string title = GetWindowText(fg);
                        if (cls == "#32770") return true; // 天正文字输入等模态对话框
                        if (cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                        if (cls.IndexOf("HwndWrapper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            cls.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) >= 0)
                            return false; // 2026 WPF 命令行，不是在编辑文字
                        if (title.IndexOf("文字", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            title.IndexOf("文本", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }

            return HasCaret();
        }

        private bool IsTextSelected()
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return false;
                var res = doc.Editor.SelectImplied();
                if (res == null || res.Status != PromptStatus.OK || res.Value == null) return false;
                return ContainsText(res.Value);
            }
            catch { return false; }
        }

        private bool IsTextLikeAt(Point3d location)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return false;
                var ed = doc.Editor;

                var implied = ed.SelectImplied();
                if (implied != null && implied.Status == PromptStatus.OK && implied.Value != null &&
                    ContainsText(implied.Value))
                    return true;

                double half = 0.5;
                try { half = Math.Max(0.001, ed.GetCurrentView().Height * 0.005); } catch { }
                var filter = new SelectionFilter(new[] { new TypedValue(0, "*") });
                var p1 = new Point3d(location.X - half, location.Y - half, 0);
                var p2 = new Point3d(location.X + half, location.Y + half, 0);
                var res = ed.SelectCrossingWindow(p1, p2, filter);
                if (res == null || res.Status != PromptStatus.OK || res.Value == null) return false;
                return ContainsText(res.Value);
            }
            catch (Exception ex)
            {
                Logger.Verbose("双击文字识别异常: " + ex.Message);
                return false;
            }
        }

        private bool ContainsText(SelectionSet set)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in set)
                    {
                        if (so == null) continue;
                        var obj = tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (IsTextLike(obj)) return true;
                    }
                    tr.Commit();
                }
            }
            catch { }
            return false;
        }

        private bool IsTextLike(DBObject obj)
        {
            if (obj is DBText || obj is MText || obj is AttributeReference ||
                obj is AttributeDefinition || obj is Dimension || obj is MLeader)
                return true;

            string dxf = obj.GetRXClass()?.DxfName ?? "";
            string name = obj.GetRXClass()?.Name ?? "";
            return dxf.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dxf.StartsWith("TCH_", StringComparison.OrdinalIgnoreCase) && dxf.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HasCaret()
        {
            try
            {
                var main = AcApp.MainWindow.Handle;
                uint pid;
                uint tid = GetWindowThreadProcessId(main, out pid);
                var gti = new GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (!GetGUIThreadInfo(tid, ref gti)) return false;

                bool hasCaret = gti.hwndCaret != IntPtr.Zero ||
                                gti.rcCaret.Right > gti.rcCaret.Left ||
                                gti.rcCaret.Bottom > gti.rcCaret.Top;
                if (!hasCaret) return false;

                string focusClass = GetClassName(gti.hwndFocus);
                if (focusClass.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    focusClass.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // AutoCAD 绘图区窗口类形如 Afx:...:28:...；只有焦点仍在这里，
                // 才认为 caret 是文字在位编辑器的 caret，而不是命令行的 caret。
                if (focusClass.IndexOf("Afx:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    focusClass.IndexOf(":28:", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return false;
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeGetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetWindowText(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            NativeGetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        #region Win32

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassName")]
        private static extern int NativeGetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowText")]
        private static extern int NativeGetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

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
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern bool GetCaretPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        #endregion
    }
}
