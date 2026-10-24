using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace CadImeSwitch.Core.Ime
{
    /// <summary>
    /// TSF（Text Services Framework）后端。搜狗、微信、QQ 拼音等现代输入法都走 TSF，
    /// 这是 AutoCAD 2026 绘图区（Cicero/TSF）下最可靠的切换方式。
    /// </summary>
    public sealed class TsfBackend : IImeBackend
    {
        private readonly ImeProfileConfig _config;
        private ITfThreadMgr _threadMgr;
        private ITfCompartmentMgr _compartmentMgr;
        private ITfInputProcessorProfileMgr _profileMgr;
        private ImeProfileInfo _profile;
        private bool _initialized;
        private bool _available;

        public string Name => "TSF";

        public bool IsAvailable => _available;

        public TsfBackend(ImeProfileConfig config)
        {
            _config = config ?? new ImeProfileConfig();
        }

        public bool Initialize()
        {
            if (_initialized) return _available;
            _initialized = true;

            try
            {
                _profile = ResolveProfile();
                if (_profile == null)
                {
                    Logger.Warn("TSF: 未找到可用的输入法配置");
                    return false;
                }

                _threadMgr = ComNative.Create<ITfThreadMgr>(
                    TsfGuids.CLSID_TF_ThreadMgr, TsfGuids.IID_ITfThreadMgr);
                _threadMgr.Activate(out uint tid);
                _threadMgr.GetGlobalCompartment(out _compartmentMgr);

                _profileMgr = ComNative.Create<ITfInputProcessorProfileMgr>(
                    TsfGuids.CLSID_TF_InputProcessorProfiles,
                    TsfGuids.IID_ITfInputProcessorProfileMgr);

                _available = true;
                Logger.Info($"TSF 初始化成功: {_profile.Name} " +
                            $"(CLSID={_profile.Clsid}, Profile={_profile.ProfileGuid}, Lang=0x{_profile.LanguageId:X4})");

                Logger.Info($"TSF 当前 open/close = {GetOpenCloseText()}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("TSF 初始化失败", ex);
                _available = false;
                return false;
            }
        }

        public bool SetChinese()
        {
            if (!Ensure()) return false;
            try
            {
                Guid clsid = Guid.Parse(_profile.Clsid);
                Guid profile = Guid.Parse(_profile.ProfileGuid);

                try
                {
                    // TF_IPPMF_FORPROCESS：让搜狗在本进程的所有 UI 线程生效，
                    // 覆盖命令行、天正对话框、在位编辑器等不同 TSF 上下文。
                    _profileMgr.ActivateProfile(1, (ushort)_profile.LanguageId,
                        ref clsid, ref profile, IntPtr.Zero, 0x10000000);
                }
                catch (COMException)
                {
                    // 某些输入法/系统不接受 FORPROCESS，退回只激活当前线程。
                    _profileMgr.ActivateProfile(1, (ushort)_profile.LanguageId,
                        ref clsid, ref profile, IntPtr.Zero, 0);
                }

                SetOpenClose(1);
                Logger.Verbose("TSF -> 中文（激活并打开输入法）");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("TSF 切换到中文失败", ex);
                return false;
            }
        }

        public bool SetEnglish()
        {
            if (!Ensure()) return false;
            try
            {
                // 用户选择的策略是“关闭输入法”，保留搜狗为当前输入法，
                // 这样不会改变键盘布局，也不会影响其他程序。
                SetOpenClose(0);
                Logger.Verbose("TSF -> 英文（关闭输入法）");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("TSF 切换到英文失败", ex);
                return false;
            }
        }

        public ImeState GetState()
        {
            if (!Ensure()) return ImeState.Unknown;
            try
            {
                int value = GetOpenClose();
                if (value < 0) return ImeState.Unknown;
                return value != 0 ? ImeState.Chinese : ImeState.English;
            }
            catch
            {
                return ImeState.Unknown;
            }
        }

        public void Dispose()
        {
            try { _threadMgr?.Deactivate(); } catch { }
            _threadMgr = null;
            _compartmentMgr = null;
            _profileMgr = null;
            _available = false;
        }

        private bool Ensure()
        {
            if (!_initialized) Initialize();
            return _available;
        }

        private ImeProfileInfo ResolveProfile()
        {
            var discovered = TsfProfileDiscovery.Discover();

            // 配置里的 GUID 优先，但必须确认它确实安装在本机。
            if (Guid.TryParse(_config.Clsid, out _) && Guid.TryParse(_config.ProfileGuid, out _))
            {
                var configured = discovered.FirstOrDefault(p =>
                    string.Equals(p.Clsid, _config.Clsid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ProfileGuid, _config.ProfileGuid, StringComparison.OrdinalIgnoreCase));
                if (configured != null) return configured;
            }

            // 按名称匹配（如“搜狗”）。
            if (!string.IsNullOrWhiteSpace(_config.Name))
            {
                var byName = discovered.FirstOrDefault(p =>
                    p.Name.IndexOf(_config.Name, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (byName != null)
                {
                    Logger.Warn("配置的输入法 GUID 未安装，按名称回退到: " + byName.Name);
                    return byName;
                }
            }

            // 最后选择第一个中文 TSF 输入法，避免换机器后完全不可用。
            var firstChinese = discovered.FirstOrDefault(p => p.LanguageId == 0x0804);
            if (firstChinese != null)
            {
                Logger.Warn("配置的输入法不可用，回退到: " + firstChinese.Name);
                return firstChinese;
            }

            return new ImeProfileInfo
            {
                Name = string.IsNullOrWhiteSpace(_config.Name) ? "已配置输入法" : _config.Name,
                Clsid = _config.Clsid,
                ProfileGuid = _config.ProfileGuid,
                LanguageId = _config.LanguageId == 0 ? 0x0804 : _config.LanguageId
            };
        }

        private int GetOpenClose()
        {
            Guid guid = TsfGuids.GUID_COMPARTMENT_KEYBOARD_OPENCLOSE;
            _compartmentMgr.GetCompartment(ref guid, out ITfCompartment comp);
            object v = comp.GetValue();
            if (v == null) return -1;
            return Convert.ToInt32(v);
        }

        private void SetOpenClose(int value)
        {
            Guid guid = TsfGuids.GUID_COMPARTMENT_KEYBOARD_OPENCLOSE;
            _compartmentMgr.GetCompartment(ref guid, out ITfCompartment comp);
            comp.SetValue(0, value);
        }

        private string GetOpenCloseText()
        {
            try { return GetOpenClose().ToString(); }
            catch { return "?"; }
        }
    }
}
