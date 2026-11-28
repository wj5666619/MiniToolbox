# 第三方组件与许可说明

MiniToolbox 的**原始代码**采用 [MIT](LICENSE)。以下组件可能随发布包分发、内嵌，或在首次使用时按需下载，它们**不适用 MIT**，各自保留上游许可证与版权。

| 组件 | 形式 | 许可 / 说明 |
| --- | --- | --- |
| .NET Framework / Windows 系统组件 | 运行时依赖，不随仓库分发 | Microsoft 许可；`csc.exe`、`System.*.dll` 来自本机 Windows |
| FFmpeg / ffmpeg.exe | `ffmpeg_tool.zip`，按需下载 | 上游为 LGPL / GPL（取决于构建选项）；包内应保留 `LICENSE` 与构建说明。再分发前请确认所用构建的许可证 |
| 7-Zip | 发布包与 7z 压缩格式 | 7-Zip 为 LGPL；`unRAR` 代码受其额外限制。若只调用系统/独立 7z，不把其代码并入本程序 |
| 工具站（ToolSite / 3top.top / e-CNY） | `ToolSite.zip`，按需下载 | 上游标注 MIT；离线包内保留 `LICENSE` 与来源说明 |
| WebView2 托管组件（`refs/WV2*.dll`） | 编译引用并内嵌 | Microsoft WebView2 SDK / 其上游许可；仅用于承载工具站页面，不修改其代码 |
| SAM / Steam 相关工具组件 | `sam_tool.zip` | 按其上游项目许可证执行；仓库只做打包与调用，不改变其许可 |
| 其他 `*_tool.zip` 组件 | 发布包或按需下载 | 以压缩包内 `LICENSE` / `README` 为准 |

## 分发注意事项

1. 再分发二进制时，保留本仓库的 `LICENSE`、本文件，以及各组件包内的许可证与版权声明。
2. 若修改 FFmpeg、7-Zip 等第三方代码并随包分发，需按对应许可证开放相应源码。
3. 自签名证书、作者本机私钥、`发布/.gh_token.txt` 不属于开源代码，不随仓库分发。
4. 本说明不构成法律意见；如发现遗漏或标注错误，请开 Issue 或按 [SECURITY.md](SECURITY.md) 联系维护者更正。
