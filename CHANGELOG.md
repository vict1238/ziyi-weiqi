# 版本记录

## v2.0.0 · 2026-11-06

- 加入 Tailscale 和局域网好友房。切磋房支持悔棋确认、认输和断线续弈，开局后锁定本机 AI。
- 研讨房允许双方摆棋、导入棋谱、试下和共享 KataGo 推荐。
- SGF 整盘分析显示一选率、恶手率、目损和问题手。可以暂停续算，并加深关键手。

[下载 v2.0.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v2.0.0)

## v1.8.1 · 2026-11-05

- 修复“AI 自动应手”和“显示手数”开关里的重叠文字。
- 最大化时，棋盘下方的操作随窗口宽度居中。

[下载 v1.8.1](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.8.1)

## v1.8.0 · 2026-11-05（已撤回）

- 换用木纹棋盘和分组侧栏，加入实测胜率历史。
- 截图识别增加拖入图片和分步校正。

## v1.7.0 · 2026-11-05

- 支持从截图和实体棋盘照片摆棋。可校正四角、摆子擦除，并把局部图放回完整棋盘。

[下载 v1.7.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.7.0)

## v1.6.0 · 2026-10-27

- 可框选局部研究，并对照局部与全盘的推荐。
- 现存棋子可显示原始手数。

[下载 v1.6.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.6.0)

## v1.5.0 · 2026-10-22

- 支持黑让白或白让黑 0–9 子。SGF 可保存预摆子和先行方。

[下载 v1.5.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.5.0)

## v1.4.0 · 2026-10-20

- 加入 CUDA 后端。此版完整包提供后续升级所用的 CUDA、OpenCL 和 GPU 模型。
- RTX 4060 Laptop 上，三档搜索的 CUDA 耗时中位数约为 OpenCL 的三分之一。原始数据见仓库的 CUDA 测速记录。

[下载 v1.4.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.4.0)

## v1.3.0 · 2026-10-20

- GPU 默认改用 2026-09-25 上传的 tf3 模型，可切回 b18。
- 新增 256 至 16384 次的六档搜索预设。

[下载 v1.3.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.3.0)

## v1.2.0 · 2026-10-20

- 加入 OpenCL、引擎和模型选择。
- 棋盘列包含 I，行号从左上角开始。支持野狐贴目编码和落子音量设置。

[下载 v1.2.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.2.0)

## v1.1.0 · 2026-10-17

- 首个公开版。内置 CPU KataGo，支持单机对弈、推荐着点、逐手回看和 SGF。
- 支持自定义搜索次数和持续思考。

[下载 v1.1.0](https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.1.0)
