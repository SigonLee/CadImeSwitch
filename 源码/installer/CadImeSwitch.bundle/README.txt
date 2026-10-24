# CAD 输入法自动切换

版本：1.3.8  
适用：AutoCAD 2025 / 2026（R25.x），天正 T30 V1 互联版  
输入法：搜狗拼音（TSF），其他 TSF 输入法可在设置中切换

## 安装

1. 关闭 AutoCAD。
2. 双击 `install.cmd`（推荐），或右键 `install.ps1` → “使用 PowerShell 运行”，或执行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

3. 重启 AutoCAD。天正 T30 会随 CAD 启动，插件会自动加载。

## 常用命令

| 命令 | 作用 |
|---|---|
| `IMESWITCH` | 打开设置界面 |
| `IMESWITCHSTATUS` | 查看当前状态/后端/CMDNAMES |
| `IMESWITCHON` / `IMESWITCHOFF` | 启用/停用自动切换 |
| `IMESWITCHTEST` | 切中文 2.5 秒后恢复英文 |
| `IMESWITCHSELFTEST` | 生成自检报告 |
| `IMESWITCHLOG` | 打开日志目录 |
| `IMESWITCHRELOAD` | 重新加载配置 |
| `IMESWITCHPROFILES` | 列出已安装的 TSF 输入法 |

## 配置

`%APPDATA%\CadImeSwitch\config.json`  
日志：`%APPDATA%\CadImeSwitch\logs\`

## 卸载

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

加 `-RemoveConfig` 可同时删除配置和日志。

## 已知限制

- 天正双击编辑单行文字由天正内部处理，可能无法通过命令名识别；建议使用右键“对象编辑”(TOBJEDIT)。
- 如果命令执行中需要输入英文选项（例如 MTEXT 的 `J` 对正），请先按 Shift 临时切英文。
- 建议在 Windows 设置中开启“允许我为每个应用窗口使用不同的输入法”。
