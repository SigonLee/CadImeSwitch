using System;

namespace CadImeSwitch.Core.Ime
{
    /// <summary>
    /// 输入法控制器的门面：负责选择 TSF/IMM32 后端、去抖、尊重用户手动切换。
    /// 所有方法都应在 AutoCAD UI 线程调用（命令事件、WinForms Timer 均满足）。
    /// </summary>
    public sealed class ImeController : IDisposable
    {
        private readonly AppConfig _config;
        private IImeBackend _backend;
        private ImeState _lastDesired = ImeState.Unknown;
        private ImeState _lastApplied = ImeState.Unknown;
        private bool _manualOverride;
        private bool _disposed;
        private DateTime _sessionStartedUtc;

        public string BackendName => _backend?.Name ?? "未初始化";
        public ImeState LastApplied => _lastApplied;
        public ImeState CurrentState
        {
            get
            {
                ImeState cuas = CuasIme.GetState();
                return cuas != ImeState.Unknown ? cuas : (_backend?.GetState() ?? ImeState.Unknown);
            }
        }
        public bool TextSessionActive { get; private set; }
        public TimeSpan TextSessionElapsed =>
            TextSessionActive ? DateTime.UtcNow - _sessionStartedUtc : TimeSpan.Zero;

        public event Action TextSessionStarted;
        public event Action TextSessionEnded;

        // 兼容旧命名（1.1.0 内部曾用 DoubleClickLease）
        public bool DoubleClickLeaseActive => TextSessionActive;

        public ImeController(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize()
        {
            if (_config.UseTsf)
            {
                var tsf = new TsfBackend(_config.ChineseIme);
                if (tsf.Initialize())
                {
                    _backend = tsf;
                    Logger.Info("输入法后端: TSF");
                    return;
                }
                tsf.Dispose();
                Logger.Warn("TSF 初始化失败，尝试 IMM32 兜底");
            }

            if (_config.UseImm32Fallback)
            {
                var imm = new Imm32Backend();
                if (imm.Initialize())
                {
                    _backend = imm;
                    Logger.Info("输入法后端: IMM32");
                    return;
                }
                imm.Dispose();
            }

            Logger.Error("没有可用的输入法后端（TSF / IMM32 均不可用）");
        }

        /// <summary>
        /// 根据当前命令状态应用中文/英文。
        /// </summary>
        /// <param name="chinese">是否需要中文。</param>
        /// <param name="commandName">当前命令名，仅用于日志。</param>
        public void Apply(bool chinese, string commandName)
        {
            if (_disposed || !_config.Enabled) return;
            if (_backend == null) Initialize();
            if (_backend == null) return;

            ImeState desired = chinese ? ImeState.Chinese : ImeState.English;

            if (_lastDesired == desired)
            {
                if (_manualOverride) return;

                // 如果当前实际状态与我们上次设置的不一致，说明用户手动切换过，
                // 本次命令内不再抢回控制权，等下一次命令状态变化再接管。
                ImeState actual = SafeGetState();
                if (actual != ImeState.Unknown && _lastApplied != ImeState.Unknown && actual != _lastApplied)
                {
                    _manualOverride = true;
                    Logger.Info($"检测到用户手动切换输入法（期望={desired}, 实际={actual}），本次命令内不再干预");
                }
                return;
            }

            _lastDesired = desired;
            _manualOverride = false;
            bool ok = desired == ImeState.Chinese ? _backend.SetChinese() : _backend.SetEnglish();

            if (!ok && _backend is TsfBackend && _config.UseImm32Fallback)
            {
                Logger.Warn("TSF 切换失败，尝试 IMM32 兜底");
                var imm = new Imm32Backend();
                if (imm.Initialize())
                {
                    _backend.Dispose();
                    _backend = imm;
                    ok = desired == ImeState.Chinese ? imm.SetChinese() : imm.SetEnglish();
                }
            }

            // 再向前台窗口线程发一次 CUAS 指令，覆盖对话框/命令行的不同 TSF 上下文。
            bool cuas = CuasIme.TrySet(chinese);
            if (cuas) ok = true;

            _lastApplied = ok ? desired : ImeState.Unknown;
            ImeState actualState = SafeGetState();
            Logger.Info($"命令 [{commandName}] -> {(chinese ? "中文" : "英文")} " +
                        $"[后端={_backend.Name}, 结果={(ok ? "成功" : "失败")}, CUAS={cuas}, 实际={actualState}]");
        }

        /// <summary>命令状态变化时重置“手动覆盖”标记。</summary>
        public void ResetForNewCommand()
        {
            _lastDesired = ImeState.Unknown;
            _manualOverride = false;
        }

        /// <summary>测试/手动命令：强制切到指定状态。</summary>
        public bool ForceSet(bool chinese)
        {
            if (_backend == null) Initialize();
            if (_backend == null) return false;
            bool ok = chinese ? _backend.SetChinese() : _backend.SetEnglish();
            bool cuas = CuasIme.TrySet(chinese);
            if (cuas) ok = true;
            ImeState actual = SafeGetState();
            _lastDesired = chinese ? ImeState.Chinese : ImeState.English;
            _lastApplied = ok ? _lastDesired : ImeState.Unknown;
            _manualOverride = false;
            Logger.Info($"强制切换 -> {(chinese ? "中文" : "英文")} ok={ok} CUAS={cuas} 实际={actual}");
            return ok;
        }

        public ImeState SafeGetState()
        {
            try { return CurrentState; }
            catch { return ImeState.Unknown; }
        }

        /// <summary>
        /// 进入“文字会话”：文字命令结束、或双击进入文字编辑时调用。
        /// 会话期间 CommandMonitor 不会因为 CMDNAMES 为空而切回英文；
        /// 由 TextSessionWatcher 根据对话框/caret/焦点/超时结束会话。
        /// </summary>
        public void BeginTextSession(string reason)
        {
            if (_disposed || !_config.Enabled) return;
            if (TextSessionActive) return;

            TextSessionActive = true;
            _sessionStartedUtc = DateTime.UtcNow;
            ForceSet(true);
            Logger.Info("进入文字会话: " + reason);
            try { TextSessionStarted?.Invoke(); } catch { }
        }

        public void EndTextSession(string reason)
        {
            if (!TextSessionActive) return;
            TextSessionActive = false;
            Logger.Info("结束文字会话: " + reason);
            try { TextSessionEnded?.Invoke(); } catch { }
        }

        // 兼容旧命名
        public void EnterDoubleClickLease(string reason) => BeginTextSession(reason);
        public void ExitDoubleClickLease(string reason) => EndTextSession(reason);

        public void Dispose()
        {
            _disposed = true;
            _backend?.Dispose();
            _backend = null;
        }
    }
}
