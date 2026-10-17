# 梓懿围棋 — bundled third-party software

梓懿围棋 bundles the following engine locally. No network service, account, Python, GPU driver, or Visual C++ runtime installation is used at runtime. The distribution needs 64-bit Windows with .NET Framework 4.8. The plain Eigen build does not require AVX2. Binaries are unchanged from the upstream archive.

## KataGo 1.14.1

- Author: David J. Wu (lightvector) and contributors.
- Source: https://github.com/lightvector/KataGo/tree/f2dc582f98a79fefeb11b2c37de7db0905318f4f
- Release: https://github.com/lightvector/KataGo/releases/tag/v1.14.1 (2024-03-10).
- Archive: https://github.com/lightvector/KataGo/releases/download/v1.14.1/katago-v1.14.1-eigen-windows-x64.zip
- Archive SHA-256: `F92BEA0453F95A02B1C9C82737815474795783A1C59EFCCB527C41CA05786854`.
- License: MIT for KataGo, with separately licensed bundled components. The complete upstream KataGo license and copyright notice are in `engine-assets/licenses/KataGo-LICENSE.txt`; contributors and upstream release README are adjacent.
- Protocol: https://github.com/lightvector/KataGo/blob/v1.14.1/docs/Analysis_Engine.md

The executable uses Eigen on the CPU. The portable upstream DLL set is bundled alongside it, including Microsoft Visual C++ runtime DLLs, OpenSSL 3, libzip, and zlib. They are redistributed from the same official KataGo release. Microsoft runtime DLLs remain governed by Microsoft's Visual C++ Redistributable license (https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/). Other upstream notices are retained in `engine-assets/licenses/`: Eigen (MPL 2.0), OpenSSL (Apache 2.0), libzip (BSD), zlib, filesystem (MIT), half (MIT), cpp-httplib (MIT), nlohmann JSON (MIT), tclap (MIT), and the SHA-2 implementation's source notice. Source notices name their original authors and conditions.

## Pretrained g170 model

- Model: `g170e-b10c128-s1141046784-d204142634.bin.gz` (10 blocks, 128 channels).
- Official download: https://katagoarchive.org/g170/neuralnets/g170e-b10c128-s1141046784-d204142634.bin.gz
- Training history: https://katagoarchive.org/g170/neuralnets/README.txt
- License: CC0 1.0 public domain dedication by Jane Street and David J. Wu, https://katagoarchive.org/g170/LICENSE.txt . Original notice retained in `engine-assets/licenses/Model-LICENSE.txt`.
- This compact 2020 model is chosen for portable CPU responsiveness. Search counts are actual engine visits, not a claimed human rank.

## Packaged file checksums

SHA-256 of each runtime asset (before embedding):

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `analysis.cfg` | 418 | `1699E98292793033D132D0FC480FDABB87D307DF7F25F50AD452BB82008FADA0` |
| `g170e-b10c128-s1141046784-d204142634.bin.gz` | 11138361 | `1A8E05A4EA3FCA20DAB79410CBB566C760767FCDD2FA0B701CFE259A84CC8B04` |
| `katago.exe` | 4176896 | `09390E6A392CB1757FBFCD12BA2070D960B193CF87B0DE5EF4A8BC3B7D601026` |
| `libcrypto-3-x64.dll` | 5148672 | `1F6560DC57D177417932D04295FCB5F5B9B3414BD3BAA75897D8E8BB49E904B7` |
| `libssl-3-x64.dll` | 776704 | `45434DB8A17A9DDF3953D85DE7E2E4D0C22866593C490E18128EE5C7931DB6D1` |
| `libz.dll` | 95232 | `F41B06AE70E84CE4215E657A9FED61B71B590423D0B19687DCED8F95D759B319` |
| `libzip.dll` | 184832 | `1C3151E9EAB8BDDFFFFD0CD180A5258B0DE6CB8E780E47F7BD61B48875C1522F` |
| `msvcp140_1.dll` | 35664 | `A61901A4D719A3E1CC4FA8F629218571330331E8DDE2EF1F05C34845B180928E` |
| `msvcp140_2.dll` | 197488 | `3D15CFE238AC1863BBE65501F05D561162F28032B80F2C8D9A1FC7B22F8445A2` |
| `msvcp140_atomic_wait.dll` | 50032 | `0FCF613254644C2EA451A693D7737741B8041F11DECB8FEFFD3B34E00220620A` |
| `msvcp140_codecvt_ids.dll` | 31600 | `A714B498AB7B4B0508E69F66CA15196DF605844D4D6D5E43969A5F1FD7D7E9E4` |
| `msvcp140.dll` | 579920 | `003DA4807ACDC912E67EDBA49BE574DAA5238BB7ACFF871D8666D16F8072FF89` |
| `vcruntime140_1.dll` | 49520 | `E4B533A94E02C574780E4B333FCF0889F65ED00D39E32C0FBBDA2116F185873F` |
| `vcruntime140.dll` | 109392 | `A8F950B4357EC12CFCCDDC9094CCA56A3D5244B95E09EA6E9A746489F2D58736` |


## Local analysis configuration and search controls

`analysis.cfg` is the application's own portable CPU configuration for the unmodified upstream binary: one simultaneous analysis, four search workers, four Eigen workers, Black-perspective scores, and a 2^16 neural cache. The application sets each query's visit target; the file's 128 visits is only a default for clients that omit a target. No `maxTime` setting or application per-analysis timeout is imposed. The 90-second model-startup watchdog does not limit an analysis after startup.

Visit targets are sent as exact signed 64-bit JSON integers, including values above Int32. KataGo 1.14.1 itself accepts 1 through 2^50 visits per query; see the upstream parser at https://github.com/lightvector/KataGo/blob/v1.14.1/cpp/command/analysis.cpp#L875-L879 . Values above that native protocol range produce a visible engine error, without silently reducing the user's number. Continuous-thinking mode uses the engine's native 2^50 budget as its manual-stop workflow. The native integer counter remains finite.

Each query requests progress every 0.5 seconds. Stop sends KataGo's native `terminate` action and awaits its final best-so-far result. Cancel discards the request when starting a new game or navigating a record. Startup resource extraction verifies SHA-256 against the embedded bytes and atomically replaces a changed configuration, so a cached older configuration cannot retain the previous time limit.
## Original KataGo license

The code in this repository currently relies on several other libraries, parts of libraries,
or external files: clblast, filesystem-1.5.8, half-2.1.0, httplib, mozilla-cacerts,
nlohmann_json, sgfmill, and tclap-1.2.2. For the licenses for those libraries and/or files, see the
individual readmes and/or license files for each one within their respective subdirectories within
cpp/external. Additionally, cpp/core/sha2.cpp derives from another piece of external code
and embeds its own license within that file.

Aside from the above, the license for all OTHER content in this repo is as follows:

----------------------------------------

Copyright 2019 David J Wu ("lightvector") and/or other authors of the content in this repository.
(See 'CONTRIBUTORS' file for a list of authors as well as other indirect contributors).

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

----------------------------------------

Additional disclaimer from David J Wu ("lightvector") regarding the above license and KataGo's
repository (https://github.com/lightvector/KataGo):
I am providing code in the repository to you under an open source license. Because this is my
personal repository, the license you receive to my code is from me, and not my employer (Facebook).
## Original g170 model license

Many thanks to Jane Street for providing much of the computation for this run of KataGo!

The data, including the models, in this directory "g170" was originally owned in part by Jane Street (data prior and up to g170-b40c256x2-s3708042240-d967973220) and in part by David J. Wu ("lightvector") (data subsequent to that point in that run). 

Each author has dedicated their parts of this data to the public domain under CC0:
https://creativecommons.org/publicdomain/zero/1.0/
waiving all copyright and related rights in this data to the extent permitted by law.

Accordingly, this data is being redistributed freely for download here.

--
THIS DATA IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR DISTRIBUTORS OF THIS DATA BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE DATA OR THE USE OR OTHER DEALINGS IN THE DATA.


