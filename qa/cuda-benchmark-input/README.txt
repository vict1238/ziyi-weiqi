Public reproducible KataGo benchmark input

Official KataGo v1.18.1 commit 92ee95c0a4b25fec214da00951ab69e97e207729
Source: https://raw.githubusercontent.com/lightvector/KataGo/92ee95c0a4b25fec214da00951ab69e97e207729/cpp/tests/testcommon.cpp
Function: TestCommon::getBenchmarkSGFData(19)

The .sgf is the exact decoded built-in 19x19 main line (276 moves), without a trailing newline. Source is retained for audit only; do not execute it.
positions.json contains three snapshots after 0, 96 and 192 moves. The first is the empty board. The full game is retained in the SGF. Positions use standard GTP columns A-H,J-T; SGF starts at top left, so aa maps to A19.
SGF gives komi 7.5 but no RU. For the analysis comparison both backends must explicitly use chinese-ogs, komi 7.5 and 19x19 board dimensions.
This replaces the missing private game. Do not mix new timing measurements with previous results from the private game.
Checksums and extraction/validation details: provenance.json. Upstream license: KataGo-LICENSE.txt.
