using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GoDesk
{
    public sealed class MoveCandidate
    {
        public string Move { get; set; }
        // All probabilities, scores, and ownership values use Black's perspective.
        public double Winrate { get; set; }
        public double ScoreLead { get; set; }
        public long Visits { get; set; }
        public int Order { get; set; }
    }

    public sealed class AnalysisResult
    {
        public string BestMove { get; set; }
        public double BlackWinrate { get; set; }
        public double BlackScoreLead { get; set; }
        public long Visits { get; set; }
        public List<MoveCandidate> Candidates { get; set; }
        // Row-major, top-left to bottom-right: +1 Black, -1 White.
        public double[] Ownership { get; set; }
    }

    public sealed class KataGoEngine : IDisposable
    {
        private const string ModelFile = "g170e-b10c128-s1141046784-d204142634.bin.gz";
        // KataGo's native JSON analysis limit; zero means think until Stop().
        private const long NativeContinuousVisits = 1L << 50;
        private readonly EngineSettings settings;
        private readonly string engineName;
        private readonly object sync = new object();
        private readonly SemaphoreSlim startGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, PendingRequest> pending = new Dictionary<string, PendingRequest>();
        private readonly Queue<string> recentErrors = new Queue<string>();
        private Process process;
        private TaskCompletionSource<bool> readySignal;
        private string readyId;
        private string status;
        private CancellationTokenSource startupCancellation;
        private bool ready;
        private bool disposed;
        private int generation;
        private int stopGeneration;

        public KataGoEngine() : this(EngineSettings.CreateCpu()) { }

        public KataGoEngine(EngineSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings.Clone();
            string backend = this.settings.NormalizedBackend;
            engineName = backend == "cpu" ? "KataGo CPU" : backend == "gpu" ? "KataGo GPU · OpenCL" :
                backend == "cuda" ? "KataGo GPU · CUDA" : "自定义 KataGo";
            status = engineName + " 引擎尚未启动";
            // Path existence is deliberately deferred to startup, allowing the UI
            // to repair a saved profile after moving/removing an external model.
        }

        private sealed class PendingRequest
        {
            public readonly TaskCompletionSource<AnalysisResult> Completion =
                new TaskCompletionSource<AnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Size;
            public Action<AnalysisResult> Progress;
        }

        public string Status { get { lock (sync) return status; } }
        public bool IsReady { get { lock (sync) return ready && !disposed; } }
        // Raised on a worker thread. Desktop consumers should marshal to their UI thread.
        public event Action<string> StatusChanged;

        public async Task<AnalysisResult> AnalyzeAsync(GoGame game, long maxVisits, bool ownership, Action<AnalysisResult> progress = null)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (maxVisits < 0) throw new ArgumentOutOfRangeException("maxVisits", "搜索次数必须为正整数，或以 0 表示持续思考。");

            // Snapshot every model field synchronously, before the first await. Undo,
            // new-game, and SGF navigation can mutate the original game while we run.
            int size = game.Size;
            double komi = game.Komi;
            string rules = game.Rules;
            Stone initialPlayer = game.InitialPlayer;
            Stone toPlay = game.ToPlay;
            var moves = game.Moves.Take(game.Position)
                .Select(m => new string[] { Color(m.Color), m.ToGtp(size) }).ToArray();
            var initialStones = game.InitialStones
                .Select(m => new string[] { Color(m.Color), m.ToGtp(size) }).ToArray();
            int requestGeneration;
            int requestStopGeneration;
            lock (sync)
            {
                ThrowIfDisposed();
                requestGeneration = generation;
                requestStopGeneration = stopGeneration;
            }
            if (size < 2 || size > 19) throw new ArgumentOutOfRangeException("game", "KataGo 支持 2 至 19 路棋盘。");

            await EnsureStartedAsync(requestGeneration).ConfigureAwait(false);
            string id = "analysis-" + Guid.NewGuid().ToString("N");
            var query = new Dictionary<string, object>
            {
                { "id", id }, { "moves", moves }, { "initialStones", initialStones },
                { "initialPlayer", Color(initialPlayer == Stone.Empty ? (moves.Length == 0 ? toPlay : Stone.Black) : initialPlayer) },
                { "rules", rules }, { "komi", komi },
                { "boardXSize", size }, { "boardYSize", size },
                { "maxVisits", maxVisits == 0 ? NativeContinuousVisits : maxVisits }, { "includeOwnership", ownership },
                { "analysisPVLen", 10 }, { "reportDuringSearchEvery", 0.5 }
            };
            var request = new PendingRequest { Size = size, Progress = progress };
            try
            {
                lock (sync)
                {
                    ThrowIfDisposed();
                    if (requestGeneration != generation) throw new TaskCanceledException("分析已取消。");
                    if (!ready || process == null || process.HasExited)
                        throw new InvalidOperationException("KataGo 进程已退出，请重新分析。");
                    pending.Add(id, request);
                    WriteLocked(query);
                    // Stop pressed while model startup was in progress must also stop
                    // this request once it reaches the native engine.
                    if (requestStopGeneration != stopGeneration) TryTerminateLocked(id);
                }
                if (!request.Completion.Task.IsCompleted)
                    SetStatus(maxVisits == 0 ? engineName + " 持续思考中，可随时停止" :
                        engineName + " 正在思考（目标 " + maxVisits.ToString(CultureInfo.InvariantCulture) + " 次搜索）");
                return await request.Completion.Task.ConfigureAwait(false);
            }
            catch
            {
                lock (sync) pending.Remove(id);
                throw;
            }
        }

        private async Task EnsureStartedAsync(int requestGeneration)
        {
            await startGate.WaitAsync().ConfigureAwait(false);
            Process attempted = null;
            CancellationTokenSource cancellation = null;
            try
            {
                Process previous;
                lock (sync)
                {
                    ThrowIfDisposed();
                    if (requestGeneration != generation) throw new TaskCanceledException("分析已取消。");
                    if (ready && process != null && !process.HasExited) return;
                    cancellation = new CancellationTokenSource();
                    startupCancellation = cancellation;
                    previous = process;
                    process = null;
                    ready = false;
                }
                if (previous != null) { StopProcess(previous); previous.Dispose(); }
                SetStatus("正在准备 " + engineName + " 引擎…");
                Task<EngineLaunchPaths> preparation = Task.Run(() => PrepareEngineFiles());
                // Hashing/extracting a model (or waiting for another instance's
                // extraction mutex) must not hold up a canceled UI operation.
                ObserveBackgroundFailure(preparation);
                await Task.WhenAny(preparation, Task.Delay(Timeout.Infinite, cancellation.Token)).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                EngineLaunchPaths paths = await preparation.ConfigureAwait(false);
                var info = new ProcessStartInfo
                {
                    FileName = paths.Executable,
                    Arguments = "analysis -config " + QuotePath(paths.Config) + " -model " + QuotePath(paths.Model) + " -quit-without-waiting",
                    WorkingDirectory = paths.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (settings.NormalizedBackend == "cuda")
                {
                    // Scope DLL discovery to this child process. Installing a
                    // system CUDA Toolkit or changing Windows PATH is unnecessary.
                    string inheritedPath = info.EnvironmentVariables["PATH"] ?? "";
                    info.EnvironmentVariables["PATH"] = Path.GetDirectoryName(paths.Executable) + Path.PathSeparator + inheritedPath;
                }
                Process launched = new Process { StartInfo = info, EnableRaisingEvents = true };
                attempted = launched;
                TaskCompletionSource<bool> startup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                launched.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) OnOutput(launched, e.Data);
                };
                launched.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) OnStderr(launched, e.Data);
                };
                launched.Exited += delegate { OnExited(launched); };
                lock (sync)
                {
                    ThrowIfDisposed();
                    cancellation.Token.ThrowIfCancellationRequested();
                    recentErrors.Clear();
                    process = launched;
                    readySignal = startup;
                    readyId = "startup-" + Guid.NewGuid().ToString("N");
                    ready = false;
                    try
                    {
                        if (!launched.Start()) throw new InvalidOperationException("无法启动 KataGo。");
                        launched.BeginOutputReadLine();
                        launched.BeginErrorReadLine();
                        WriteLocked(new Dictionary<string, object> { { "id", readyId }, { "action", "query_version" } });
                    }
                    catch (Exception error)
                    {
                        process = null;
                        var windowsError = error as System.ComponentModel.Win32Exception;
                        if (windowsError != null)
                            throw new InvalidOperationException(DescribeLaunchFailure(settings.NormalizedBackend, windowsError.NativeErrorCode, windowsError.Message), error);
                        throw;
                    }
                }
                string selectedBackend = settings.NormalizedBackend;
                bool cpu = selectedBackend == "cpu";
                SetStatus(cpu ? "正在加载 CPU 围棋模型…" : settings.IsGpu ?
                    "正在加载 " + (selectedBackend == "cuda" ? EngineSettings.CudaBackendDescription : EngineSettings.GpuBackendDescription) +
                    " 模型；首次初始化可能需要数分钟，可取消…" : "正在加载自定义引擎与模型；可取消…");
                int timeoutMilliseconds = cpu ? 90000 : 300000;
                Task completed = await Task.WhenAny(startup.Task, Task.Delay(timeoutMilliseconds, cancellation.Token)).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                if (completed != startup.Task)
                {
                    StopProcess(launched);
                    throw new TimeoutException(cpu ? "CPU 模型加载超过 90 秒，请检查模型后重试。" :
                        engineName + " 加载或调优超过 5 分钟，请检查驱动、引擎与模型。可在设置中主动切换 CPU。");
                }
                await startup.Task.ConfigureAwait(false);
                lock (sync)
                {
                    ThrowIfDisposed();
                    if (process != launched || launched.HasExited)
                        throw new InvalidOperationException("KataGo 在加载过程中退出。");
                    ready = true;
                }
                SetStatus(engineName + " 引擎就绪");
            }
            catch (Exception ex)
            {
                if (attempted != null)
                {
                    lock (sync) { if (process == attempted) { process = null; ready = false; } }
                    StopProcess(attempted);
                    attempted.Dispose();
                }
                SetStatus(ex is OperationCanceledException ? engineName + " 启动已取消" : engineName + " 启动失败：" + ex.Message);
                throw;
            }
            finally
            {
                lock (sync) { if (startupCancellation == cancellation) startupCancellation = null; }
                if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); }
                startGate.Release();
            }
        }

        private void OnOutput(Process source, string line)
        {
            PendingRequest request = null;
            TaskCompletionSource<bool> startup = null;
            Exception failure = null;
            AnalysisResult result = null;
            bool nowIdle = false;
            bool partial = false;
            string responseId = null;
            try
            {
                var message = Serializer().DeserializeObject(line) as Dictionary<string, object>;
                if (message == null) return;
                string id = Text(message, "id");
                responseId = id;
                lock (sync)
                {
                    if (source != process || disposed) return;
                    if (id == readyId && message.ContainsKey("version")) startup = readySignal;
                    if (message.ContainsKey("warning")) return;
                    if (message.ContainsKey("error"))
                    {
                        failure = new InvalidOperationException("KataGo：" + Text(message, "error"));
                        if (String.IsNullOrEmpty(id))
                        {
                            // Engine-wide protocol failure: fail every waiting request.
                            FailAllLocked(failure);
                            if (readySignal != null) readySignal.TrySetException(failure);
                            return;
                        }
                        if (id == readyId) startup = readySignal;
                    }
                    partial = message.ContainsKey("isDuringSearch") && Convert.ToBoolean(message["isDuringSearch"], CultureInfo.InvariantCulture);
                    if (pending.TryGetValue(id, out request))
                    {
                        if (!partial)
                        {
                            pending.Remove(id);
                            nowIdle = pending.Count == 0;
                        }
                    }
                }
                if (startup != null)
                {
                    if (failure != null) startup.TrySetException(failure); else startup.TrySetResult(true);
                }
                if (request == null) return; // Late output from a canceled request, or an action acknowledgment.
                if (partial)
                {
                    if (failure == null && request.Progress != null && !request.Completion.Task.IsCompleted)
                    {
                        result = ParseResult(message, request.Size);
                        try { request.Progress(result); } catch { /* Observers must not stop the native reader. */ }
                    }
                    return;
                }
                if (failure == null)
                {
                    if (message.ContainsKey("noResults") && Convert.ToBoolean(message["noResults"], CultureInfo.InvariantCulture))
                        request.Completion.TrySetCanceled();
                    else
                    {
                        result = ParseResult(message, request.Size);
                        request.Completion.TrySetResult(result);
                    }
                }
                else request.Completion.TrySetException(failure);
                if (nowIdle) SetStatus(failure == null ? engineName + " 引擎就绪" : failure.Message);
            }
            catch (Exception ex)
            {
                if (request != null)
                {
                    lock (sync)
                    {
                        PendingRequest active;
                        if (responseId != null && pending.TryGetValue(responseId, out active) && active == request)
                        {
                            pending.Remove(responseId);
                            TryTerminateLocked(responseId);
                        }
                    }
                    request.Completion.TrySetException(new InvalidOperationException("无法读取 KataGo 分析结果。", ex));
                    SetStatus("AI 分析结果读取失败，请重试");
                }
                else OnStderr(source, "协议读取错误：" + ex.Message);
            }
        }

        private static AnalysisResult ParseResult(Dictionary<string, object> message, int size)
        {
            object rootObject;
            if (!message.TryGetValue("rootInfo", out rootObject)) throw new InvalidDataException("结果缺少 rootInfo。");
            var root = rootObject as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("rootInfo 格式错误。");
            var candidates = new List<MoveCandidate>();
            object movesObject;
            if (message.TryGetValue("moveInfos", out movesObject))
            {
                foreach (object item in (IEnumerable)movesObject)
                {
                    var move = item as Dictionary<string, object>;
                    if (move == null) continue;
                    candidates.Add(new MoveCandidate { Move = Text(move, "move"), Winrate = Number(move, "winrate"),
                        ScoreLead = Number(move, "scoreLead"), Visits = Integer(move, "visits"), Order = (int)Number(move, "order") });
                }
            }
            candidates.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (candidates.Count == 0 || String.IsNullOrEmpty(candidates[0].Move))
                throw new InvalidDataException("KataGo 没有返回可用着法。");
            double[] ownership = null;
            object ownershipObject;
            if (message.TryGetValue("ownership", out ownershipObject))
            {
                ownership = ((IEnumerable)ownershipObject).Cast<object>()
                    .Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToArray();
                if (ownership.Length != size * size) throw new InvalidDataException("领地数组长度与棋盘不符。");
            }
            return new AnalysisResult { BestMove = candidates[0].Move, BlackWinrate = Number(root, "winrate"),
                BlackScoreLead = Number(root, "scoreLead"), Visits = Integer(root, "visits"),
                Candidates = candidates, Ownership = ownership };
        }

        private void OnStderr(Process source, string line)
        {
            lock (sync)
            {
                if (source != process || disposed) return;
                recentErrors.Enqueue(line);
                while (recentErrors.Count > 8) recentErrors.Dequeue();
            }
        }

        private void OnExited(Process source)
        {
            string detail;
            lock (sync)
            {
                if (source != process) return;
                ready = false;
                process = null;
                string reported = recentErrors.Count == 0 ? "进程已退出" : String.Join(" | ", recentErrors);
                int exitCode;
                try { exitCode = source.ExitCode; } catch (InvalidOperationException) { exitCode = 0; }
                detail = DescribeProcessExit(settings.NormalizedBackend, exitCode, reported);
                var error = new InvalidOperationException(engineName + " 已退出：" + detail);
                FailAllLocked(error);
                if (readySignal != null) readySignal.TrySetException(error);
            }
            SetStatus(engineName + " 已退出，下次分析将重新启动：" + detail);
            source.Dispose();
        }

        // Finish the active searches with the best result found so far. Unlike Cancel,
        // this keeps their tasks alive for KataGo's final response and any chosen move.
        public void Stop()
        {
            bool stopping;
            bool loading;
            lock (sync)
            {
                if (disposed) return;
                stopGeneration++;
                stopping = pending.Count > 0;
                loading = !ready && startupCancellation != null;
                // Before a model is ready there is no best-so-far result to keep.
                // A stop during lengthy OpenCL tuning cancels that startup promptly.
                if (loading) startupCancellation.Cancel();
                foreach (string id in pending.Keys) TryTerminateLocked(id);
            }
            if (loading) SetStatus(engineName + " 加载已取消");
            else if (stopping) SetStatus("正在停止思考并整理结果…");
        }

        public void Cancel()
        {
            lock (sync)
            {
                if (disposed) return;
                generation++;
                if (startupCancellation != null) startupCancellation.Cancel();
                foreach (var pair in pending)
                {
                    TryTerminateLocked(pair.Key);
                    pair.Value.Completion.TrySetCanceled();
                }
                pending.Clear();
            }
            SetStatus(IsReady ? engineName + " 引擎就绪" : engineName + " 分析已取消");
        }

        private void TryTerminateLocked(string id)
        {
            try
            {
                if (process != null && !process.HasExited)
                    WriteLocked(new Dictionary<string, object> { { "id", "cancel-" + Guid.NewGuid().ToString("N") },
                        { "action", "terminate" }, { "terminateId", id } });
            }
            catch (IOException) { }
            catch (InvalidOperationException) { }
        }

        private void WriteLocked(object query)
        {
            process.StandardInput.WriteLine(Serializer().Serialize(query));
            process.StandardInput.Flush();
        }

        private void FailAllLocked(Exception error)
        {
            foreach (PendingRequest request in pending.Values) request.Completion.TrySetException(error);
            pending.Clear();
        }

        private void SetStatus(string value)
        {
            Action<string> handler;
            lock (sync)
            {
                if (disposed || status == value) return;
                status = value;
                handler = StatusChanged;
            }
            if (handler != null) { try { handler(value); } catch { /* A UI event must not kill the reader. */ } }
        }

        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException("KataGoEngine"); }
        private static string Color(Stone stone) { return stone == Stone.White ? "W" : "B"; }
        private static string Text(Dictionary<string, object> item, string name)
        { object value; return item.TryGetValue(name, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        private static double Number(Dictionary<string, object> item, string name)
        { object value; return item.TryGetValue(name, out value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0; }
        private static long Integer(Dictionary<string, object> item, string name)
        { object value; return item.TryGetValue(name, out value) ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0; }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 }; }

        private static void ObserveBackgroundFailure(Task task)
        {
            task.ContinueWith(failed => { var observed = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        internal static string DescribeLaunchFailure(string backend, int errorCode, string detail)
        {
            if (errorCode == 4551)
                return "Windows 应用控制策略阻止了 KataGo（错误 4551）。请查看 Windows Security 的应用控制拦截记录，确认程序来自本发布包后手动处理；也可在 AI 引擎设置中选择 OpenCL 或 CPU。";
            if (backend == "cuda" && (errorCode == 2 || errorCode == 126 || errorCode == 193))
                return "CUDA 引擎或运行库缺失/不匹配（错误 " + errorCode + "）。请完整解压 GPU 包，保留 cuda 文件夹内的全部 DLL；也可在 AI 引擎设置中选择 OpenCL。原始信息：" + detail;
            return "无法启动 KataGo（Windows 错误 " + errorCode + "）：" + detail;
        }

        internal static string DescribeProcessExit(string backend, int exitCode, string detail)
        {
            uint code = unchecked((uint)exitCode);
            if (code == 0xC0000428)
                return "Windows 应用控制或签名策略阻止了 KataGo。请检查 Windows Security 的拦截记录，确认程序来源后手动处理；也可在设置中选择 OpenCL 或 CPU。";
            if (backend == "cuda")
            {
                if (code == 0xC0000135 || code == 0xC0000139 || code == 0xC000007B)
                    return "CUDA 运行库缺失或版本不匹配（退出码 0x" + code.ToString("X8", CultureInfo.InvariantCulture) +
                        "）。请完整解压 GPU 包，保留 cuda 文件夹内的全部 DLL；若仍失败，请安装/修复 Microsoft Visual C++ 2015–2022 x64 运行库，或在设置中选择 OpenCL。";
                string reported = detail ?? "";
                if (reported.IndexOf("driver version is insufficient", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reported.IndexOf("cudaErrorInsufficientDriver", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "NVIDIA 显卡驱动不支持此 CUDA 运行库，请更新 NVIDIA 驱动，或在设置中选择 OpenCL。原始信息：" + reported;
                if (reported.IndexOf("no CUDA-capable device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reported.IndexOf("cudaErrorNoDevice", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "没有找到可用的 NVIDIA CUDA 显卡，请检查 NVIDIA 驱动，或在设置中选择 OpenCL/CPU。原始信息：" + reported;
            }
            return detail;
        }

        private sealed class EngineLaunchPaths
        {
            public string Executable;
            public string Model;
            public string Config;
            public string WorkingDirectory;
        }

        private EngineLaunchPaths PrepareEngineFiles()
        {
            if (!Environment.Is64BitOperatingSystem) throw new PlatformNotSupportedException("此版本的 KataGo 需要 64 位 Windows。");
            settings.Validate();
            string backend = settings.NormalizedBackend;
            string executable = settings.ResolvedEnginePath;
            string model = settings.ResolvedModelPath;
            if (backend == "cpu" && (String.IsNullOrEmpty(executable) || String.IsNullOrEmpty(model)))
            {
                string embedded = ExtractEmbeddedEngine();
                if (String.IsNullOrEmpty(executable)) executable = Path.Combine(embedded, "katago.exe");
                if (String.IsNullOrEmpty(model)) model = Path.Combine(embedded, ModelFile);
            }
            // Validate() requires concrete GPU/custom paths; no CPU fallback is
            // performed when a driver, external binary, or external model fails.
            string working = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GoDesk", "engine", "work", backend);
            Directory.CreateDirectory(working);
            string configuration = BuildAnalysisConfiguration(backend);
            string fingerprint;
            using (var hash = SHA256.Create())
                fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(configuration))).Replace("-", "").Substring(0, 16);
            string config = Path.Combine(working, "analysis-" + fingerprint + ".cfg");
            WriteConfiguration(config, configuration);
            return new EngineLaunchPaths { Executable = executable, Model = model, Config = config, WorkingDirectory = working };
        }

        internal static string BuildAnalysisConfiguration(string backend)
        {
            bool cpu = backend == "cpu";
            var config = new StringBuilder();
            config.AppendLine("# Generated by GoDesk for KataGo JSON analysis.");
            config.AppendLine("logToStderr = true");
            config.AppendLine("logAllRequests = false");
            config.AppendLine("logAllResponses = false");
            config.AppendLine("reportAnalysisWinratesAs = BLACK");
            config.AppendLine("numAnalysisThreads = 1");
            config.AppendLine("numSearchThreadsPerAnalysisThread = " + (cpu ? "4" : "16"));
            if (cpu) config.AppendLine("numEigenThreadsPerModel = 4");
            config.AppendLine("nnMaxBatchSize = " + (cpu ? "4" : "32"));
            config.AppendLine("nnCacheSizePowerOfTwo = " + (cpu ? "16" : "18"));
            config.AppendLine("nnMutexPoolSizePowerOfTwo = " + (cpu ? "12" : "14"));
            config.AppendLine("maxVisits = 128");
            config.AppendLine("wideRootNoise = 0.0");
            config.AppendLine("analysisPVLen = 10");
            config.AppendLine("rootSymmetryPruning = true");
            return config.ToString();
        }

        private static void WriteConfiguration(string path, string configuration)
        {
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == configuration) return;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(configuration);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else
                {
                    try { File.Move(temporary, path); }
                    catch (IOException) { if (File.Exists(path)) File.Replace(temporary, path, null); else throw; }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string QuotePath(string path)
        {
            if (String.IsNullOrEmpty(path) || path.IndexOf('"') >= 0) throw new ArgumentException("引擎参数路径无效。");
            return "\"" + path + "\"";
        }

        private static string ExtractEmbeddedEngine()
        {
            if (!Environment.Is64BitOperatingSystem) throw new PlatformNotSupportedException("此版本的 KataGo 需要 64 位 Windows。");
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GoDesk", "engine", "katago-1.14.1-b10-v1");
            Directory.CreateDirectory(directory);
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("engine.", StringComparison.Ordinal)).ToArray();
            if (resources.Length == 0) throw new FileNotFoundException("应用程序未包含 KataGo 引擎资源，请重新构建完整版本。");
            using (var extractionLock = new Mutex(false, @"Local\GoDesk-KataGo-1.14.1-b10-v1"))
            {
                bool acquired = false;
                try
                {
                    try { acquired = extractionLock.WaitOne(60000); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new TimeoutException("另一个窗口正在准备 AI 引擎，请稍后重试。");
                    foreach (string resource in resources)
                    {
                        string filename = resource.Substring("engine.".Length);
                        if (filename.Length == 0 || filename != Path.GetFileName(filename) ||
                            filename.Any(c => !(Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) || filename.Contains(".."))
                            throw new InvalidDataException("引擎资源文件名无效。");
                        string destination = Path.Combine(directory, filename);
                        using (Stream embedded = assembly.GetManifestResourceStream(resource))
                        {
                            byte[] expected;
                            using (var hash = SHA256.Create()) expected = hash.ComputeHash(embedded);
                            bool matches = false;
                            if (File.Exists(destination))
                            {
                                using (Stream existing = File.OpenRead(destination))
                                using (var hash = SHA256.Create()) matches = expected.SequenceEqual(hash.ComputeHash(existing));
                            }
                            if (matches) continue;
                            embedded.Position = 0;
                            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                                { embedded.CopyTo(output); output.Flush(true); }
                                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                                else File.Move(temporary, destination);
                            }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        }
                    }
                }
                finally { if (acquired) extractionLock.ReleaseMutex(); }
            }
            foreach (string required in new string[] { "katago.exe", "analysis.cfg", ModelFile })
                if (!File.Exists(Path.Combine(directory, required))) throw new FileNotFoundException("缺少 AI 资源：" + required);
            return directory;
        }

        private void StopProcess(Process target)
        {
            if (target == null) return;
            try { if (!target.HasExited) target.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            Process target;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                ready = false;
                generation++;
                if (startupCancellation != null) startupCancellation.Cancel();
                foreach (PendingRequest request in pending.Values) request.Completion.TrySetCanceled();
                pending.Clear();
                if (readySignal != null) readySignal.TrySetCanceled();
                target = process;
                process = null;
            }
            StopProcess(target);
            if (target != null) target.Dispose();
            // startGate may still be held by a canceled startup; do not dispose it here.
        }
    }
}
