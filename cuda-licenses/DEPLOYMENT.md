# CUDA deployment verification — 2026-10-20

The application ships the original, unmodified KataGo 1.18.1 CUDA 12.1 / cuDNN 9.8.0 Windows x64 engine and the NVIDIA runtimes pinned in `sources.json`.

The only runtime DLL exclusions from the selected NVIDIA wheels are:

| Excluded original DLL | Uncompressed bytes | Reason |
| --- | ---: | --- |
| `cudnn_adv64_9.dll` | 253,583,928 | Legacy RNN, CTC loss and old multihead-attention API, unused by this KataGo build. |
| `nvblas64_12.dll` | 328,704 | Drop-in CPU BLAS interception wrapper; KataGo directly calls cuBLAS instead. |

The source evidence is the tagged upstream implementation:

- https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/neuralnet/cudabackend.cpp
- https://github.com/lightvector/KataGo/blob/v1.18.1/cpp/neuralnet/cudaandrocmbackend.inc

The CUDA backend calls cuDNN convolution/tensor/graph functions and cuBLAS GEMM. Its Transformer attention uses KataGo's own CUDA attention kernels or cuDNN Graph API, rather than the legacy `cudnnMultiHeadAttn*` functions. The original executable's PE imports were checked directly and contain no `cudnn_adv` or `nvblas` dependency or legacy RNN/CTC/multihead-attention symbols.

NVIDIA's versioned API overview documents the sub-library boundary: `cudnn_cnn` depends on `cudnn_ops`, `cudnn_graph` and precompiled engines; `cudnn_adv` is the separate legacy RNN/CTC/multihead-attention library. All graph, heuristic, precompiled-engine and runtime-compiled-engine DLLs are retained, as are cuBLASLt and NVRTC for alternate supported GPU/engine paths. Source: https://docs.nvidia.com/deeplearning/cudnn/backend/v9.8.0/api/overview.html .

On the tested NVIDIA GeForce RTX 4060 Laptop GPU (8 GiB), the deployment was copied to an isolated directory containing the official engine files plus only the 12 selected NVIDIA DLLs. The child process PATH contained that directory and Windows system directories; no system CUDA installation or directory from the earlier complete test runtime was used.

For **both** `kata1-tf3-b11c768-s11003M-d5973M-7gres` and `kata1-b18c384nbt-s9996604416-d4316597426`:

- Native analysis started normally and selected CUDA FP16.
- Independent empty 19×19, 9×9 and 13×13 positions completed at least 32 visits and returned ownership arrays of the correct size.
- A live process-module enumeration after inference confirmed all loaded cuDNN, cuBLAS and NVRTC DLLs came from the isolated deployment directory. Neither excluded DLL was loaded, and none was available in that directory.
- The original NVIDIA DLLs remained byte-identical to the signed files in the pinned official wheels. No fatbinary, GPU architecture or other binary modification was performed.

These checks establish the dependency selection for this KataGo build and its supported model families. They do not constitute a benchmark or an inference test on every NVIDIA GPU. Other compatible cards keep the original runtime architecture coverage and the alternative graph engine implementations.
