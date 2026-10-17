# 梓懿围棋

**致敬六大至尊围棋王“梓”和“懿”。**

梓懿围棋是一款可离线运行的 Windows 围棋对弈与复盘工具，使用本机 KataGo 计算着法。当前版本为 **1.1.0**。

[下载最新版本](https://github.com/vict1238/ziyi-weiqi/releases/latest) · [版本记录](CHANGELOG.md) · [使用说明](使用说明.txt)

![梓懿围棋桌面界面](qa/desktop-preview.png)

## 下载

| 版本 | 单文件程序 | Windows 便携包 | 完整源码 |
| --- | --- | --- | --- |
| [1.1.0 · 梓懿围棋](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.1.0) | [EXE](https://github.com/vict1238/ziyi-weiqi/releases/download/v1.1.0/ziyi-weiqi-v1.1.0.exe) | [ZIP](https://github.com/vict1238/ziyi-weiqi/releases/download/v1.1.0/ziyi-weiqi-Windows-x64-v1.1.0.zip) | [源码 ZIP](https://github.com/vict1238/ziyi-weiqi/releases/download/v1.1.0/ziyi-weiqi-source-v1.1.0.zip) |

下载文件使用英文名，便携包中的主程序为 `梓懿围棋.exe`。校验文件：[SHA-256](https://github.com/vict1238/ziyi-weiqi/releases/download/v1.1.0/SHA256SUMS.txt)。

## 功能

- 人机对弈：执黑、执白、AI 自动应手，也可让 AI 自弈或双方自由摆谱。
- 9 路、13 路、19 路棋盘，中国 / 日本规则、贴目和让子设置。
- 推荐着点、黑方胜率、目差及领地归属估计，搜索时实时更新。
- 快速 / 标准 / 深入三档计算量，以及自定义搜索次数、手动结束持续思考。
- 悔棋、停一手、逐手复盘，打开与保存 SGF，读取主变化、初始摆子和先行方。
- 原生 WinForms 界面，内置 KataGo **1.14.1 Eigen CPU** 引擎与 **b10c128** 模型。

## 运行

需要 **Windows 10 / 11 x64、.NET Framework 4.8**。从 [Releases](https://github.com/vict1238/ziyi-weiqi/releases/latest) 下载 Windows 压缩包，解压后双击 `梓懿围棋.exe`。

引擎、模型和运行依赖随程序提供；下棋无需联网、账号、Python 或显卡配置。首次请求 AI 时会释放引擎并载入模型，后续请求复用同一进程。

默认人执黑、19 路、AI 自动应手。选择执白后 AI 先行；选择双方可自由复盘。方向键切换前后手，`Home` / `End` 跳到首手 / 末手，`Ctrl+O` / `Ctrl+S` 打开 / 保存棋谱。回看中落子会覆盖该位置之后的原有变化。

### 计算深度

快速、标准、深入分别使用 **16 / 64 / 256 次搜索**，表示计算量。选择“自定义”，或从“规则 / 设置 → 设置计算深度”打开设置：

- 输入正整数，每步按该目标计算。KataGo 1.14.1 原生单次范围为 **1 至 1,125,899,906,842,624（2⁵⁰）**；超出范围会明确提示，程序不会静默截断或缩小输入。
- 选择“持续思考”后，由你手动结束。该模式使用引擎原生 **2⁵⁰** 搜索预算，计数仍然有限；正常使用按手动停止的流程操作。
- 点击“结束思考并落子”采用当前最佳着法；推荐与领地分析中点击“结束分析”，保留当时的分析结果。

分析不设置固定时长；较大的目标会持续占用 CPU。胜率、目差和领地是 AI 估计。新局、悔棋、棋谱导航和关闭程序会取消对应计算，旧结果不会落到新局面。

引擎缓存位于 `%LOCALAPPDATA%\GoDesk\engine`，错误日志位于 `%LOCALAPPDATA%\GoDesk\logs`。第三方许可可从“帮助 → 第三方许可”打开。

## 从源码构建

在 Windows 上安装 .NET Framework 4.8，并确保系统提供 `Framework64\v4.0.30319\csc.exe`。仓库已包含构建所需的引擎资源，无需额外 NuGet 包。在项目根目录打开 PowerShell：

```powershell
.\build.ps1
```

输出：`release\梓懿围棋-v1.1.0\梓懿围棋.exe`。构建脚本将引擎、模型及许可嵌入 EXE。

执行规则、SGF、搜索次数解析和真实 KataGo 引擎检查：

```powershell
.\build.ps1 -Checks
```

执行桌面交互检查并生成报告与截图：

```powershell
Start-Process -FilePath '.\release\梓懿围棋-v1.1.0\梓懿围棋.exe' `
  -ArgumentList '--ui-test', '.\qa\ui-test.txt' -WindowStyle Hidden -Wait
Get-Content .\qa\ui-test.txt
```

检查报告写入 `qa`；已有验证说明见 [验证记录](验证记录.txt)。

## 目录

```text
src/
  GoGame.cs             围棋规则、历史与 SGF
  KataGoEngine.cs       本地引擎、流式分析与停止 / 取消
  MainForm.cs           桌面界面与对弈流程
  BoardControl.cs       棋盘、棋子与分析叠加绘制
  SearchDepth.cs        自定义搜索次数解析与原生范围校验
  *Tests.cs、UiSmoke.cs  规则、搜索次数与桌面交互检查
engine-assets/          KataGo、模型、依赖及原始许可
examples/               示例 SGF
qa/                     验证报告与截图
build.ps1               Windows 构建及检查入口
```

## 开源与致谢

应用源码采用 [MIT 许可](LICENSE.txt)。内置 KataGo、模型和依赖分别遵循其原有许可，详见 [第三方说明](THIRD-PARTY.md) 与 [原始许可文件](engine-assets/licenses)。

感谢 [KataGo](https://github.com/lightvector/KataGo) 提供本地围棋引擎与开放模型。本项目的棋盘与侧栏布局受到 [KataGui](https://github.com/hauensteina/katagui) 启发，桌面界面与规则代码独立编写。
