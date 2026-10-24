# CadImeSwitch —— CAD 输入法自动切换

AutoCAD 2026（R25.1）+ 天正 T30 V1 互联版 + 搜狗拼音 环境下，自动在「命令行英文 ↔ 文字编辑中文」之间无感切换。

- 进入 `TEXT / MTEXT / DDEDIT / TTEXT / TMTEXT / TOBJEDIT` 等文字、标注、表格命令时自动切中文；
- TTEXT 连续放置时保持中文，编辑结束才切回英文；
- 双击天正文字进入在位编辑也会自动切中文（实验特性，可在设置中关闭）；
- 回到命令提示 / 执行绘图命令时自动切英文；
- 系统托盘图标：双击打开设置，右键启停/打开日志；
- 无需手动按 Shift、无需管理员权限、不使用全局键盘钩子。

## 仓库结构

```text
CadImeSwitch/
├─ src/CadImeSwitch/     # .NET 8 插件源码（核心）
├─ installer/            # ApplicationPlugins bundle + 安装/卸载脚本
├─ 测试/                 # 自动加载 / 双击 / 会话测试脚本与结果
├─ 程序/                 # 可直接分发的安装包 CadImeSwitch-1.3.8.zip
├─ 文档/                 # 8 份项目文档（建议书/需求/设计/测试/手册）
├─ build.ps1             # 编译 + 打包
└─ README.md
```

完整说明见 [源码/README.md](源码/README.md)。

## 快速安装（最终用户）

1. 关闭 AutoCAD。
2. 解压 `程序/CadImeSwitch-1.3.8.zip`。
3. 双击 `install.cmd`（或右键 `install.ps1` →“使用 PowerShell 运行”）。
4. 重启 AutoCAD。

插件复制到 `%APPDATA%\Autodesk\ApplicationPlugins\CadImeSwitch.bundle`，
安装脚本会写入 `TRUSTEDPATHS` 避免安全弹窗。

## 从源码构建（开发者）

要求：.NET 8 SDK、本机安装 AutoCAD 2026（提供 `acmgd.dll / acdbmgd.dll / accoremgd.dll`）。

> ⚠️ **注意**：`src/CadImeSwitch/CadImeSwitch.csproj` 中三个 AutoCAD DLL 的 `HintPath`
> 目前硬编码为 `D:\Program Files\Autodesk\AutoCAD 2026\`。
> 换机编译前请改为本机 AutoCAD 的安装路径（或提交 Pull Request 改成自动探测/环境变量方式）。

```powershell
dotnet build .\src\CadImeSwitch\CadImeSwitch.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## 验证

在 CAD 命令行执行：

- `IMESWITCH` —— 打开设置界面
- `IMESWITCHSTATUS` —— 查看启用状态 / 后端（TSF/IMM32）/ CMDNAMES
- `IMESWITCHSELFTEST` —— 生成自检报告到 `%APPDATA%\CadImeSwitch\logs`

## 技术路线

- AutoCAD .NET 8 插件，`IExtensionApplication` 自动加载，无独立进程；
- `CMDNAMES` 轮询 + `SystemVariableChanged` + `DocumentActivated` 感知命令状态；
- 双击检测（BeginDoubleClick / 消息层 / 鼠标轮询三层）+「文字会话租约」覆盖天正双击在位编辑盲区；
- 输入法控制：TSF 优先（`ITfInputProcessorProfileMgr` / `ITfCompartmentMgr` + compartment 开关），
  WM_IME_CONTROL（CUAS）补多线程上下文，IMM32 兜底；
- 配置 `%APPDATA%\CadImeSwitch\config.json`，日志 `%APPDATA%\CadImeSwitch\logs`（2MB×8 自动滚动）。

## 已知限制

- 双击在位编辑为实验特性：基于双击消息 + 中文租约实现，个别天正版本表现异常时可在设置中关闭，改用右键“对象编辑”（TOBJEDIT）；
- 命令执行中需输入英文选项（如 MTEXT 的 J）时按 Shift 临时切英文，本次命令内插件不抢回；
- 按 AutoCAD 2026 验证；2025 理论兼容未完整回归。

## License

[MIT](LICENSE)