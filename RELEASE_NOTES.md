# v1.3.0 · 首次公开预览 / Initial public preview

## 中文

面向 Windows 11 x64 的本地 GGUF 模型启动器，使用 WinUI 3，为已有 llama-server 提供模型选择、参数设置、日志、托盘和客户端接入界面。程序界面为简体中文，提供中英文文档。

### 本次完善

1. 重复启动时恢复已有窗口，支持最小化和托盘状态。
2. 显存监控失败后有限重试，并支持手动重新检测。
3. 连接、HTTP、格式、超时及内存错误提供具体提示，可主动查看日志。
4. 新增基础回复、流式输出、工具调用及结果回传自检，支持取消。
5. 窄窗口折叠侧栏、字段和按钮换行，记住窗口位置、大小和最大化状态。
6. 日志有界缓冲与增量显示，减少隐藏页面刷新，复用已读 GGUF 元数据。
7. 区分参数保存与运行生效状态，可恢复当前运行参数；无效输入时保留原模型。
8. WorkBuddy 备份列表、目录查看和恢复；恢复前再次备份当前配置。

同时包含已有的每模型预设、思考设置、文本/翻译测试、三客户端配置与应用/模型图标。

### 下载与运行

- `LlamaLauncher-WinUI3-1.3.0-win-x64.zip`：完整解压后运行 `LlamaLauncher.exe`。
- `LlamaLauncher-WinUI3-1.3.0-source.zip`：与本次发布源码一致的源码包。
- `LlamaLauncher-WinUI3-1.3.0-SHA256.txt`：本次程序与源码压缩包的 SHA256 校验值。

需要另行安装 llama.cpp 并准备 GGUF；不包含模型或第三方客户端。升级前先退出旧版，保留 EXE 同目录的全部运行文件。

### 验证状态

Release 编译 0 警告、0 错误；临时配置备份/恢复、模拟 HTTP/SSE/工具调用、失败和取消场景以及元数据/参数检查通过。1.3.0 新版 UI、真实模型自检、显存恢复与三个客户端端到端效果仍需实测。此次发布标记为 Pre-release。

## English

A WinUI 3 launcher for local GGUF models on Windows 11 x64. It adds model selection, launch settings, logs, tray controls, and client setup to an existing llama-server installation. The UI is in Simplified Chinese, with Chinese and English documentation.

### Changes

1. Restore the existing window on repeated launch, including minimized and tray states.
2. Retry GPU memory monitoring after failures, with manual re-detection.
3. Explain connection, HTTP, format, timeout, and memory errors with an optional log action.
4. Add basic, streaming, and tool-call round-trip checks with cancellation.
5. Adapt sidebars, fields, and buttons to smaller windows; remember placement, size, and maximized state.
6. Buffer and append logs efficiently, reduce hidden-page refresh, and reuse GGUF metadata.
7. Distinguish saved settings from active server parameters, restore active settings, and retain the selected model when inputs are invalid.
8. List, locate, and restore WorkBuddy backups, backing up the current configuration before restoration.

Includes existing per-model presets, thinking settings, text/translation testing, three-client configuration, and application/model icons.

### Assets and requirements

Extract the full Windows x64 archive and run `LlamaLauncher.exe`. The source archive and SHA256 checksums accompany the binary archive. Install llama.cpp separately and supply your own GGUF files. Models and third-party clients are not bundled. Exit older launcher versions before upgrading.

### Verification

Release compilation passed with zero warnings/errors. Temporary-file backup/restoration, simulated HTTP/SSE/tool calls, error/cancellation cases, metadata, and argument checks passed. The new UI, real-model diagnostics, GPU monitoring recovery, and end-to-end use in the three clients still require runtime testing. Release status: **Pre-release**.
