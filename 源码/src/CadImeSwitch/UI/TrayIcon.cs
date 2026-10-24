using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CadImeSwitch.Core;

namespace CadImeSwitch.UI
{
    /// <summary>
    /// 系统托盘图标：设置、启用/停用、打开日志、关于。
    /// 图标优先使用 DLL 同目录的 icon.png，找不到时用系统默认图标。
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _enableItem;

        public TrayIcon()
        {
            _menu = new ContextMenuStrip();
            _enableItem = new ToolStripMenuItem("启用自动切换") { CheckOnClick = false };
            _enableItem.Click += (_, __) => ToggleEnabled();

            _menu.Items.Add("设置...", null, (_, __) => ShowSettings());
            _menu.Items.Add(_enableItem);
            _menu.Items.Add("打开日志", null, (_, __) => OpenLog());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("关于", null, (_, __) => ShowAbout());
            _menu.Opening += (_, __) =>
            {
                var p = Plugin.Instance;
                _enableItem.Checked = p?.Config?.Enabled == true;
            };

            _notify = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "CAD 输入法自动切换",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _notify.DoubleClick += (_, __) => ShowSettings();
        }

        public void Dispose()
        {
            try
            {
                _notify.Visible = false;
                _notify.Dispose();
                _menu.Dispose();
            }
            catch { }
        }

        private static void ShowSettings()
        {
            try
            {
                var p = Plugin.Instance;
                if (p == null) return;
                using (var form = new SettingsForm(p.Config))
                {
                    if (form.ShowDialog() == DialogResult.OK) p.Reload();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("托盘打开设置失败", ex);
                MessageBox.Show("打开设置失败：" + ex.Message, "CAD 输入法",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleEnabled()
        {
            try
            {
                var p = Plugin.Instance;
                if (p == null) return;
                p.Config.Enabled = !p.Config.Enabled;
                ConfigStore.Save(p.Config);
                p.Monitor?.CheckNow("tray", true);
                if (!p.Config.Enabled) p.Ime?.ForceSet(false);
                _notify.ShowBalloonTip(1500, "CAD 输入法",
                    "自动切换已" + (p.Config.Enabled ? "启用" : "停用"), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Error("托盘切换启用状态失败", ex);
            }
        }

        private static void OpenLog()
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
            catch { }
        }

        private static void ShowAbout()
        {
            MessageBox.Show(
                "CAD 输入法自动切换 1.3.8\n\n" +
                "AutoCAD 2026 + 天正 T30 V1\n" +
                "文字命令自动中文，命令输入自动英文。\n\n" +
                "配置：" + AppConfig.ConfigPath + "\n" +
                "日志：" + Logger.LogDirectory,
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static Icon LoadIcon()
        {
            try
            {
                string baseDir = Path.GetDirectoryName(typeof(TrayIcon).Assembly.Location);
                string path = Path.Combine(baseDir ?? "", "icon.png");
                if (!File.Exists(path))
                    path = Path.Combine(AppContext.BaseDirectory, "icon.png");
                if (!File.Exists(path))
                {
                    Logger.Info("托盘图标未找到: " + path + "，使用系统默认图标");
                    return SystemIcons.Application;
                }

                using (var bmp = new Bitmap(path))
                {
                    IntPtr h = bmp.GetHicon();
                    try
                    {
                        using (var tmp = Icon.FromHandle(h))
                            return (Icon)tmp.Clone();
                    }
                    finally
                    {
                        DestroyIcon(h);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
