# 图标来源

- `app.svg`：本项目制作的羊驼和启动按钮图标；SVG 为可编辑源文件。
- `app.ico`：由 app.svg 渲染的 16、20、24、32、40、48、64、96、128、256 像素图层，用于 EXE、窗口及托盘。
- `app-512.png`：由 app.svg 渲染的预览图。
- `gguf.svg`：本项目制作的通用模型芯片图标。
- `qwen-color.svg`、`openai.svg`、`hunyuan-color.svg`：来自 [LobeHub Icons](https://github.com/lobehub/lobe-icons)，固定版本 `8a1b2821bb768c484fe8da20c5e1838b6bf551a9`，目录 `packages/static-svg/icons`。OpenAI 图形将 currentColor 固定为深绿色，放在浅色底板上显示。
- LobeHub Icons 的 MIT 许可证随程序分发，见 `licenses/LobeHub-Icons-MIT.txt`。

界面直接使用 SVG，按逻辑尺寸渲染；模型图形不从低分辨率截图截取。图标资源随程序提供，运行时无需联网。
