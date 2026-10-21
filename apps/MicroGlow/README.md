# 微光 OSD / MicroGlow OSD

[项目中文说明](../../README.md) · [Project overview in English](../../README.en.md)

Windows x64 / .NET Framework 4.8。桌面界面目前为简体中文。

构建 / Build:

```powershell
powershell -NoProfile -File .\apps\MicroGlow\build.ps1
```

在仓库根目录运行，输出到 build/MicroGlow。保留 EXE、XAML 和 ICO 在同一目录。

Run from the repository root. Keep the generated EXE, XAML and ICO together in build/MicroGlow.

公开版保留亮度、画质、系统声音、场景、托盘、快捷键和动态图案。分区切换没有可见效果，已停用，不发送 E8 Set。图案打开时防止息屏，关闭释放。

The public build keeps brightness, picture settings, system audio, scenes, tray, hotkeys and test patterns. Local-dimming toggle had no visible effect and is disabled — no E8 Set sent. Patterns inhibit display sleep while open.

只匹配 RTK2775。状态读不到不代表分区背光没在工作。

Matches one RTK2775 display only. Unreadable control status doesn't mean local dimming is off.

快捷键 / Hotkeys:

| 组合 / Chord | 功能 / Action |
| --- | --- |
| Ctrl + Shift + Alt + O | 打开/收起 / Show/hide |
| Ctrl + Shift + Alt + Up/Down | 屏幕亮度 / Monitor brightness |
| Ctrl + Shift + Alt + Left/Right | 系统音量 / System volume |
| Ctrl + Shift + Alt + M | 静音 / Mute |
| Ctrl + Shift + Alt + L | 显示开关限制说明，不写设备 / Explain toggle limitation; no write |

开发入口 --layout-check 只解析布局。--probe 会读取本机屏幕和声音状态，记录保存在本机 LocalAppData。--comprehensive-test 会短暂改变多项屏幕和声音参数再尝试恢复，不是日常入口。--dimming-test 已停用并返回2。

Development flags: --layout-check parses layout only. --probe reads local display/audio state into LocalAppData. --comprehensive-test temporarily changes multiple display/audio parameters and attempts to restore them; it is not a normal-use command. --dimming-test is disabled and returns 2.
