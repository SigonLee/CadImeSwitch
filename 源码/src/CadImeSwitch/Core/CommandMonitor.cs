using System;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using CadImeSwitch.Core.Ime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CadImeSwitch.Core
{
    /// <summary>
    /// 命令状态监听器。
    /// 主通道：轮询 CMDNAMES（100ms，足够跟手，且不依赖 CAD 版本的事件差异）。
    /// 辅助通道：SystemVariableChanged / DocumentActivated，用于快速响应和文档切换。
    /// </summary>
    public sealed class CommandMonitor : IDisposable
    {
        private readonly AppConfig _config;
        private readonly ImeController _ime;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _started;
        private string _lastCmdNames = string.Empty;
        private string _lastCommandToken = string.Empty;
        private bool? _lastChinese;

        public CommandMonitor(AppConfig config, ImeController ime)
        {
            _config = config;
            _ime = ime;
            _timer = new System.Windows.Forms.Timer { Interval = 50 };
            _timer.Tick += (_, __) => CheckNow("timer");
        }

        public void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                AcApp.SystemVariableChanged += OnSystemVariableChanged;
                AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
            }
            catch (Exception ex)
            {
                Logger.Warn("订阅 CAD 事件失败（将继续使用轮询）: " + ex.Message);
            }

            _timer.Start();
            CheckNow("startup");
            Logger.Info($"命令监听已启动，轮询间隔 {_timer.Interval}ms，中文命令 {_config.CommandSet.Count} 条");
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            _timer.Stop();
            try
            {
                AcApp.SystemVariableChanged -= OnSystemVariableChanged;
                AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
            }
            catch { }
        }

        public void CheckNow(string source, bool force = false)
        {
            try
            {
                string cmdNames = GetCmdNames();
                if (!string.Equals(cmdNames, _lastCmdNames, StringComparison.Ordinal))
                {
                    _lastCmdNames = cmdNames;
                    Logger.Verbose($"CMDNAMES=[{cmdNames}] ({source})");
                }

                bool chinese = IsChineseCommand(cmdNames);
                string token = string.Join(";", CommandCatalog.Split(cmdNames));
                bool commandChanged = !string.Equals(token, _lastCommandToken, StringComparison.OrdinalIgnoreCase);
                if (commandChanged)
                {
                    _lastCommandToken = token;
                    _ime.ResetForNewCommand();
                }

                bool wasChinese = _lastChinese == true;
                bool textCommandEnded = wasChinese && !chinese && string.IsNullOrEmpty(token);

                // 任何文字命令结束都立即切英文：
                // - TEXTEDIT/MTEDIT：退出在位编辑，用户会马上输入命令；
                // - TEXT/TTEXT：放置结束，后续双击会重新进入中文会话。
                // 若天正文字对话框仍在，TextSessionWatcher 会在下一 tick 重新进入会话。
                if (textCommandEnded && !force)
                {
                    _lastChinese = false;
                    _ime.EndTextSession("文字命令结束");
                    _ime.Apply(false, "文字命令结束");
                    return;
                }

                // 文字会话期间，CMDNAMES 可能一直为空；此时不要切回英文。
                if (!chinese && _ime.TextSessionActive && !force)
                {
                    _lastChinese = false;
                    return;
                }

                // 命令提示状态下的英文看门狗：
                // 覆盖 CAD 刚启动、TSF 尚未就绪、上一次会话结束延迟等情况，
                // 确保用户输入第一个命令字符前输入法已经是英文。
                if (!chinese && !_ime.TextSessionActive && string.IsNullOrEmpty(token) && !force)
                {
                    if (_ime.CurrentState == ImeState.Chinese)
                    {
                        _ime.ForceSet(false);
                        _lastChinese = false;
                    }
                }

                // 新命令开始且不是文字命令：结束文字会话。
                if (!chinese && !string.IsNullOrEmpty(token) && _ime.TextSessionActive)
                {
                    _ime.EndTextSession("切换到命令 " + token);
                }

                if (_lastChinese != chinese || commandChanged || force)
                {
                    _lastChinese = chinese;
                    _ime.Apply(chinese, cmdNames);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("命令状态检测失败", ex);
            }
        }

        public string GetCmdNames()
        {
            try
            {
                return (AcApp.GetSystemVariable("CMDNAMES") as string) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool IsChineseCommand(string cmdNames)
        {
            if (string.IsNullOrWhiteSpace(cmdNames)) return false;
            foreach (var token in CommandCatalog.Split(cmdNames))
            {
                if (_config.CommandSet.Contains(token)) return true;
            }
            return false;
        }

        private void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "CMDNAMES", StringComparison.OrdinalIgnoreCase))
                CheckNow("sysvar");
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            _ime.ResetForNewCommand();
            _lastCmdNames = string.Empty;
            _lastChinese = null;
            CheckNow("document");
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
