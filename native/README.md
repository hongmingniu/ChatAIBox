# ChatAIBox 原生界面测试版

Windows 使用 WinUI 3，Android 使用 Uno Platform，共用 C#/XAML 界面与 C# 核心。

此目录目前为 0.4.0 测试版。Android 最低要求 Android 7.0；Windows 为 x64 Windows 10 2004 或更高版本。安装包包含所需 .NET 运行时。

聊天永久保存于手机的原有加密文件；Windows 只保留运行期间的聊天副本。服务器仅转发端到端加密数据。服务器地址和访问密钥由用户填写，公开包不提供私人服务器配置。

普通 Markdown 使用原生控件；数学公式、HTML 等复杂内容由独立的本地受限 WebView 渲染。原生应用不包含 Electron。

## 文件输入

点击输入区的“＋ → 文件”可选择文本、代码、CSV、PDF、DOCX 和 XLSX，可在发送前移除附件。每条消息最多 6 个文档，单文件最大 20 MB，最多提取 25 万字符；超出限制会报错，不会悄悄截断。

文档在本机提取文字，发送时作为明确标注的附件资料交给当前模型。提取的文字与文件名保存在聊天正文中，兼容已有的加密同步、备份和上下文压缩；原始二进制文件不保存到聊天库或上传到转发服务器。扫描 PDF 需要先转文字或使用图片输入；DOCX/XLSX 的图片、样式和排版不提取。

PDF 解析使用 PdfPig 0.1.16，Apache-2.0 许可与原始第三方说明见 `third-party/pdfpig`。

## 构建

需要 .NET SDK 10.0.404、Uno.Sdk 7.0.22、Node.js、Windows SDK / C++ Build Tools。Android 需要 .NET Android workload、Android SDK 36、JDK 21。

在仓库根目录执行 `npm ci`，然后执行 `node scripts/build-window-effects.cjs` 和 `node scripts/build-native-renderer.cjs`。

Windows：`dotnet publish native/ChatAIBox.App/ChatAIBox/ChatAIBox.csproj -f net10.0-windows10.0.26100 -c Release -p:Platform=x64 -p:PublishTrimmed=false`。

Android：`dotnet publish native/ChatAIBox.App/ChatAIBox/ChatAIBox.csproj -f net10.0-android -c Release -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false`。使用自己的签名凭据构建独立发行版；只有原签名才能覆盖原安装包，不要卸载原版来绕过签名检查。

## 检查

`dotnet run --project native/ChatAIBox.Core.Checks` 验证加密、合并、流式输出和压缩检查点。

构建核心检查项目后，`node scripts/verify-native-interop.cjs` 检查与现有 JavaScript 客户端的 IPv6 和带访问密钥的转发互通。

以 `-p:EnableAppChecks=true` 构建 Windows 检查版，设置 `CHATAIBOX_NATIVE_CHECK_EXE` 指向其程序，再执行 `node scripts/verify-native-app.cjs`。脚本使用独立临时配置和本地合成 API，不使用真实聊天或 API Key。检查入口不会编译进普通发行包。

`ChatAIBox.Windows.Checks` 使用合成 Electron 配置检查旧设置解密和 DPAPI 存储。

## 第三方许可

鸿蒙字体保留原始许可，未经修改。Markdown 渲染依赖沿用仓库内的第三方说明。

`ChatAIBox.Edge` 为独立的 LGPL-3.0 库，包含 Edge TTS 协议实现。其源码和项目文件可用于修改、重新构建和重新链接应用，许可与原始归属见 `shared/EDGE-TTS-LICENSE.txt`、`shared/GPL-3.0.txt` 和 `shared/THIRD_PARTY_NOTICES.md`。其余依赖以各自 NuGet 包中的许可证为准。
