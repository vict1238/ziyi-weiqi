# 梓懿围棋 GPU runtime notices

The GPU package uses the unmodified official KataGo 1.18.1 OpenCL Windows x64 runtime. Keep the complete upstream DLL tree next to `katago.exe`. The NVIDIA/AMD/Intel OpenCL driver is supplied by the graphics vendor; this package does not install CUDA.

## Pinned runtime and model

| Component | Official source | SHA-256 |
| --- | --- | --- |
| KataGo OpenCL 1.18.1, `katago-v1.18.1-opencl-windows-x64.zip` (6,004,137 bytes) | https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-opencl-windows-x64.zip | `1710DB1903AB921AA6837A9599C8474F8A59F057650217C5D9BC125EE393A9FF` |
| `kata1-b18c384nbt-s9996604416-d4316597426.bin.gz` (97,898,094 bytes) | https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz | `9D7A6AFED8FF5B74894727E156F04F0CD36060A24824892008FBB6E0CBA51F1D` |

The b18 model was published on 2024-05-26 and uses 18 nested-bottleneck blocks with 384 channels. It is copied as `gpu/model.bin.gz` without changing its bytes. `runtime.json` records its original identity and checksum.

## Licenses

- **KataGo:** MIT; original `KataGo-LICENSE.txt`, `KataGo-CONTRIBUTORS.txt`, and official release `KataGo-release-README.txt` are included. Source: https://github.com/lightvector/KataGo/tree/v1.18.1 .
- **kata1 b18 model:** KataGo Neural Network License, with MIT-style permission and notice-retention requirements. The complete original page is retained as `Model-LICENSE.html`, with the applicable full notice in `Model-LICENSE.txt`. Source: https://katagotraining.org/network_license/ .
- **CLBlast:** Apache 2.0, `CLBlast-LICENSE.txt`, from https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/external/clblast/LICENSE .
- **half 2.2.0:** MIT, `half-LICENSE.txt`, from https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/external/half-2.2.0/LICENSE.txt .
- **tclap 1.2.5:** MIT, `tclap-COPYING.txt`, from https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/external/tclap-1.2.5/COPYING .
- **bzip2:** bzip2 license, `bzip2-COPYING.txt`, from https://raw.githubusercontent.com/libarchive/bzip2/master/COPYING .
- **OpenSSL, libzip, zlib, filesystem, cpp-httplib, nlohmann JSON and SHA-2 code:** their original notices are retained alongside these files. These common dependency notices are copied from the project's existing upstream notice bundle.
- **Microsoft Visual C++ runtime DLLs:** governed by Microsoft's redistributable terms; the original page is preserved as `Microsoft-VisualC-Runtime-License.html`: https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/ .

The application source has its own MIT license in the project root. The embedded CPU g170 model retains its separate CC0 dedication under `engine-assets/licenses`; the kata1 b18 model above uses the KataGo Neural Network License.

Notices were retrieved on 2026-10-20. `prepare-gpu.ps1` copies this directory into `gpu/licenses` in the portable distribution.
