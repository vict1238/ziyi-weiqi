using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GoDesk
{
    internal enum ReviewGrade { Best, Good, Average, Inaccurate, Bad, Blunder, Unavailable }

    internal sealed class ReviewThresholds
    {
        public double Good { get; private set; }
        public double Average { get; private set; }
        public double Poor { get; private set; }
        public double Blunder { get; private set; }

        public ReviewThresholds(double good = 1.5, double average = 3, double poor = 4.5, double blunder = 6)
        {
            if (!(good >= 0 && good < average && average < poor && poor < blunder) || Double.IsInfinity(blunder))
                throw new ArgumentException("目损阈值须为非负数，并从好手到恶手依次递增。");
            Good = good;
            Average = average;
            Poor = poor;
            Blunder = blunder;
        }
    }

    internal sealed class ReviewRow
    {
        private readonly Move actualMove;
        private readonly string playedCoordinate;

        public int MoveNumber { get; private set; }
        public Move ActualMove { get { return new Move(actualMove.X, actualMove.Y, actualMove.Color); } }
        public Stone Color { get { return actualMove.Color; } }
        public AnalysisResult Recommendation { get; private set; }
        public MoveCandidate ActualCandidate { get; private set; }
        public bool IsTerminalPass { get; private set; }
        public bool IsPending { get { return !IsTerminalPass && !HasEvaluation && String.IsNullOrEmpty(Error); } }
        public string Error { get; private set; }
        public long Visits { get; private set; }
        public bool HasEvaluation { get; private set; }
        public bool IsFirstChoice { get; private set; }
        public double PointLoss { get; private set; }
        // Expressed in percentage points, with the mover as the reference side.
        public double WinrateLoss { get; private set; }

        internal ReviewRow(int moveNumber, Move move, int boardSize, long visits, bool terminalPass,
            AnalysisResult recommendation = null, MoveCandidate actualCandidate = null, string error = null)
        {
            MoveNumber = moveNumber;
            actualMove = new Move(move.X, move.Y, move.Color);
            playedCoordinate = move.ToGtp(boardSize);
            Visits = visits;
            IsTerminalPass = terminalPass;
            Recommendation = recommendation;
            ActualCandidate = actualCandidate;
            Error = error;

            MoveCandidate best = FirstCandidate(recommendation);
            HasEvaluation = !terminalPass && error == null && HasValue(best) && HasValue(actualCandidate) &&
                String.Equals(actualCandidate.Move, playedCoordinate, StringComparison.OrdinalIgnoreCase);
            if (!HasEvaluation) return;

            IsFirstChoice = String.Equals(best.Move, playedCoordinate, StringComparison.OrdinalIgnoreCase);
            if (IsFirstChoice) return;
            double perspective = actualMove.Color == Stone.Black ? 1 : -1;
            PointLoss = Math.Max(0, perspective * (best.ScoreLead - actualCandidate.ScoreLead));
            WinrateLoss = Math.Max(0, perspective * (best.Winrate - actualCandidate.Winrate)) * 100;
        }

        public ReviewGrade Grade(ReviewThresholds thresholds)
        {
            if (thresholds == null) throw new ArgumentNullException("thresholds");
            if (!HasEvaluation) return ReviewGrade.Unavailable;
            if (IsFirstChoice) return ReviewGrade.Best;
            if (PointLoss <= thresholds.Good) return ReviewGrade.Good;
            if (PointLoss <= thresholds.Average) return ReviewGrade.Average;
            if (PointLoss <= thresholds.Poor) return ReviewGrade.Inaccurate;
            return PointLoss < thresholds.Blunder ? ReviewGrade.Bad : ReviewGrade.Blunder;
        }

        internal static MoveCandidate FirstCandidate(AnalysisResult result)
        {
            if (result == null || result.Candidates == null || result.Candidates.Count == 0) return null;
            MoveCandidate first = result.Candidates[0];
            return first != null && first.Order == 0 && first.Move == result.BestMove ? first : null;
        }

        internal static bool HasValue(MoveCandidate candidate)
        {
            return candidate != null && candidate.Visits > 0 && !String.IsNullOrEmpty(candidate.Move) &&
                !Double.IsNaN(candidate.ScoreLead) && !Double.IsInfinity(candidate.ScoreLead) &&
                candidate.Winrate >= 0 && candidate.Winrate <= 1;
        }
    }

    internal sealed class ReviewSummary
    {
        private readonly int[] counts = new int[(int)ReviewGrade.Unavailable + 1];
        private double pointLossTotal;

        public int EligibleCount { get; private set; }
        public int EvaluatedCount { get; private set; }
        public int TerminalPassCount { get; private set; }
        public int UnavailableCount { get { return Count(ReviewGrade.Unavailable); } }
        public int PendingCount { get { return EligibleCount - EvaluatedCount - UnavailableCount; } }
        public int FirstCount { get { return Count(ReviewGrade.Best); } }
        public int GoodCount { get { return FirstCount + Count(ReviewGrade.Good); } }
        public int BlunderCount { get { return Count(ReviewGrade.Blunder); } }
        public double AveragePointLoss { get { return EvaluatedCount == 0 ? Double.NaN : pointLossTotal / EvaluatedCount; } }

        public int Count(ReviewGrade grade) { return counts[(int)grade]; }

        internal void Add(ReviewRow row, ReviewThresholds thresholds)
        {
            if (row.IsTerminalPass) { TerminalPassCount++; return; }
            EligibleCount++;
            if (row.IsPending) return;
            counts[(int)row.Grade(thresholds)]++;
            if (!row.HasEvaluation) return;
            EvaluatedCount++;
            pointLossTotal += row.PointLoss;
        }
    }

    /// <summary>
    /// Keeps one frozen main line and one engine profile for the lifetime of a report.
    /// A deeper single-move result does not overwrite the baseline statistics.
    /// </summary>
    internal sealed class WholeGameReview : IDisposable
    {
        private readonly object sync = new object();
        private readonly string originalSgf;
        private readonly ReviewRow[] rows;
        private readonly KataGoEngine engine;
        private readonly Dictionary<Tuple<int, long>, ReviewRow> rechecks = new Dictionary<Tuple<int, long>, ReviewRow>();
        private CancellationTokenSource active;
        private int completedCount;
        private bool disposed;

        public string ModelDescription { get; private set; }
        public long Visits { get; private set; }
        public int TotalMoves { get { return rows.Length; } }
        public int CompletedCount { get { lock (sync) return completedCount; } }
        public bool IsRunning { get { lock (sync) return active != null; } }
        // Raised by a worker thread. WinForms consumers must marshal to their UI thread.
        public event Action Changed;

        public WholeGameReview(GoGame game, EngineSettings settings, long visits)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (settings == null) throw new ArgumentNullException("settings");
            if (game.Size != 9 && game.Size != 13 && game.Size != 19)
                throw new ArgumentException("整盘分析支持 9 路、13 路和 19 路棋谱。", "game");
            ValidateVisits(visits);

            // ToSgf deliberately retains the full original line, even when the
            // user starts a report while viewing an earlier position.
            originalSgf = game.ToSgf();
            GoGame frozen = GoGame.FromSgf(originalSgf);
            rows = new ReviewRow[frozen.Moves.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                Move move = frozen.Moves[i];
                bool terminalPass = frozen.IsOver && i >= rows.Length - 2 && move.IsPass;
                rows[i] = new ReviewRow(i + 1, move, frozen.Size, visits, terminalPass);
                if (terminalPass) completedCount++;
            }
            Visits = visits;
            ModelDescription = settings.Description;
            engine = new KataGoEngine(settings);
        }

        public ReviewRow[] Snapshot()
        {
            lock (sync) return (ReviewRow[])rows.Clone();
        }

        public GoGame GetPosition(int position)
        {
            GoGame copy = GoGame.FromSgf(originalSgf);
            copy.Goto(position);
            return copy;
        }

        public ReviewSummary Summarize(Stone color, int firstMove, int lastMove, ReviewThresholds thresholds)
        {
            if (thresholds == null) throw new ArgumentNullException("thresholds");
            var summary = new ReviewSummary();
            ReviewRow[] snapshot = Snapshot();
            foreach (ReviewRow row in snapshot)
                if ((color == Stone.Empty || row.Color == color) && row.MoveNumber >= firstMove && row.MoveNumber <= lastMove)
                    summary.Add(row, thresholds);
            return summary;
        }

        public Task RunAsync()
        {
            CancellationTokenSource work = BeginWork();
            return Task.Run(async delegate
            {
                NotifyChanged();
                try
                {
                    GoGame position = GoGame.FromSgf(originalSgf);
                    for (int index = 0; index < rows.Length; index++)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        if (!rows[index].IsPending) continue;
                        position.Goto(index);
                        ReviewRow row = await AnalyzeRowAsync(position, index + 1, Visits, work.Token).ConfigureAwait(false);
                        lock (sync)
                        {
                            work.Token.ThrowIfCancellationRequested();
                            rows[index] = row;
                            completedCount++;
                        }
                        NotifyChanged();
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!work.IsCancellationRequested) throw;
                }
                finally { FinishWork(work); }
            });
        }

        public void Pause()
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested) return;
                active.Cancel();
                engine.Cancel();
            }
        }

        public Task<ReviewRow> RecheckAsync(int moveNumber, long visits)
        {
            if (moveNumber < 1 || moveNumber > rows.Length) throw new ArgumentOutOfRangeException("moveNumber");
            ValidateVisits(visits);
            var key = Tuple.Create(moveNumber, visits);
            CancellationTokenSource work;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("WholeGameReview");
                ReviewRow baseline = rows[moveNumber - 1];
                if (baseline.IsTerminalPass || (visits == Visits && baseline.HasEvaluation)) return Task.FromResult(baseline);
                ReviewRow cached;
                if (rechecks.TryGetValue(key, out cached)) return Task.FromResult(cached);
                work = BeginWork();
            }
            return Task.Run(async delegate
            {
                NotifyChanged();
                try
                {
                    ReviewRow row = await AnalyzeRowAsync(GetPosition(moveNumber - 1), moveNumber, visits, work.Token).ConfigureAwait(false);
                    lock (sync)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        if (row.HasEvaluation) rechecks[key] = row;
                    }
                    return row;
                }
                finally { FinishWork(work); }
            });
        }

        private async Task<ReviewRow> AnalyzeRowAsync(GoGame position, int moveNumber, long visits, CancellationToken cancellation)
        {
            Move played = position.Moves[moveNumber - 1];
            string coordinate = played.ToGtp(position.Size);
            AnalysisResult recommendation;
            try { recommendation = await QueryAsync(position, visits, null, cancellation).ConfigureAwait(false); }
            catch (InvalidOperationException ex)
            {
                // Invalid data for a single position can be skipped. A startup or
                // process failure stops the batch so it can be repaired and resumed.
                if (!(ex.InnerException is InvalidDataException)) throw;
                return new ReviewRow(moveNumber, played, position.Size, visits, false, error: ex.InnerException.Message);
            }

            MoveCandidate best = ReviewRow.FirstCandidate(recommendation);
            if (!ReviewRow.HasValue(best))
                return new ReviewRow(moveNumber, played, position.Size, visits, false, recommendation, error: "首选着法没有可用的评估。");
            MoveCandidate actual = FindCandidate(recommendation, coordinate);
            // A few visits attached to a policy candidate are insufficient for
            // comparison. Use the same budget with a root-only restriction.
            long minimumVisits = Math.Min(visits, Math.Max(8, visits / 16));
            if (actual != best && (!ReviewRow.HasValue(actual) || actual.Visits < minimumVisits))
            {
                try
                {
                    AnalysisResult supplement = await QueryAsync(position, visits, played, cancellation).ConfigureAwait(false);
                    actual = FindCandidate(supplement, coordinate);
                }
                catch (InvalidOperationException ex)
                {
                    return new ReviewRow(moveNumber, played, position.Size, visits, false, recommendation, error: ex.Message);
                }
            }
            if (!ReviewRow.HasValue(actual))
                return new ReviewRow(moveNumber, played, position.Size, visits, false, recommendation, error: "实战着法没有可用的评估。");
            return new ReviewRow(moveNumber, played, position.Size, visits, false, recommendation, actual);
        }

        private Task<AnalysisResult> QueryAsync(GoGame position, long visits, Move played, CancellationToken cancellation)
        {
            lock (sync)
            {
                // Pause and request submission share this lock. A request cannot
                // start with a fresh engine generation just after Pause cancels it.
                cancellation.ThrowIfCancellationRequested();
                return engine.AnalyzeReviewAsync(position, visits, played);
            }
        }

        private static MoveCandidate FindCandidate(AnalysisResult result, string coordinate)
        {
            if (result == null || result.Candidates == null) return null;
            foreach (MoveCandidate candidate in result.Candidates)
                if (candidate != null && String.Equals(candidate.Move, coordinate, StringComparison.OrdinalIgnoreCase)) return candidate;
            return null;
        }

        private CancellationTokenSource BeginWork()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("WholeGameReview");
                if (active != null) throw new InvalidOperationException("请先暂停当前整盘分析或复算。");
                active = new CancellationTokenSource();
                return active;
            }
        }

        private void FinishWork(CancellationTokenSource work)
        {
            lock (sync)
            {
                active = null;
                work.Dispose();
            }
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                try { handler(); }
                catch { /* A report observer must not stop or strand an engine request. */ }
            }
        }

        private static void ValidateVisits(long visits)
        {
            if (visits < 1 || visits > (1L << 50))
                throw new ArgumentOutOfRangeException("visits", "整盘分析需要有限的正整数搜索次数。");
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                Changed = null;
                if (active != null) active.Cancel();
            }
            engine.Dispose();
        }
    }
}
