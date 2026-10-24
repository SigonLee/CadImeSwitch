using System;
using System.Collections.Generic;
using System.Linq;

namespace CadImeSwitch.Core
{
    /// <summary>
    /// 需要自动切换为中文的命令清单。
    /// 命令名不是越多越好：只放“会进入文字/标注/表格编辑”的命令，
    /// 否则会影响正常绘图命令的英文快捷键输入。
    /// </summary>
    public static class CommandCatalog
    {
        /// <summary>AutoCAD 原生文字、标注、表格类命令。</summary>
        public static readonly string[] CadPreset =
        {
            "TEXT", "DTEXT", "MTEXT", "MTEXTEDIT", "DDEDIT", "TEDIT", "TEXTEDIT", "MTEDIT",
            "TOBJEDIT", "ATTDEF", "ATTEDIT", "EATTEDIT", "BATTMAN",
            "MLEADER", "MLEADEDITOR", "MLEADEREDIT", "QLEADER", "LEADER",
            "DIMEDIT", "DIMTEDIT", "TOLERANCE",
            "TABLE", "TABLEDIT", "TBLEDIT",
            "SPEDIT", "FIND", "STYLE", "FIELD"
        };

        /// <summary>天正 T30 文字/表格/标注类命令（从天正菜单 TCH.TMN 中提取）。</summary>
        public static readonly string[] TianzhengPreset =
        {
            "TTEXT", "TMTEXT", "ZYWZ", "TWORDLIB",
            "TOBJEDIT", "TCH_OBJEDIT",
            "TCELLEDIT", "TSHEETEDIT",
            "TCHDIMTEXT", "GXWZ",
            "TREPFIND", "TCH_TEXT", "TCH_TEXT2", "TCH_MTEXT",
            "TEXTEDIT"
        };

        public static readonly string[] DefaultChineseCommands =
            CadPreset.Concat(TianzhengPreset)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                     .ToArray();

        /// <summary>
        /// 这些命令本身就是“在位文字编辑会话”：命令结束时编辑已经完成，
        /// 必须立刻切英文，不能像 TTEXT 放置那样继续等双击。
        /// </summary>
        public static readonly string[] ImmediateEnglishAfterEnd =
        {
            "TEXTEDIT", "MTEDIT", "TEDIT", "MLEADEREDITOR"
        };

        public static bool IsImmediateEnglishAfterEnd(string command)
        {
            if (string.IsNullOrEmpty(command)) return false;
            return ImmediateEnglishAfterEnd.Contains(command, StringComparer.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> AllPresets => DefaultChineseCommands;

        private static readonly Dictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TEXT"] = "单行文字",
                ["DTEXT"] = "动态文字",
                ["MTEXT"] = "多行文字",
                ["MTEXTEDIT"] = "多行文字编辑",
                ["DDEDIT"] = "编辑文字",
                ["TEDIT"] = "文字编辑",
                ["TEXTEDIT"] = "在位文字编辑",
                ["MTEDIT"] = "在位多行文字",
                ["TOBJEDIT"] = "对象编辑",
                ["TCH_OBJEDIT"] = "天正对象编辑",
                ["TTEXT"] = "天正单行文字",
                ["TMTEXT"] = "天正多行文字",
                ["TCH_TEXT"] = "天正文字",
                ["TCH_TEXT2"] = "天正文字2",
                ["TCH_MTEXT"] = "天正多行文字",
                ["ATTDEF"] = "属性定义",
                ["ATTEDIT"] = "属性编辑",
                ["EATTEDIT"] = "增强属性编辑",
                ["BATTMAN"] = "属性管理器",
                ["MLEADER"] = "多重引线",
                ["MLEADEDITOR"] = "多重引线编辑",
                ["MLEADEREDIT"] = "多重引线编辑",
                ["QLEADER"] = "快速引线",
                ["LEADER"] = "引线",
                ["DIMEDIT"] = "标注编辑",
                ["DIMTEDIT"] = "标注文字编辑",
                ["TOLERANCE"] = "形位公差",
                ["TABLE"] = "表格",
                ["TABLEDIT"] = "表格编辑",
                ["TBLEDIT"] = "表格编辑",
                ["TCELLEDIT"] = "天正表格编辑",
                ["TSHEETEDIT"] = "天正图纸编辑",
                ["TCHDIMTEXT"] = "天正标注文字",
                ["GXWZ"] = "改文字",
                ["ZYWZ"] = "自定义文字",
                ["TWORDLIB"] = "天正词库",
                ["TREPFIND"] = "天正查找替换",
                ["SPEDIT"] = "文字编辑",
                ["FIND"] = "查找",
                ["STYLE"] = "文字样式",
                ["FIELD"] = "字段"
            };

        public static string GetDisplayName(string command)
        {
            if (string.IsNullOrEmpty(command)) return string.Empty;
            return DisplayNames.TryGetValue(command, out var name) ? name : string.Empty;
        }

        /// <summary>
        /// 将 CMDNAMES / 命令事件中的原始名称归一化为大写命令名。
        /// 例如 "'_.TEXT" -> "TEXT"；"TEXT;MTEDIT" 的第一个 token 作为主命令。
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var s = raw.Trim();
            while (s.Length > 0 && (s[0] == '\'' || s[0] == '_' || s[0] == '.' || s[0] == '-' || s[0] == ' '))
                s = s.Substring(1);

            var cut = s.IndexOfAny(new[] { ';', '\n', '\r', '\t', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);

            return s.Trim().ToUpperInvariant();
        }

        /// <summary>把 CMDNAMES 拆成多个命令名（可能包含嵌套命令）。</summary>
        public static IEnumerable<string> Split(string cmdNames)
        {
            if (string.IsNullOrWhiteSpace(cmdNames)) yield break;
            foreach (var part in cmdNames.Split(new[] { ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var n = Normalize(part);
                if (!string.IsNullOrEmpty(n)) yield return n;
            }
        }
    }
}
