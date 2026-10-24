using System;
using CadImeSwitch.Core;
using CadImeSwitch.Core.Ime;
using CadImeSwitch.UI;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;

[assembly: ExtensionApplication(typeof(CadImeSwitch.Plugin))]
[assembly: CommandClass(typeof(CadImeSwitch.Commands))]

namespace CadImeSwitch
{
    /// <summary>
    /// AutoCAD 扩展入口。由 ApplicationPlugins 自动加载，无需 NETLOAD。
    /// </summary>
    public sealed class Plugin : IExtensionApplication
    {
        public static Plugin Instance { get; private set; }

        internal AppConfig Config { get; private set; }
        internal ImeController Ime { get; private set; }
        internal CommandMonitor Monitor { get; private set; }
        internal DoubleClickWatcher DoubleClick { get; private set; }
        internal TrayIcon Tray { get; private set; }

        public void Initialize()
        {
            Instance = this;
            try
            {
                Config = ConfigStore.Load();
                Logger.Initialize(Config.LogVerbose, Config.EnableLogging);
                Logger.Info("配置: " + AppConfig.ConfigPath);
                Logger.Info("启用=" + Config.Enabled + ", TSF=" + Config.UseTsf +
                            ", IMM32=" + Config.UseImm32Fallback +
                            ", 轮询=" + Config.PollIntervalMs + "ms, 中文命令=" + Config.CommandSet.Count);

                Ime = new ImeController(Config);
                Ime.Initialize();

                Monitor = new CommandMonitor(Config, Ime);
                Monitor.Start();

                DoubleClick = new DoubleClickWatcher(Config, Ime, Monitor);
                DoubleClick.Start();

                Tray = new TrayIcon();
            }
            catch (Exception ex)
            {
                Logger.Error("插件初始化失败", ex);
            }
        }

        public void Terminate()
        {
            try
            {
                Tray?.Dispose();
                DoubleClick?.Dispose();
                Monitor?.Dispose();
                Ime?.Dispose();
                Logger.Info("插件已卸载");
            }
            catch { }
            Tray = null;
            DoubleClick = null;
            Monitor = null;
            Ime = null;
            Instance = null;
        }

        /// <summary>设置界面保存后热重载配置，不需要重启 CAD。</summary>
        public void Reload()
        {
            try
            {
                DoubleClick?.Dispose();
                Monitor?.Stop();
                Ime?.Dispose();

                Config = ConfigStore.Load();
                Logger.Initialize(Config.LogVerbose, Config.EnableLogging);
                Ime = new ImeController(Config);
                Ime.Initialize();

                Monitor = new CommandMonitor(Config, Ime);
                Monitor.Start();

                DoubleClick = new DoubleClickWatcher(Config, Ime, Monitor);
                DoubleClick.Start();
                Logger.Info("配置已热重载");
            }
            catch (Exception ex)
            {
                Logger.Error("热重载配置失败", ex);
                throw;
            }
        }

        public static void Write(string message)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\n" + message);
            }
            catch { }
        }
    }
}
