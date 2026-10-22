梓懿围棋 v1.5.0 升级说明

规则 / 设置中选择让子方向与0–9子，确定后点“新对局”。
白让黑：黑棋先摆，白方先行。黑让白：白棋先摆，黑方先行。
0为分先；1子放天元；2–9子按标准星位摆放。贴目独立设置。

已有v1.4.0完整GPU包：关闭旧程序后，用新“梓懿围棋.exe”替换原目录主程序，保留原cuda、gpu文件夹，继续使用原运行库和模型。
初次使用GPU：先下载并解压v1.4.0完整CUDA/OpenCL基础包，再用此升级包更新主程序与说明。
GPU基础包：https://github.com/vict1238/ziyi-weiqi/releases/download/v1.4.0/ziyi-weiqi-Windows-CUDA-v1.4.0.zip
单独运行EXE可使用内置CPU引擎。

本版已实现双向让子；人对人联机仍需另建房间服务，具体方案见源码docs/ONLINE-PLAY.md。
项目与更新：https://github.com/vict1238/ziyi-weiqi
本版发布：https://github.com/vict1238/ziyi-weiqi/releases/tag/v1.5.0
