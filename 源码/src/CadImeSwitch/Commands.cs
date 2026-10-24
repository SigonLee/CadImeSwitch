using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CadImeSwitch.Core;
using CadImeSwitch.Core.Ime;
using CadImeSwitch.UI;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace CadImeSwitch
{
    public static class Commands
    {
        [CommandMethod("IMESWITCH", CommandFlags.Modal)]
        public static void ShowSettings()
        {
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    Plugin.Write("[CAD输入法] 插件尚未初始化");
                    return;
                }

                using (var form = new SettingsForm(plugin.Config))
                {
                    var result = form.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        plugin.Reload();
                        Plugin.Write("[CAD输入法] 设置已保存并生效");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打开设置界面失败", ex);
                Plugin.Write("[CAD输入法] 打开设置界面失败: " + ex.Message);
            }
        }

        [CommandMethod("IMESWITCHSTATUS", CommandFlags.Modal)]
        public static void ShowStatus()
        {
            var p = Plugin.Instance;
            if (p == null) { Plugin.Write("[CAD输入法] 未初始化"); return; }

            string cmdNames = p.Monitor?.GetCmdNames() ?? "";
            Plugin.Write("[CAD输入法] 启用=" + p.Config.Enabled + " 后端=" + p.Ime?.BackendName +
                         " 状态=" + p.Ime?.CurrentState + " CMDNAMES=[" + cmdNames + "]");
            Plugin.Write("[CAD输入法] 双击支持=" + p.Config.EnableDoubleClickSupport +
                         " 文字会话=" + (p.Ime?.TextSessionActive == true ? "活动中" : "无"));
            Plugin.Write("[CAD输入法] 配置=" + AppConfig.ConfigPath);
            Plugin.Write("[CAD输入法] 日志=" + Logger.LogDirectory);
        }

        [CommandMethod("IMESWITCHON", CommandFlags.Modal)]
        public static void TurnOn()
        {
            SetEnabled(true);
        }

        [CommandMethod("IMESWITCHOFF", CommandFlags.Modal)]
        public static void TurnOff()
        {
            SetEnabled(false);
        }

        [CommandMethod("IMESWITCHTOGGLE", CommandFlags.Modal)]
        public static void Toggle()
        {
            var p = Plugin.Instance;
            if (p == null) return;
            SetEnabled(!p.Config.Enabled);
        }

        private static void SetEnabled(bool enabled)
        {
            var p = Plugin.Instance;
            if (p == null) { Plugin.Write("[CAD输入法] 未初始化"); return; }
            p.Config.Enabled = enabled;
            ConfigStore.Save(p.Config);
            p.Monitor?.CheckNow("manual");
            if (!enabled) p.Ime?.ForceSet(false);
            Plugin.Write("[CAD输入法] 已" + (enabled ? "启用" : "停用") + "自动切换");
        }

        [CommandMethod("IMESWITCHTEST", CommandFlags.Modal)]
        public static void Test()
        {
            var p = Plugin.Instance;
            if (p?.Ime == null) { Plugin.Write("[CAD输入法] 未初始化"); return; }

            bool ok = p.Ime.ForceSet(true);
            Plugin.Write("[CAD输入法] 测试：切换到中文 " + (ok ? "成功" : "失败") + "，2.5 秒后恢复英文");

            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                timer.Dispose();
                p.Ime.ForceSet(false);
                Plugin.Write("[CAD输入法] 测试结束，已恢复英文");
            };
            timer.Start();
        }

        [CommandMethod("IMESWITCHDBLCLICKTEST", CommandFlags.Modal)]
        public static void DoubleClickLeaseTest()
        {
            var p = Plugin.Instance;
            if (p?.Ime == null) { Plugin.Write("[CAD输入法] 未初始化"); return; }

            p.Ime.BeginTextSession("手动测试");
            Plugin.Write("[CAD输入法] 已进入文字会话（中文），5 秒后自动结束");

            var timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                timer.Dispose();
                p.Ime.EndTextSession("手动测试结束");
                p.Monitor?.CheckNow("text-session-end", true);
                Plugin.Write("[CAD输入法] 文字会话已结束");
            };
            timer.Start();
        }

        [CommandMethod("IMESWITCHRELOAD", CommandFlags.Modal)]
        public static void Reload()
        {
            try
            {
                Plugin.Instance?.Reload();
                Plugin.Write("[CAD输入法] 配置已重新加载");
            }
            catch (Exception ex)
            {
                Plugin.Write("[CAD输入法] 重新加载失败: " + ex.Message);
            }
        }

        [CommandMethod("IMESWITCHLOG", CommandFlags.Modal)]
        public static void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(Logger.LogDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + Logger.LogDirectory + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Plugin.Write("[CAD输入法] 打开日志目录失败: " + ex.Message);
            }
        }

        [CommandMethod("IMESWITCHPROFILES", CommandFlags.Modal)]
        public static void ListProfiles()
        {
            var list = TsfProfileDiscovery.Discover();
            if (list.Count == 0)
            {
                Plugin.Write("[CAD输入法] 未从注册表读取到 TSF 输入法");
                return;
            }

            Plugin.Write("[CAD输入法] 共发现 " + list.Count + " 个 TSF 输入法：");
            foreach (var p in list.Take(30))
                Plugin.Write("  " + p.Name + "  CLSID=" + p.Clsid +
                             "  Profile=" + p.ProfileGuid + "  Lang=0x" + p.LanguageId.ToString("X4"));
        }

        [CommandMethod("IMESWITCHSELFTEST", CommandFlags.Modal)]
        public static void SelfTest()
        {
            var p = Plugin.Instance;
            var sb = new StringBuilder();
            Action<string> line = s => sb.AppendLine(s);

            line("CadImeSwitch 自检报告");
            line("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            line("AutoCAD: " + AcApp.Version);
            line("进程: " + Environment.ProcessPath);
            line("64 位: " + Environment.Is64BitProcess);
            line("配置: " + AppConfig.ConfigPath);
            line("日志: " + Logger.LogDirectory);
            line("");

            if (p == null)
            {
                line("插件实例: NULL");
            }
            else
            {
                line("启用: " + p.Config.Enabled);
                line("后端: " + p.Ime?.BackendName);
                line("当前状态: " + p.Ime?.CurrentState);
                line("CMDNAMES: [" + (p.Monitor?.GetCmdNames() ?? "") + "]");
                line("中文命令数: " + p.Config.CommandSet.Count);
                line("输入法配置: " + p.Config.ChineseIme.Name + " / " +
                     p.Config.ChineseIme.Clsid + " / " + p.Config.ChineseIme.ProfileGuid);
            }

            line("");
            line("已安装 TSF 输入法:");
            var profiles = TsfProfileDiscovery.Discover();
            if (profiles.Count == 0) line("  (无)");
            foreach (var info in profiles)
                line("  " + info.Name + " | CLSID=" + info.Clsid +
                     " | Profile=" + info.ProfileGuid + " | Lang=0x" + info.LanguageId.ToString("X4"));

            line("");
            line("IMM32 上下文:");
            try
            {
                var main = AcApp.MainWindow.Handle;
                line("  MainWindow=0x" + main.ToInt64().ToString("X"));
                var imm = new Imm32Backend();
                line("  IMM32 可用=" + imm.Initialize());
            }
            catch (Exception ex)
            {
                line("  IMM32 检测异常: " + ex.Message);
            }

            string file = Path.Combine(Logger.LogDirectory,
                "selftest-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            Directory.CreateDirectory(Logger.LogDirectory);
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            Plugin.Write("[CAD输入法] 自检完成: " + file);
        }
    }
}
