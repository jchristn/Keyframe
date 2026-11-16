namespace Keyframe.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Database;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Models;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Accepts video and audio jobs, runs them on a bounded number of workers, persists status and progress, and stores
    /// results. Jobs that were queued or running when the process stopped are marked Interrupted at startup.
    /// Concurrency and queue limits are read live from settings.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class JobService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Jobs currently running.
        /// </summary>
        public int ActiveCount
        {
            get
            {
                return _Running.Count;
            }
        }

        /// <summary>
        /// Jobs waiting for a worker.
        /// </summary>
        public int QueuedCount
        {
            get
            {
                return _Queue.Count;
            }
        }

        #endregion

        #region Private-Members

        private const string Header = "[JobService] ";
        private static readonly TimeSpan _PersistInterval = TimeSpan.FromSeconds(1);

        private readonly DatabaseDriverBase _Database;
        private readonly AnalysisServices _Services;
        private readonly Func<KeyframeSettings> _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentQueue<JobWork> _Queue = new ConcurrentQueue<JobWork>();
        private readonly ConcurrentDictionary<string, JobWork> _Running = new ConcurrentDictionary<string, JobWork>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, JobWork> _All = new ConcurrentDictionary<string, JobWork>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _Signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Dispatcher = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="services">Analysis services.</param>
        /// <param name="settings">Live settings accessor.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public JobService(DatabaseDriverBase database, AnalysisServices services, Func<KeyframeSettings> settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mark leftover jobs interrupted and start the dispatcher.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            int interrupted = await _Database.Jobs.MarkInterruptedAsync("The server stopped while the job was queued or running.", token).ConfigureAwait(false);
            if (interrupted > 0) _Logging.Warn(Header + "marked " + interrupted + " job(s) interrupted from a previous run");

            KeyframeMetrics.ActiveJobsProvider = () => ActiveCount;
            KeyframeMetrics.QueuedJobsProvider = () => QueuedCount;
            _Dispatcher = Task.Run(() => DispatchLoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Accept a job. The request must already be validated.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="principalName">Submitting principal.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="QueueFullException">Thrown when the queue is at capacity.</exception>
        public Task<Job> SubmitAsync(VideoAnalysisRequest request, string? principalName, CancellationToken token = default)
        {
            return SubmitAsync(request, principalName, null, token);
        }

        /// <summary>
        /// Accept a video job. The request must already be validated.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="principalName">Submitting principal.</param>
        /// <param name="requestId">HTTP request identifier, recorded in the result and analytics.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="QueueFullException">Thrown when the queue is at capacity.</exception>
        public async Task<Job> SubmitAsync(VideoAnalysisRequest request, string? principalName, string? requestId, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Job job = NewJob(MediaKindEnum.Video, principalName, request.Video?.Url);
            await EnqueueAsync(new JobWork(job, request, requestId), token).ConfigureAwait(false);
            return job;
        }

        /// <summary>
        /// Accept an audio job. The request must already be validated.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="principalName">Submitting principal.</param>
        /// <param name="requestId">HTTP request identifier, recorded in the result and analytics.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="QueueFullException">Thrown when the queue is at capacity.</exception>
        public async Task<Job> SubmitAsync(AudioAnalysisRequest request, string? principalName, string? requestId, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Job job = NewJob(MediaKindEnum.Audio, principalName, request.Audio?.Url);
            await EnqueueAsync(new JobWork(job, request, requestId), token).ConfigureAwait(false);
            return job;
        }

        /// <summary>
        /// Wait for a job to finish.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="timeoutMs">Maximum wait in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The finished job, or null when it did not finish in time or is not tracked.</returns>
        public async Task<Job?> WaitAsync(string id, int timeoutMs, CancellationToken token = default)
        {
            JobWork? work;
            if (!_All.TryGetValue(id, out work)) return null;
            try
            {
                return await work.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)), token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        /// Cancel a queued or running job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job after the request, or null when not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the job already finished.</exception>
        public async Task<Job?> CancelAsync(string id, CancellationToken token = default)
        {
            Job? stored = await _Database.Jobs.ReadAsync(id, token).ConfigureAwait(false);
            if (stored == null) return null;
            if (stored.IsTerminal) throw new InvalidOperationException("Job " + id + " already finished with status " + stored.Status + ".");

            JobWork? work;
            if (!_All.TryGetValue(id, out work))
            {
                stored.Status = JobStatusEnum.Canceled;
                stored.CompletedUtc = DateTime.UtcNow;
                return await _Database.Jobs.UpdateAsync(stored, token).ConfigureAwait(false);
            }

            work.CanceledByCaller = true;
            if (!_Running.ContainsKey(id))
            {
                // Still queued: finish it now. The dispatcher skips canceled work.
                await FinishAsync(work, JobStatusEnum.Canceled, "Canceled by caller.", null, token).ConfigureAwait(false);
                return work.Job;
            }

            work.Cancellation.Cancel();
            Job? finished = await WaitAsync(id, 10000, token).ConfigureAwait(false);
            return finished ?? await _Database.Jobs.ReadAsync(id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop accepting work and interrupt running jobs.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token = default)
        {
            _Stopping.Cancel();
            foreach (JobWork work in _Running.Values) work.Cancellation.Cancel();

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (_Running.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, token).ConfigureAwait(false);
            }

            KeyframeMetrics.ActiveJobsProvider = null;
            KeyframeMetrics.QueuedJobsProvider = null;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Stopping.Cancel();
                _Stopping.Dispose();
                _Signal.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private Job NewJob(MediaKindEnum kind, string? principalName, string? sourceUrl)
        {
            if (_Queue.Count >= _Settings().Jobs.MaxQueuedJobs)
                throw new QueueFullException("The job queue is full (" + _Settings().Jobs.MaxQueuedJobs + " queued). Retry later.");

            return new Job
            {
                MediaKind = kind,
                Status = JobStatusEnum.Queued,
                PrincipalName = principalName,
                SourceUrl = sourceUrl,
                ProgressStage = "Queued"
            };
        }

        private async Task EnqueueAsync(JobWork work, CancellationToken token)
        {
            await _Database.Jobs.CreateAsync(work.Job, token).ConfigureAwait(false);
            _All[work.Job.Id] = work;
            _Queue.Enqueue(work);
            _Signal.Release();
        }

        private async Task DispatchLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _Signal.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);

                    while (_Running.Count < _Settings().Jobs.MaxConcurrentJobs)
                    {
                        JobWork? work;
                        if (!_Queue.TryDequeue(out work)) break;
                        if (work.Job.IsTerminal) continue;

                        _Running[work.Job.Id] = work;
                        _ = Task.Run(() => RunAsync(work, token), CancellationToken.None);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(Header + "dispatcher error: " + ex.Message);
                }
            }
        }

        private async Task RunAsync(JobWork work, CancellationToken stopping)
        {
            Job job = work.Job;
            Stopwatch sw = Stopwatch.StartNew();
            string kindLabel = job.MediaKind.ToString().ToLowerInvariant();
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(work.Cancellation.Token, stopping))
            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("job " + kindLabel, ActivityKind.Internal))
            {
                activity?.SetTag("keyframe.job_id", job.Id);
                try
                {
                    job.Status = JobStatusEnum.Running;
                    job.StartedUtc = DateTime.UtcNow;
                    job.ProgressStage = "Starting";
                    await _Database.Jobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);

                    double queueWaitMs = Math.Max(0, (job.StartedUtc.Value - job.CreatedUtc).TotalMilliseconds);
                    KeyframeMetrics.RecordJobQueue(kindLabel, queueWaitMs / 1000.0);
                    AnalysisOrigin origin = new AnalysisOrigin
                    {
                        RequestId = work.RequestId,
                        JobId = job.Id,
                        PrincipalName = job.PrincipalName,
                        QueueWaitMs = queueWaitMs
                    };

                    AnalysisResponse response;
                    if (work.Audio != null)
                        response = await new AudioAnalyzer(_Services).AnalyzeAsync(work.Audio, p => OnProgress(work, p), origin, linked.Token).ConfigureAwait(false);
                    else
                        response = await new VideoAnalyzer(_Services).AnalyzeAsync(work.Video!, p => OnProgress(work, p), origin, linked.Token).ConfigureAwait(false);

                    if (response.Properties != null)
                    {
                        job.SizeBytes = response.Properties.SizeBytes;
                        job.DurationMs = response.Properties.DurationMs ?? 0;
                        job.SourceSha256 = response.Properties.Sha256;
                    }

                    string json = KeyframeSerializer.Serialize(response, false);
                    await _Database.Jobs.StoreResultAsync(new JobResult { JobId = job.Id, ResultJson = json }, CancellationToken.None).ConfigureAwait(false);
                    job.ProgressPercent = 100;
                    job.ProgressStage = "Done";
                    await FinishAsync(work, JobStatusEnum.Succeeded, response.Success ? null : String.Join(" ", response.Errors), activity, CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (work.CanceledByCaller)
                {
                    await FinishAsync(work, JobStatusEnum.Canceled, "Canceled by caller.", activity, CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    await FinishAsync(work, JobStatusEnum.Interrupted, "The server stopped while the job was running.", activity, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(Header + "job " + job.Id + " failed: " + ex.Message);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    await FinishAsync(work, JobStatusEnum.Failed, ex.Message, activity, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    JobWork? removed;
                    _Running.TryRemove(job.Id, out removed);
                    _Signal.Release();
                    _Logging.Debug(Header + "job " + job.Id + " " + job.Status + " in " + sw.ElapsedMilliseconds + "ms");
                }
            }
        }

        private void OnProgress(JobWork work, JobProgress progress)
        {
            Job job = work.Job;
            bool stageChanged = !String.Equals(job.ProgressStage, progress.Stage, StringComparison.Ordinal);
            job.ProgressStage = progress.Stage;
            job.ProgressCompleted = progress.Completed;
            job.ProgressTotal = progress.Total;
            job.ProgressPercent = progress.Percent;

            DateTime now = DateTime.UtcNow;
            if (!stageChanged && now - work.LastPersistUtc < _PersistInterval) return;
            work.LastPersistUtc = now;
            _ = PersistProgressAsync(job);
        }

        private async Task PersistProgressAsync(Job job)
        {
            try
            {
                if (job.IsTerminal) return;
                await _Database.Jobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(Header + "progress update for " + job.Id + " failed: " + ex.Message);
            }
        }

        private async Task FinishAsync(JobWork work, JobStatusEnum status, string? error, Activity? activity, CancellationToken token)
        {
            Job job = work.Job;
            lock (work)
            {
                if (job.IsTerminal) return;
                job.Status = status;
            }

            job.Error = error;
            job.CompletedUtc = DateTime.UtcNow;
            activity?.SetTag("keyframe.job_status", status.ToString());
            try
            {
                await _Database.Jobs.UpdateAsync(job, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(Header + "could not persist final status for " + job.Id + ": " + ex.Message);
            }

            KeyframeMetrics.RecordJob(status.ToString().ToLowerInvariant());
            work.Completion.TrySetResult(job);

            // Keep finished work briefly so late waiters still resolve, then release it.
            _ = ReleaseLaterAsync(work);
        }

        private async Task ReleaseLaterAsync(JobWork work)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), _Stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            JobWork? removed;
            if (_All.TryRemove(work.Job.Id, out removed)) removed.Dispose();
        }

        #endregion
    }
}
