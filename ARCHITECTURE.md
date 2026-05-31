# BoardBeam Architecture

当前代码按职责拆分为以下模块：

- `PresenterApplicationContext`：托盘、全局快捷键、主流程入口。
- `Hotkeys`：全局快捷键定义、格式化、配置读写。
- `SettingsForm` / `HotkeyCaptureForm`：快捷键设置界面、按键捕获、重复和误触校验。
- `OverlayForm`：覆盖层窗口、输入状态、批注模式状态。
- `OverlayForm.Selection`：PixPin 风格选区、取色、区域复制/保存/贴图。
- `OverlayForm.Rendering`：覆盖层渲染、HUD、计时器/聚光灯绘制、截图导出。
- `Annotations`：画笔、形状、文字、编号、模糊等批注对象。
- `CaptureTool`：屏幕捕获、截图边界裁剪、鼠标下窗口识别和窗口截图。
- `CaptureStore` / `CaptureHistoryForm`：截图历史和历史操作。
- `ScrollingCaptureTool`：选区滚动长截图和重叠拼接。
- `PinManager` / `PinForm`：贴图窗口创建、缩放、透明度、统一关闭。
- `AnimatedGifWriter` / `RecordingTool`：区域 GIF 录屏、录屏控制窗、停止保存。
- `DemoTypeEngine`：DemoType 基础文本输入。
- `ClipboardService`：剪贴板读写保护。
- `AppPaths`：图片保存路径和用户配置路径。

仍建议后续新增的模块：

- `VideoRecordingTool`：Windows Graphics Capture + MP4/音频编码。
- `OcrTool`：Windows OCR 或本地 OCR 引擎。
- `InputHookTool`：按住快捷键即画、触控笔压感和低级输入处理。

## 开源提交边界

应提交：

- `src/` 源码
- `build.ps1`
- `app.manifest`
- `README.md`
- `ARCHITECTURE.md`
- `LICENSE`
- `.gitignore`

不应提交：

- `bin/`、`obj/` 和其他编译产物
- 本地截图、录屏和用户配置
- `.codex/`、`.claude/`、`.cursor/` 等 AI 工具配置
- `.vs/`、`.vscode/`、`.idea/` 等本地编辑器配置

