# AI 娘图标换装 v1.0.0

Windows 桌面快捷方式图标换装工具，内置 ChatGPT、Claude、Gemini、Grok、DeepSeek 五款角色。

## 下载与使用

1. 在下方 **Assets** 下载 **AI-Mascot-Icons-v1.0.0-windows.zip**。
2. 全部解压，双击 **AI-Mascot-Icons.exe**。
3. 勾选快捷方式、选择角色，然后应用；需要恢复时勾选原项目并点恢复。

程序包约 2.3 MB，免安装、离线使用，不需要登录或 API Key。自动保留首次修改前的完整备份，支持自定义 ICO 和批量换装。

## 适用范围

支持 Windows 本地 `.lnk` 快捷方式，需要 .NET Framework 4.7.2+。现代 Windows 10/11 通常自带。实际验证环境为 Windows 11；不修改应用 EXE，不启动 Claude 或其他 AI 客户端。运行时任务栏图标可能仍由应用决定。

40 项本地检查、Windows Shell 图标显示、备份恢复及分发包完整性验证已通过。完整鼠标点击流程和 Windows 10/ARM64 等环境尚未验收，详见 [VALIDATION.md](https://github.com/Eclipse017/AI-Mascot-Icons/blob/main/VALIDATION.md)。当前 EXE 未做商业代码签名。

代码采用 MIT；角色素材另见 [ASSETS.md](https://github.com/Eclipse017/AI-Mascot-Icons/blob/main/ASSETS.md)。非官方爱好项目。

附件包含 Windows 程序包、完整源码包及 SHA256SUMS.txt 校验值。普通用户下载 windows.zip 即可。
