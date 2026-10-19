<p align="center"><img src="assets/app-512.png" width="112" alt="Llama Launcher 图标"></p>

# Llama Launcher · WinUI 3

**为 Windows 11 提供直观的本地 GGUF 模型启动与连接界面。**

[English](README.en.md) · [客户端接入](docs/CLIENTS.md) · [1.3.0 更新说明](RELEASE_NOTES.md) · [下载 Windows x64 版本](https://github.com/accbilili/llama-launcher-winui3/releases)

选择模型、调整参数、启动本机 `llama-server`，然后连接 WorkBuddy、Cherry Studio 或 DeepSeek Harness。使用 C# 和 WinUI 3 实现，推理由独立安装的 llama.cpp 完成。

> 当前版本：**1.3.0，预览阶段**。界面目前为简体中文，中英文文档均已提供。已完成编译和离线检查；新版窗口交互、显存恢复及三个客户端的真实对话效果仍需实测。

## 主要功能

| 功能 | 说明 |
| --- | --- |
| 本地模型列表 | 递归扫描 GGUF，读取架构、量化类型、训练上下文及模型名称；分片模型只列首片 |
| 可视化启动参数 | 端口、上下文、GPU 层数、CPU 线程、批大小、Flash Attention、KV 缓存及采样参数 |
| 模型参数预设 | 每个模型保存多组命名预设，区分已保存参数和当前运行参数 |
| 思考与推理选项 | 根据聊天模板声明提供 `enable_thinking` 或 `reasoning_effort` 设置 |
| 服务管理 | 启动、停止、重启，检查端口与健康状态，显示日志；管理本程序启动的服务进程 |
| 三客户端接入 | WorkBuddy 配置写入与备份恢复、Cherry Studio 服务商导入、Harness 填写信息及 YAML 片段 |
| 接口测试 | 基础回复、流式输出、工具调用与结果回传；提供文本及翻译测试 |
| 资源与窗口 | Windows GPU 专用显存监控、手动重检、托盘、重复启动唤醒、窗口布局与位置记忆 |
| 高 DPI 与图标 | PerMonitorV2、窄窗口布局，应用多尺寸 ICO 与模型 SVG 标识 |

## 图标预览

![应用图标及模型图标](assets/icons-preview.png)

上图展示应用与模型图标，不是 1.3.0 界面截图。Qwen、GPT-OSS、HY-MT2 标识用于识别模型系列；其他模型使用通用 GGUF 图标。

## 使用条件

- **Windows 11 x64**；当前不提供 Windows 10、ARM64、Linux 或 macOS 版本。
- 本机已有可运行的 [`llama-server.exe`](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) 及其所需运行文件。
- 已下载并有权使用的 GGUF 模型，以及足够的内存或显存。
- 如需连接第三方客户端，另行安装对应客户端。

发布包包含 .NET 与 WinUI 运行库，无需另装 Python。模型、llama.cpp 和第三方客户端不包含在发布包中。GPU 加速由你的 llama.cpp 构建、驱动和硬件决定。

## 快速开始

1. 从 Releases 下载 `LlamaLauncher-WinUI3-1.3.0-win-x64.zip`，完整解压到可写目录。保留 EXE 同目录的资源与运行库。
2. 退出正在运行的旧版启动器，再打开 `LlamaLauncher.exe`。
3. 在“路径设置”选择 `llama-server.exe` 和模型根目录。默认模型目录为 `D:\AI\models`，可以修改。
4. 选择模型，调整上下文与 GPU 层数，点击“启动模型”，等待“服务已就绪”。
5. 点击“打开聊天”使用 llama-server 自带页面，或到“客户端接入”复制相应连接信息。

服务监听 `127.0.0.1`。例如端口为 `8081` 时：

| 用途 | 地址 |
| --- | --- |
| 浏览器聊天 | `http://127.0.0.1:8081/` |
| OpenAI 风格 API 基础地址 | `http://127.0.0.1:8081/v1` |
| Chat Completions 完整地址 | `http://127.0.0.1:8081/v1/chat/completions` |
| 模型列表 | `http://127.0.0.1:8081/v1/models` |

模型 ID 是 GGUF 文件名去掉 `.gguf`，以界面显示为准。当前服务未启用 API Key 验证；客户端要求非空密钥时使用 `local` 占位。

## 连接三个客户端

| 客户端 | 启动器提供的操作 | 使用时需要完成 |
| --- | --- | --- |
| WorkBuddy | 预览并合并本地模型配置、写入前备份、选择备份恢复 | 开启自定义协议，使用完整 Chat Completions 地址，重新选择模型 |
| Cherry Studio | 通过原生链接导入 OpenAI 类型服务商 | 确认导入后获取/添加模型，再在聊天中选择 |
| DeepSeek Harness | 复制填写信息或导出 YAML 片段 | 在原生设置中添加服务商与模型；程序不自动合并 YAML |

详细步骤及 404 排查见 [客户端接入说明](docs/CLIENTS.md)。配置生成或连接检查通过，不等于三个客户端的全部 Agent 功能已经验证。

## 自检与已知边界

“检查连接”只读取健康状态与模型列表。基础、流式、工具调用测试需要主动点击，并会请求当前本地模型；工具测试只执行内存回显，不运行系统命令。支持取消，单次接口自检最长三分钟。

- 工具调用、思考输出和视觉能力取决于模型、聊天模板、投影文件及 llama-server 支持，界面标识不是能力保证。
- 显存读数来自 Windows，表示整张显卡的专用显存占用；驱动不提供计数器时可能不可用。
- 保留 PerMonitorV2，并实现自适应布局；1.3.0 的跨屏、4K 缩放和窗口恢复仍需运行验证。
- “缩到托盘”保留模型服务；真正退出会停止本程序启动的服务。默认关闭窗口即退出，可在设置中改为关闭缩托盘。
- 不包含模型下载、自动升级、完整聊天历史管理或多模型同时服务。

## 从源码构建

在 Windows 11 x64 上安装 **.NET 10 SDK**，在仓库根目录执行：

```powershell
.\build.ps1
```

输出为 `release-1.3.0\LlamaLauncher.exe`。首次构建需要还原 NuGet 依赖。构建使用 Windows SDK 10.0.26100 系列工具与 WinUI 3；依赖版本固定在项目文件中。图标已生成，正常构建不需要 Node.js。

离线检查入口：

```powershell
dotnet run --project checks\StaticChecks.csproj -c Release
```

此入口会读取本机设置及 GGUF 文件，需要先配置有效模型目录与 llama-server 路径；其中 HTTP 测试使用内存模拟响应，WorkBuddy 写入测试只使用临时文件，不启动模型、不修改真实客户端配置。`tests\SmokeTests.csproj` 为早期真实模型测试，带有“三个模型”的环境假设，会启动服务和发送请求，不应当作通用离线检查运行。

当前验证结果：Release 编译 0 警告、0 错误；备份恢复、模拟接口成功与失败场景、GGUF 元数据和参数检查通过。未将这些检查描述为真实客户端端到端测试。

## 本地数据

配置与预设：`%LOCALAPPDATA%\LlamaLauncherWinUI\settings.json`；服务日志：同目录 `logs\server-*.log`；客户端异常：`client-errors.log`。

WorkBuddy 配置位于 `%USERPROFILE%\.workbuddy\models.json`，备份保存在相同目录。只在主动点击写入或恢复时修改。模型文件不会由本程序编辑。

## 致谢与第三方声明

- 界面与使用流程参考 [lop008/llama-gui-launcher](https://github.com/lop008/llama-gui-launcher)。本项目使用 C# / WinUI 3 实现。
- 推理服务由 [llama.cpp](https://github.com/ggml-org/llama.cpp) 提供。
- 模型图标来自 [LobeHub Icons](https://github.com/lobehub/lobe-icons)，来源见 [图标说明](src/Assets/README.md)，其 MIT 许可见 [许可证文件](licenses/LobeHub-Icons-MIT.txt)。
- 其他随附组件的许可和声明保留在 `licenses/`。模型及客户端名称和标识归相应权利人所有，不表示官方合作或背书。

项目自身的许可证尚待仓库所有者确认；第三方许可不代表本项目整体已经采用相同许可证。
