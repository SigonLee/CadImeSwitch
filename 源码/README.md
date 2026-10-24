# CAD 输入法自动切换（CadImeSwitch）

适用于 **AutoCAD 2026（R25.1）+ 天正 T30 V1 互联版 + 搜狗拼音**。

在 CAD/天正中：

- 进入 `TEXT / MTEXT / DDEDIT / TTEXT / TMTEXT / TOBJEDIT` 等文字、标注、表格命令时，自动切中文；
- **TTEXT 连续放置时保持中文；按空格/右键/回车结束放置后，不会立刻切英文，而是继续等待双击/右键进入文字编辑；编辑结束才切回英文**；
- **双击天正文字进入在位编辑时，也会自动切中文（实验特性，可在设置中关闭）**；
- 回到命令提示或执行普通绘图命令时，自动切英文；
- 系统托盘图标：双击打开设置，右键可启停/打开日志；
- 无需手动按 Shift，无需管理员权限，不使用全局键盘钩子。

## 交付内容

```text
CadImeSwitch/
├─ src/CadImeSwitch/          # .NET 8 插件源码
├─ installer/                 # ApplicationPlugins bundle + 安装/卸载脚本
├─ dist/                      # 可直接分发的安装包
├─ docs/
│  ├─ md/                     # 文档 Markdown 源
│  └─ word/                   # 8 份 Word 文档
├─ test/                      # 自动加载测试脚本与结果
├─ build.ps1                  # 编译 + 打包
└─ README.md
```

## 快速安装

1. 关闭 AutoCAD。
2. 解压 `dist/CadImeSwitch-1.3.8.zip`。
3. 双击 `install.cmd`（或右键 `install.ps1` →“使用 PowerShell 运行”）。
4. 重启 AutoCAD。

安装脚本会把插件复制到：

```text
%APPDATA%\Autodesk\ApplicationPlugins\CadImeSwitch.bundle
```

并写入当前用户 AutoCAD 配置的 `TRUSTEDPATHS`，避免每次弹“未签名的可执行文件”。

## 验证

在 CAD 命令行执行：

```text
IMESWITCHSTATUS
IMESWITCHSELFTEST
```

- `IMESWITCHSTATUS`：查看启用状态、后端（TSF/IMM32）、当前 `CMDNAMES`。
- `IMESWITCHSELFTEST`：生成自检报告到 `%APPDATA%\CadImeSwitch\logs`。
- `IMESWITCH`：打开设置界面。

## 从源码构建

要求：.NET 8 SDK、AutoCAD 2026（提供 `acmgd.dll / acdbmgd.dll / accoremgd.dll`）。

```powershell
dotnet build .\src\CadImeSwitch\CadImeSwitch.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` 会生成：

- `installer/CadImeSwitch.bundle/Contents/Windows/CadImeSwitch.dll`
- `dist/CadImeSwitch-1.3.8.zip`

## 技术路线

- AutoCAD .NET 8 插件，`IExtensionApplication` 自动加载。
- `CMDNAMES` 轮询（100ms）+ `SystemVariableChanged` + `DocumentActivated`。
- 输入法控制：TSF 优先（`ITfInputProcessorProfileMgr` / `ITfCompartmentMgr`），IMM32 兜底。
- 配置：`%APPDATA%\CadImeSwitch\config.json`。
- 日志：`%APPDATA%\CadImeSwitch\logs`；可在设置中关闭，单文件 2MB 自动分段，最多保留 8 个文件（约 12MB）。

## 已知限制

- 双击文字的在位编辑支持为**实验特性**：通过双击消息 + 中文租约实现，不依赖天正暴露 `CMDNAMES`；如个别天正版本表现异常，可在 `IMESWITCH` 设置中关闭，改用右键“对象编辑”(`TOBJEDIT`)。
- 命令执行中如需输入英文选项（如 MTEXT 的 `J`），请按 Shift 临时切英文；本次命令内插件不会抢回。
- 本期按 AutoCAD 2026 验证；AutoCAD 2025 理论兼容，未做完整回归。

## 文档

- [项目建议书与可行性分析](docs/word/01_项目建议书与可行性分析.docx)
- [需求规格说明书](docs/word/02_需求规格说明书_SRS.docx)
- [概要设计说明书](docs/word/03_概要设计说明书_HLD.docx)
- [详细设计说明书](docs/word/04_详细设计说明书_LLD.docx)
- [测试计划与测试用例](docs/word/05_测试计划与测试用例.docx)
- [安装部署手册](docs/word/06_安装部署手册.docx)
- [用户操作手册](docs/word/07_用户操作手册.docx)
- [需求跟踪矩阵](docs/word/08_需求跟踪矩阵.docx)
