# 战斗吧剑灵 · 本地开发补丁

这里是本地版的源码覆盖层。仓库包含本地服务端、存档管家代码、对客户端的补丁和原创剧情素材。原版贴图、模型、音频、关卡表、客户端程序集及发行包由开发者从自备游戏文件准备，生成在 Git 忽略的 `LocalBuild/` 下。

## 从已有开发工作区重建

需要 Python 3.11 或更新版本。编译服务端另需 .NET 10 SDK。已恢复的本地开发工作区应包含 `Project/`、服务端编译引用，以及 `WindowsPreview/LocalServer/data/`。支持 v28 对应的客户端补丁输入，或已经应用相同修改的文件。不同来源的反编译文本可能不匹配校验值。

```powershell
python tools/workspace.py hydrate --workspace "D:/自己的本地开发工作区"
python tools/workspace.py build-server
python tools/workspace.py build-windows --editor "D:/Unity/Editor/Unity.exe"
```

Unity 工程位于 `LocalBuild/workspace/Project`，使用 Unity 6.3。上面的命令分别生成服务端核心和 Windows 游戏客户端。输入目录只读，输出目录必须是新目录。早期基底缺少新增的本地派生资源时，可用 `--local-extras` 指向自备补充目录，目录下按清单保留工程相对路径。脚本会列出具体缺项。成功导入的目录只供本机使用。

## 从原版 APK 提取

目前匹配 `com.tencent.tmgp.bnsm19.5.3.914.apk`，以 `tools/original-apk.json` 的 SHA-256 为准。工具不会下载游戏。

用 AssetRipper 2.0.0 的 `AssetRipper.GUI.Free.exe --headless --port 58493` 启动本地导出服务，另开终端运行下面的命令。ILSpy 的 `ilspycmd` 需在 PATH 中，也可通过 `--ilspy` 指定位置。

```powershell
python tools/original_game.py unpack --apk "D:/原版/com.tencent.tmgp.bnsm19.5.3.914.apk"
python tools/original_game.py decompile --assembly "D:/自备可读程序集/Assembly-CSharp.dll" --references LocalBuild/original/ClientData/Managed
python tools/original_game.py export-unity --ripper-port 58493 --assembly "D:/自备可读程序集/Assembly-CSharp.dll" --hot-update "D:/自己的热更新资源"
```

解包输出包含基础 Unity 数据和原版启动图标。反编译需要能由 ILSpy 打开的本地程序集，APK 里的受保护程序集不能直接交给该命令。Unity 导出用本地 AssetRipper Web 界面显示的端口。原版提取结果是恢复输入。要接入目前的客户端补丁，仍需准备与补丁匹配的源工程、修复资源引用，并提供本地服务端引用和表数据。**仅有 APK 时还不能从零重建当前可玩版。** [AssetRipper](https://github.com/AssetRipper/AssetRipper) · [ILSpy 命令行工具](https://github.com/icsharpcode/ILSpy/tree/master/ICSharpCode.ILSpyCmd)

也可以使用之前发布的本地 Windows Mono 版。先解压自己的发行包，再将包含 `Managed/` 和 Unity 数据的 `*_Data` 目录交给工具。

```powershell
python tools/original_game.py unpack-windows --game-data "D:/旧版本/BnsmLocal_Data"
python tools/original_game.py decompile --assembly LocalBuild/previous-windows/ClientData/Managed/Assembly-CSharp.dll --references LocalBuild/previous-windows/ClientData/Managed --output LocalBuild/previous-windows-source
python tools/original_game.py export-unity --input LocalBuild/previous-windows --ripper-port 58493 --output LocalBuild/previous-windows-project
```

## 仓库布局

| 目录 | 内容 |
| --- | --- |
| `overlay/` | 原创代码、测试代码和明确纳入的原创剧情素材，按目标工程相对路径排列。 |
| `patches/client.json` | 基于文件校验值应用的客户端源码变更及本地资源清单。 |
| `tools/` | 解包、反编译、导出、恢复和公开目录校验。 |
| `LocalBuild/` | 本机输入、生成的源码工作区和构建结果，默认忽略。 |

公开提交前运行 `python tools/check_public.py`。GitHub CI 同样运行该检查。维护者应从此仓库发布公开源码，不上传旧工作仓库及其 Git LFS 历史。 [维护说明](docs/maintaining.md) · [原创剧情制作流程](overlay/SourceArt/原创剧情制作流程.md)
