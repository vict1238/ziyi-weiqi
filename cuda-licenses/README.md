# 梓懿围棋 CUDA runtime notices

This application bundles the unmodified official KataGo 1.18.1 CUDA 12.1 / cuDNN 9.8.0 Windows x64 engine, NVIDIA CUDA runtime 12.1.105, cuBLAS 12.1.3.1, NVRTC 12.1.105, and cuDNN 9.8.0.87. They run from the application's `cuda/` directory and are used by the application's KataGo subprocess. A compatible NVIDIA GPU and driver are required. This package installs no driver or system CUDA Toolkit and makes no global PATH change; Python and SDK developer tools are not required.

The models remain in the application's shared `gpu/models/` directory. Their identities, dates and model licenses are retained in `gpu/runtime.json` and `gpu/licenses/`.

## Applicable licenses and redistribution

- The application's own source is MIT licensed. **NVIDIA CUDA/cuBLAS/NVRTC/cuDNN binaries are proprietary components under the included NVIDIA terms, and are not covered by the application's MIT license.** They are provided solely for use by 梓懿围棋 and its bundled KataGo engine on NVIDIA GPUs. The NVIDIA terms, including restrictions on copying, modification, reverse engineering and independent redistribution, continue to apply. This application is not sponsored or endorsed by NVIDIA.
- `NVIDIA-CUDA-12.1-EULA.txt` is the original license from NVIDIA's CUDA 12.1.105 / cuBLAS 12.1.3.1 / NVRTC 12.1.105 wheels (the three files have identical SHA-256). Attachment A expressly lists the CUDA runtime, cuBLAS/cuBLASLt and NVRTC runtime DLLs as distributable with applications. Official versioned text: https://docs.nvidia.com/cuda/archive/12.1.1/eula/index.html .
- `NVIDIA-cuDNN-9.8-EULA.html` preserves the complete official versioned cuDNN 9.8 agreement, whose supplement identifies runtime `.so` and `.dll` files as distributable. Source: https://docs.nvidia.com/deeplearning/cudnn/backend/v9.8.0/reference/eula.html . `NVIDIA-cuDNN-9.8-Acknowledgements.html` preserves that release's third-party acknowledgements: https://docs.nvidia.com/deeplearning/cudnn/backend/v9.8.0/reference/acknowledgements.html .
- `NVIDIA-cuDNN-wheel-License.txt` is preserved unchanged from the official 9.8.0.87 wheel. Its supplement still names an older `cudnn64_7.dll`; the versioned 9.8 agreement above documents the redistribution grant for the actual 9.8 runtime `.dll` files shipped here. Neither text is rewritten or relicensed by this project.
- KataGo is MIT licensed. All original KataGo/common dependency notices from `gpu-licenses/` are also copied into `cuda/licenses/`: KataGo authors and contributors, bzip2, OpenSSL, libzip, zlib, half, tclap, Eigen, filesystem, cpp-httplib, nlohmann JSON, SHA-2 and Microsoft's Visual C++ redistributable terms. The complete upstream engine archive is retained during preparation, including its original README and every supplied DLL.
- KataGo's vendored **cuDNN frontend** is MIT licensed (`cudnn-frontend-LICENSE.txt`), source: https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/external/cudnn-frontend/LICENSE.txt . Its vendored **CUTLASS** notice is preserved as `CUTLASS-LICENSE.txt`, source: https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/external/cutlass/LICENSE.txt .

The CUDA/cuDNN redistribution grants apply to an application with material functionality beyond the SDK. 梓懿围棋 provides the Go board, rules, opponent play, analysis, SGF support, controls and interface; the runtimes are app-local dependencies used by its bundled engine. Do not separate these NVIDIA files for distribution as a general-purpose SDK or apply the project's MIT license to them.

## Reproducible runtime selection

`sources.json` pins every official archive's URL, byte length and SHA-256, plus the exact original DLL entries and their SHA-256. `prepare-cuda.ps1` checks both archive and extracted file hashes. It extracts only runtime DLLs from the NVIDIA wheels, without importing or installing the wheels or running their code.

The selected 12 NVIDIA DLLs are CUDA runtime, cuBLAS, cuBLASLt, NVRTC and its builtins, cuDNN shim, convolution, operations, graph, heuristic and both engine libraries. All GPU architecture data is retained unchanged. `cudnn_adv64_9.dll` and `nvblas64_12.dll` are omitted because KataGo does not call their legacy RNN/CTC/multihead-attention API or the drop-in CPU BLAS wrapper. See `DEPLOYMENT.md` for the source/API and isolated inference checks supporting that choice.

Notices were retrieved and checked on 2026-10-20. Keep `cuda/licenses/`, `cuda/THIRD-PARTY.txt` and the shared model notices with redistributed application packages.
