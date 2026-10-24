using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CadImeSwitch.Core;
using CadImeSwitch.Core.Ime;

namespace CadImeSwitch.UI
{
    public sealed class SettingsForm : Form
    {
        private AppConfig _config;
        private readonly List<ImeProfileInfo> _profiles = new List<ImeProfileInfo>();

        private CheckBox _chkEnabled;
        private CheckBox _chkLogging;
        private CheckBox _chkVerbose;
        private CheckBox _chkDoubleClick;
        private ComboBox _cboProfiles;
        private FlowLayoutPanel _lstCommands;
        private TextBox _txtSearch;
        private TextBox _txtCustom;
        private Label _lblStatus;
        private readonly List<string> _allCommands = new List<string>();
        private readonly HashSet<string> _checkedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _rebuilding;

        public SettingsForm(AppConfig config)
        {
            _config = config.Clone();
            BuildUi();
            LoadConfig();
            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            Text = "CAD 输入法自动切换 - 设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(920, 760);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) _cancelClicked = true;
                else if (e.Control && e.Shift && e.KeyCode == Keys.R) ResetAllDefaults();
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            var header = new Panel { Dock = DockStyle.Fill };
            var title = new Label
            {
                Text = "CAD 输入法自动切换",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 0)
            };
            var subtitle = new Label
            {
                Text = "编辑文字时自动切中文，输入命令时自动切英文。适用于 AutoCAD 2026 + 天正 T30。",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(2, 34)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var btnSaveTop = new Button
            {
                Text = "保存并应用",
                Width = 110,
                Height = 32,
                Location = new Point(760, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnSaveTop.Click += (_, __) => SaveAndClose();
            header.Controls.Add(btnSaveTop);
            root.Controls.Add(header, 0, 0);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildLeftPanel(), 0, 0);
            content.Controls.Add(BuildCommandPanel(), 1, 0);
            root.Controls.Add(content, 0, 1);

            root.Controls.Add(BuildButtons(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildLeftPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 0, 10, 0)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var general = new GroupBox { Text = "常规", Dock = DockStyle.Fill, Padding = new Padding(12) };
            _chkEnabled = new CheckBox
            {
                Text = "启用自动切换",
                AutoSize = true,
                Location = new Point(16, 28)
            };
            _chkLogging = new CheckBox
            {
                Text = "启用日志",
                AutoSize = true,
                Location = new Point(16, 58)
            };
            _chkVerbose = new CheckBox
            {
                Text = "记录详细日志（排查问题用）",
                AutoSize = true,
                Location = new Point(16, 88)
            };
            _chkDoubleClick = new CheckBox
            {
                Text = "启用双击在位编辑支持（实验）",
                AutoSize = true,
                Location = new Point(16, 118)
            };
            var mode = new Label
            {
                Text = "英文状态：关闭输入法（保留当前键盘布局）",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(16, 150)
            };
            var tip = new Label
            {
                Text = "建议在 Windows 设置中开启：\r\n“允许我为每个应用窗口使用不同的输入法”",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(16, 178)
            };
            general.Controls.AddRange(new Control[] { _chkEnabled, _chkLogging, _chkVerbose, _chkDoubleClick, mode, tip });
            _chkLogging.CheckedChanged += (_, __) => _chkVerbose.Enabled = _chkLogging.Checked;
            panel.Controls.Add(general, 0, 0);

            var imeGroup = new GroupBox { Text = "中文输入法", Dock = DockStyle.Fill, Padding = new Padding(12) };
            var lbl = new Label
            {
                Text = "选择编辑文字时使用的输入法：",
                AutoSize = true,
                Location = new Point(16, 28)
            };
            _cboProfiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(16, 54),
                Width = 290,
                DisplayMember = "Name"
            };
            var btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(16, 88),
                Width = 80
            };
            btnRefresh.Click += (_, __) => LoadProfiles();

            var hint = new Label
            {
                Text = "若列表为空，请先在 Windows 中安装并启用搜狗拼音；\r\n插件会优先读取注册表中的 TSF 配置。",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(16, 126),
                MaximumSize = new Size(300, 0)
            };
            imeGroup.Controls.AddRange(new Control[] { lbl, _cboProfiles, btnRefresh, hint });
            panel.Controls.Add(imeGroup, 0, 1);

            return panel;
        }

        private Control BuildCommandPanel()
        {
            var group = new GroupBox { Text = "需要自动切换为中文的命令", Dock = DockStyle.Fill, Padding = new Padding(12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            var search = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var lblSearch = new Label { Text = "搜索：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 0),
                PlaceholderText = "输入字符筛选命令..."
            };
            _txtSearch.TextChanged += (_, __) => ApplyCommandFilter();
            search.Controls.Add(lblSearch, 0, 0);
            search.Controls.Add(_txtSearch, 1, 0);
            layout.Controls.Add(search, 0, 0);

            _lstCommands = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.Controls.Add(_lstCommands, 0, 1);

            var hint = new Label
            {
                Text = "点击前面的复选框切换；点击文字只选中。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(120, 120, 120),
                Margin = new Padding(0)
            };
            layout.Controls.Add(hint, 0, 2);

            var custom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            custom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            var lblCustom = new Label { Text = "自定义命令：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _txtCustom = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 6, 0) };
            var btnAdd = new Button { Text = "添加", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            btnAdd.Click += (_, __) => AddCustomCommand();
            custom.Controls.Add(lblCustom, 0, 0);
            custom.Controls.Add(_txtCustom, 1, 0);
            custom.Controls.Add(btnAdd, 2, 0);
            layout.Controls.Add(custom, 0, 3);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            var btnAll = new Button { Text = "全选", Width = 60, Height = 30 };
            var btnNone = new Button { Text = "全不选", Width = 64, Height = 30 };
            var btnDefault = new Button { Text = "恢复默认", Width = 84, Height = 30 };
            var btnResetAll = new Button { Text = "全部还原默认", Width = 108, Height = 30 };
            var btnSave2 = new Button { Text = "保存并应用", Width = 100, Height = 30 };
            btnAll.Click += (_, __) => SetAllChecked(true);
            btnNone.Click += (_, __) => SetAllChecked(false);
            btnDefault.Click += (_, __) => RestoreDefaults();
            btnResetAll.Click += (_, __) => ResetAllDefaults();
            btnSave2.Click += (_, __) => SaveAndClose();
            buttons.Controls.AddRange(new Control[] { btnAll, btnNone, btnDefault, btnResetAll, btnSave2 });
            layout.Controls.Add(buttons, 0, 4);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildButtons()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            _lblStatus = new Label
            {
                Text = "修改后点击“保存并应用”，无需重启 CAD。",
                AutoSize = true,
                Location = new Point(0, 18),
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            var btnSave = new Button
            {
                Text = "保存并应用",
                Width = 110,
                Height = 32,
                Location = new Point(470, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            var btnTest = new Button
            {
                Text = "测试",
                Width = 80,
                Height = 32,
                Location = new Point(588, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            var btnLog = new Button
            {
                Text = "打开日志",
                Width = 90,
                Height = 32,
                Location = new Point(676, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            var btnCancel = new Button
            {
                Text = "取消",
                Width = 80,
                Height = 32,
                Location = new Point(774, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            btnSave.Click += (_, __) => SaveAndClose();
            btnCancel.Click += (_, __) =>
            {
                _cancelClicked = true;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            btnTest.Click += (_, __) => RunTest();
            btnLog.Click += (_, __) => OpenLog();

            panel.Controls.AddRange(new Control[] { _lblStatus, btnSave, btnTest, btnLog, btnCancel });
            return panel;
        }

        private void LoadConfig()
        {
            _chkEnabled.Checked = _config.Enabled;
            _chkLogging.Checked = _config.EnableLogging;
            _chkVerbose.Checked = _config.LogVerbose;
            _chkVerbose.Enabled = _config.EnableLogging;
            _chkDoubleClick.Checked = _config.EnableDoubleClickSupport;
            LoadProfiles();
            LoadCommands();
        }

        private void LoadProfiles()
        {
            _profiles.Clear();
            _profiles.AddRange(TsfProfileDiscovery.Discover());

            if (_profiles.Count == 0)
            {
                _profiles.Add(new ImeProfileInfo
                {
                    Name = string.IsNullOrWhiteSpace(_config.ChineseIme.Name)
                        ? "当前配置（未检测到 TSF 输入法）"
                        : _config.ChineseIme.Name,
                    Clsid = _config.ChineseIme.Clsid,
                    ProfileGuid = _config.ChineseIme.ProfileGuid,
                    LanguageId = _config.ChineseIme.LanguageId
                });
            }

            _cboProfiles.Items.Clear();
            foreach (var p in _profiles) _cboProfiles.Items.Add(p);

            int index = _profiles.FindIndex(p =>
                string.Equals(p.Clsid, _config.ChineseIme.Clsid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProfileGuid, _config.ChineseIme.ProfileGuid, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = _profiles.FindIndex(p =>
                    !string.IsNullOrEmpty(_config.ChineseIme.Name) &&
                    p.Name.IndexOf(_config.ChineseIme.Name, StringComparison.CurrentCultureIgnoreCase) >= 0);
            _cboProfiles.SelectedIndex = index >= 0 ? index : 0;

            if (_lblStatus != null && _profiles.Count > 0 && _profiles[0].Clsid == _config.ChineseIme.Clsid)
                _lblStatus.Text = "已检测到 " + _profiles.Count + " 个输入法配置。";
        }

        private void LoadCommands()
        {
            _allCommands.Clear();
            _checkedCommands.Clear();

            foreach (var cmd in CommandCatalog.DefaultChineseCommands)
            {
                var n = CommandCatalog.Normalize(cmd);
                if (!string.IsNullOrEmpty(n) && !_allCommands.Contains(n, StringComparer.OrdinalIgnoreCase))
                    _allCommands.Add(n);
            }

            foreach (var cmd in _config.ChineseCommands.Select(CommandCatalog.Normalize)
                         .Where(c => !string.IsNullOrEmpty(c)))
            {
                if (!_allCommands.Contains(cmd, StringComparer.OrdinalIgnoreCase))
                    _allCommands.Add(cmd);
            }

            foreach (var cmd in _allCommands)
            {
                if (_config.CommandSet.Contains(cmd)) _checkedCommands.Add(cmd);
            }

            // TOBJEDIT 直接纳入中文，不依赖旧配置是否已经保存过。
            if (!_allCommands.Contains("TOBJEDIT", StringComparer.OrdinalIgnoreCase))
                _allCommands.Add("TOBJEDIT");
            _checkedCommands.Add("TOBJEDIT");

            // 旧版本列表事件缺陷可能把配置写成“只有 TOBJEDIT”，导致自动切换几乎失效。
            // 这种情况下自动恢复默认命令，用户仍可在界面上重新取消。
            if (_checkedCommands.Count <= 1)
            {
                foreach (var cmd in CommandCatalog.DefaultChineseCommands)
                    _checkedCommands.Add(CommandCatalog.Normalize(cmd));
            }

            _allCommands.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyCommandFilter();
        }

        private void ApplyCommandFilter()
        {
            if (_lstCommands == null) return;

            string filter = _txtSearch?.Text?.Trim() ?? "";
            _rebuilding = true;
            _lstCommands.SuspendLayout();
            try
            {
                _lstCommands.Controls.Clear();
                foreach (var cmd in _allCommands)
                {
                    string display = CommandCatalog.GetDisplayName(cmd);
                    if (filter.Length > 0 &&
                        cmd.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        display.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var itemPanel = new Panel
                    {
                        Width = 145,
                        Height = 24,
                        Margin = new Padding(2, 2, 2, 2)
                    };
                    var cb = new CheckBox
                    {
                        Width = 18,
                        Height = 18,
                        Location = new Point(0, 3),
                        Tag = cmd,
                        Checked = _checkedCommands.Contains(cmd)
                    };
                    var lbl = new Label
                    {
                        Text = string.IsNullOrEmpty(display) ? cmd : cmd + "  " + display,
                        AutoSize = false,
                        Width = 122,
                        Height = 20,
                        Location = new Point(20, 2),
                        TextAlign = ContentAlignment.MiddleLeft,
                        AutoEllipsis = true
                    };
                    cb.CheckedChanged += (s, __) =>
                    {
                        if (_rebuilding) return;
                        var box = (CheckBox)s;
                        string name = CommandCatalog.Normalize(box.Tag?.ToString() ?? "");
                        if (string.IsNullOrEmpty(name)) return;
                        if (box.Checked) _checkedCommands.Add(name);
                        else _checkedCommands.Remove(name);
                    };
                    itemPanel.Controls.Add(cb);
                    itemPanel.Controls.Add(lbl);
                    _lstCommands.Controls.Add(itemPanel);
                }
            }
            finally
            {
                _lstCommands.ResumeLayout();
                _rebuilding = false;
            }
        }

        private void AddCustomCommand()
        {
            var cmd = CommandCatalog.Normalize(_txtCustom.Text);
            if (string.IsNullOrEmpty(cmd))
            {
                MessageBox.Show(this, "请输入命令名，例如：MYTEXT", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_allCommands.Contains(cmd, StringComparer.OrdinalIgnoreCase))
                _allCommands.Add(cmd);
            _checkedCommands.Add(cmd);
            _allCommands.Sort(StringComparer.OrdinalIgnoreCase);
            _txtCustom.Clear();

            // 清空搜索，确保新增命令可见
            if (_txtSearch != null && _txtSearch.TextLength > 0) _txtSearch.Clear();
            ApplyCommandFilter();
            var item = _lstCommands.Controls.OfType<Panel>()
                .SelectMany(p => p.Controls.OfType<CheckBox>())
                .FirstOrDefault(c => string.Equals(c.Tag?.ToString(), cmd, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.Checked = true;
                item.Focus();
            }
        }

        private void SetAllChecked(bool value)
        {
            if (value)
            {
                foreach (var cmd in _allCommands) _checkedCommands.Add(cmd);
            }
            else
            {
                _checkedCommands.Clear();
            }
            ApplyCommandFilter();
        }

        private void RestoreDefaults()
        {
            _allCommands.Clear();
            _checkedCommands.Clear();
            foreach (var cmd in CommandCatalog.DefaultChineseCommands)
            {
                _allCommands.Add(cmd);
                _checkedCommands.Add(cmd);
            }
            if (!_allCommands.Contains("TOBJEDIT", StringComparer.OrdinalIgnoreCase))
                _allCommands.Add("TOBJEDIT");
            _checkedCommands.Add("TOBJEDIT");
            _allCommands.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyCommandFilter();
        }

        private bool _savedOnClose;
        private bool _cancelClicked;

        private void ResetAllDefaults()
        {
            var dr = MessageBox.Show(this,
                "将把启用状态、日志、输入法、双击支持和命令列表全部恢复为初始默认。\r\n" +
                "恢复后仍需点击“保存并应用”（或直接关闭窗口）才会写入配置。\r\n" +
                "确定继续吗？",
                "全部还原默认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;

            _config = ConfigStore.CreateDefault();
            LoadConfig();
            if (_txtSearch != null) _txtSearch.Clear();
            _lblStatus.Text = "已恢复初始默认设置，正在保存...";
            // 一键还原后立即保存，避免用户再次忘记点保存。
            if (TrySaveConfig())
            {
                _lblStatus.Text = "已恢复并保存初始默认设置。";
            }
        }

        private void SaveAndClose()
        {
            if (!TrySaveConfig()) return;
            _savedOnClose = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_savedOnClose || _cancelClicked) return;
            // 用户直接点右上角 X 时也保存，避免“改了但没点保存”导致配置丢失。
            // 注意：WinForms 点 X 会把 DialogResult 设为 Cancel，不能用 DialogResult 判断取消。
            if (!TrySaveConfig())
            {
                e.Cancel = true;
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private bool TrySaveConfig()
        {
            var selected = _checkedCommands
                .Select(CommandCatalog.Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selected.Count == 0)
            {
                var dr = MessageBox.Show(this,
                    "没有勾选任何中文命令，插件将不会自动切换中文。\r\n确定要保存吗？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return false;
            }

            var profile = _cboProfiles.SelectedItem as ImeProfileInfo;
            if (profile != null)
            {
                _config.ChineseIme = new ImeProfileConfig
                {
                    Name = profile.Name,
                    Clsid = profile.Clsid,
                    ProfileGuid = profile.ProfileGuid,
                    LanguageId = profile.LanguageId
                };
            }

            _config.Enabled = _chkEnabled.Checked;
            _config.EnableLogging = _chkLogging.Checked;
            _config.LogVerbose = _chkVerbose.Checked && _chkLogging.Checked;
            _config.EnableDoubleClickSupport = _chkDoubleClick.Checked;
            _config.ChineseCommands = selected;

            try
            {
                ConfigStore.Save(_config);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void RunTest()
        {
            var ime = Plugin.Instance?.Ime;
            if (ime == null)
            {
                MessageBox.Show(this, "插件尚未初始化。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ok = ime.ForceSet(true);
            _lblStatus.Text = ok
                ? "已切换到中文，2.5 秒后自动恢复英文……"
                : "切换到中文失败，请查看日志。";

            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                timer.Dispose();
                ime.ForceSet(false);
                _lblStatus.Text = "测试结束，已恢复英文。";
            };
            timer.Start();
        }

        private void OpenLog()
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
                MessageBox.Show(this, ex.Message, "打开日志失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
