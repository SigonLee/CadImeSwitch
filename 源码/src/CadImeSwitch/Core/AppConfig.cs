using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadImeSwitch.Core
{
    public sealed class ImeProfileConfig
    {
        public string Name { get; set; } = "搜狗拼音";
        public string Clsid { get; set; } = "E7EA138E-69F8-11D7-A6EA-00065B844310";
        public string ProfileGuid { get; set; } = "E7EA138F-69F8-11D7-A6EA-00065B844311";
        public int LanguageId { get; set; } = 0x0804;
    }

    public sealed class AppConfig
    {
        public int ConfigVersion { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public bool UseTsf { get; set; } = true;
        public bool UseImm32Fallback { get; set; } = true;
        public int PollIntervalMs { get; set; } = 100;
        public string EnglishMode { get; set; } = "CloseIme";
        public bool EnableLogging { get; set; } = true;
        public bool LogVerbose { get; set; } = false;
        public bool ShowTrayIcon { get; set; } = false;
        public bool EnableDoubleClickSupport { get; set; } = true;
        public int DoubleClickLeaseTimeoutMs { get; set; } = 180000;
        public int DoubleClickCaretGraceMs { get; set; } = 1500;
        public ImeProfileConfig ChineseIme { get; set; } = new ImeProfileConfig();
        public List<string> ChineseCommands { get; set; } = new List<string>();

        [JsonIgnore]
        public HashSet<string> CommandSet { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CadImeSwitch");

        public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

        public void RebuildCommandSet()
        {
            CommandSet = new HashSet<string>(
                (ChineseCommands ?? new List<string>())
                    .Select(CommandCatalog.Normalize)
                    .Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);
        }

        public AppConfig Clone()
        {
            var json = JsonSerializer.Serialize(this, ConfigStore.JsonOptions);
            return JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions);
        }
    }

    public static class ConfigStore
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(AppConfig.ConfigPath))
                {
                    var json = File.ReadAllText(AppConfig.ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();
                    Normalize(cfg);
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取配置失败，使用默认配置", ex);
            }

            var def = CreateDefault();
            Save(def);
            return def;
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Normalize(config);
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                var json = JsonSerializer.Serialize(config, JsonOptions);
                var tmp = AppConfig.ConfigPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(AppConfig.ConfigPath)) File.Delete(AppConfig.ConfigPath);
                File.Move(tmp, AppConfig.ConfigPath);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败", ex);
                throw;
            }
        }

        public static AppConfig CreateDefault()
        {
            var cfg = new AppConfig
            {
                ChineseCommands = CommandCatalog.DefaultChineseCommands.ToList()
            };
            Normalize(cfg);
            return cfg;
        }

        public static void Normalize(AppConfig cfg)
        {
            if (cfg == null) return;
            if (cfg.PollIntervalMs < 50) cfg.PollIntervalMs = 50;
            if (cfg.PollIntervalMs > 2000) cfg.PollIntervalMs = 2000;
            if (cfg.DoubleClickLeaseTimeoutMs < 5000) cfg.DoubleClickLeaseTimeoutMs = 180000;
            if (cfg.DoubleClickCaretGraceMs < 300) cfg.DoubleClickCaretGraceMs = 1500;
            if (cfg.ChineseCommands == null)
                cfg.ChineseCommands = CommandCatalog.DefaultChineseCommands.ToList();

            // v1.3.4 及更早版本的列表控件事件缺陷可能把命令列表写成只剩 TOBJEDIT，
            // 导致自动切换几乎失效。旧配置做一次迁移恢复默认命令。
            if (cfg.ConfigVersion < 2)
            {
                if (cfg.ChineseCommands.Count <= 1)
                    cfg.ChineseCommands = CommandCatalog.DefaultChineseCommands.ToList();

                // 旧版本在配置为空时按字母序选到了 Microsoft Wubi；
                // 清空后由 TsfBackend 优先解析搜狗拼音。
                if (cfg.ChineseIme != null &&
                    cfg.ChineseIme.Name != null &&
                    cfg.ChineseIme.Name.IndexOf("Wubi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cfg.ChineseIme = new ImeProfileConfig();
                }

                cfg.ConfigVersion = 2;
            }

            if (cfg.ChineseIme == null) cfg.ChineseIme = new ImeProfileConfig();
            if (string.IsNullOrWhiteSpace(cfg.EnglishMode)) cfg.EnglishMode = "CloseIme";
            cfg.RebuildCommandSet();
        }
    }
}
