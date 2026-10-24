using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace CadImeSwitch.Core.Ime
{
    public sealed class ImeProfileInfo
    {
        public string Name { get; set; }
        public string Clsid { get; set; }
        public string ProfileGuid { get; set; }
        public int LanguageId { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 从注册表读取已安装的 TSF 输入法配置，避免写死某一台机器的 GUID。
    /// 同时读取 64 位与 32 位视图（搜狗等输入法可能注册在 WOW6432Node）。
    /// </summary>
    public static class TsfProfileDiscovery
    {
        private static readonly string[] LanguageKeys = { "0x00000804", "0x00000409" };

        public static List<ImeProfileInfo> Discover()
        {
            var result = new List<ImeProfileInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var tip = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\CTF\TIP");
                        if (tip == null) continue;

                        foreach (var clsidName in tip.GetSubKeyNames())
                        {
                            using var tipKey = tip.OpenSubKey(clsidName);
                            if (tipKey == null) continue;

                            using var langRoot = tipKey.OpenSubKey("LanguageProfile");
                            if (langRoot == null) continue;

                            foreach (var langName in langRoot.GetSubKeyNames())
                            {
                                if (!LanguageKeys.Contains(langName, StringComparer.OrdinalIgnoreCase)) continue;
                                int langId = ParseLangId(langName);
                                using var langKey = langRoot.OpenSubKey(langName);
                                if (langKey == null) continue;

                                foreach (var profileName in langKey.GetSubKeyNames())
                                {
                                    string key = clsidName + "|" + profileName;
                                    if (!seen.Add(key)) continue;

                                    using var profileKey = langKey.OpenSubKey(profileName);
                                    string desc = profileKey?.GetValue("Description") as string;
                                    if (string.IsNullOrWhiteSpace(desc)) desc = profileKey?.GetValue(null) as string;
                                    if (string.IsNullOrWhiteSpace(desc)) desc = tipKey.GetValue(null) as string;
                                    if (string.IsNullOrWhiteSpace(desc)) desc = profileName;

                                    bool hidden = Convert.ToInt32(profileKey?.GetValue("HiddenInSettingUI") ?? 0) != 0;
                                    if (hidden) continue;

                                    result.Add(new ImeProfileInfo
                                    {
                                        Name = desc.Trim(),
                                        Clsid = clsidName.Trim('{', '}'),
                                        ProfileGuid = profileName.Trim('{', '}'),
                                        LanguageId = langId
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Verbose($"读取 TSF 输入法注册表失败 ({hive}/{view}): {ex.Message}");
                    }
                }
            }

            return result
                .OrderBy(p => p.LanguageId == 0x0804 ? 0 : 1)
                .ThenBy(p => IsSogou(p.Name) ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static bool IsSogou(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   (name.IndexOf("搜狗", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    name.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static ImeProfileInfo FindByName(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;
            var list = Discover();
            return list.FirstOrDefault(p =>
                       p.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0)
                   ?? list.FirstOrDefault(p =>
                       p.Name.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static int ParseLangId(string key)
        {
            try
            {
                var s = key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? key.Substring(2) : key;
                return Convert.ToInt32(s, 16);
            }
            catch { return 0x0804; }
        }
    }
}
