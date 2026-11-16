# Keyframe.Sdk (C#)

C# client for the Keyframe REST API. Keyframe summarizes images, video, and audio and extracts typed metadata fields. This package targets `net8.0` and `net10.0`.

Version 0.1.0 (ALPHA).

## Install

```bash
dotnet add package Keyframe.Sdk --version 0.1.0
```

Or reference `sdk/csharp/Keyframe.Sdk/Keyframe.Sdk.csproj` directly.

## Quick start

```csharp
using Keyframe.Sdk;
using Keyframe.Sdk.Enums;
using Keyframe.Sdk.Requests;
using Keyframe.Sdk.Responses;

using (KeyframeClient client = new KeyframeClient("http://127.0.0.1:8700", "default"))
{
    ImageAnalysisRequest request = new ImageAnalysisRequest
    {
        Image = await MediaSources.FromFileAsync("backyard.jpg"),
        Context = new List<ContextItem>
        {
            new ContextItem { Label = "Source", Text = "Photos submitted by pet adoption volunteers." },
            new ContextItem { Text = "Animals wearing orange collars are already adopted." }
        },
        Functions = new FunctionsSpec
        {
            Summarize = new SummarizeSpec { Enable = true, MaxWords = 60 },
            Extract = new ExtractSpec
            {
                Enable = true,
                Fields = new List<FieldSpec>
                {
                    new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Does this image have cats?" },
                    new FieldSpec { Key = "dogCount", Type = FieldTypeEnum.Integer, Prompt = "How many dogs?", Minimum = 0 }
                }
            }
        }
    };

    ImageAnalysisResponse result = await client.Image.AnalyzeAsync(request);
    string? summary = result.GetResult<string>("Summary");
    bool hasCats = result.GetResult<bool>("hasCats");
    long dogs = result.GetResult<long>("dogCount");
}
```

Base URLs use the IPv4 loopback `127.0.0.1` so Windows clients do not stall on an IPv6 fallback.

## Surface

| Member | Route |
|---|---|
| `Health.GetAsync`, `Health.IsAliveAsync` | `GET /v1.0/health`, `HEAD /` (anonymous) |
| `WhoAmI.GetAsync` | `GET /v1.0/whoami` |
| `Image.AnalyzeAsync` | `POST /v1.0/image` |
| `Video.AnalyzeAsync` | `POST /v1.0/video`, returns `VideoSubmitResult` holding a `Job` or a `Result` |
| `Video.AnalyzeAndWaitAsync` | Submits, polls the job until it is terminal, then fetches the result |
| `Audio.AnalyzeAsync` | `POST /v1.0/audio`, returns `AudioSubmitResult` holding a `Job` or a `Result` |
| `Audio.AnalyzeAndWaitAsync` | Submits, polls the job until it is terminal, then fetches the `AudioAnalysisResponse` |
| `Segments.GetSegmentAsync` | `POST /v1.0/segment` (Binary), returns `SegmentMedia`: bytes plus metadata from the response headers |
| `Segments.GetSegmentJsonAsync` | `POST /v1.0/segment` (Json), returns `SegmentResponse` with base64 `Data` (`GetBytes()` decodes it) |
| `Probe.ProbeAsync` | `POST /v1.0/probe` |
| `Jobs.ListAsync`, `GetAsync`, `GetResultAsync`, `GetAudioResultAsync`, `CancelAsync`, `DeleteAsync`, `WaitAsync` | `/v1.0/jobs` |
| `Endpoints.ListAsync`, `Endpoints.TestAsync` (admin) | `/v1.0/endpoints` |
| `RequestHistory.ListAsync`, `SummaryAsync`, `GetAsync`, `DeleteAsync` (admin), `DeleteManyAsync` (admin) | `/v1.0/api/request-history` |
| `Analytics.GetSummaryAsync`, `ListAnalysesAsync`, `GetAnalysisAsync`, `DeleteAnalysisAsync`, `DeleteAnalysesAsync` (all admin) | `/v1.0/analytics/summary`, `/v1.0/analytics/analyses` |
| `Settings.GetAsync`, `UpdateAsync`, `RestartAsync` (all admin) | `/v1.0/settings`, `/v1.0/restart` |
| `SendRawAsync` | Any route, no exception on error status |

Models live in `Keyframe.Sdk.Requests`, `Keyframe.Sdk.Responses`, `Keyframe.Sdk.Enums`, and `Keyframe.Sdk.Settings`. They mirror the server DTOs: JSON is PascalCase, enums are strings, and `Context` accepts a string, an array of strings, or `{Label, Text}` items on input and is always written as items.

## Video

```csharp
VideoAnalysisRequest video = new VideoAnalysisRequest
{
    Video = MediaSources.FromUrl("https://example.com/talks/keynote.mp4"),
    Functions = new VideoFunctionsSpec
    {
        Summarize = new SummarizeSpec { Enable = true },
        Scenes = new ScenesSpec { Enable = true, Summarize = new SummarizeSpec { Enable = true } },
        Transcribe = new TranscribeSpec { Enable = true }
    }
};

// Submit and poll until done (throws KeyframeJobException if the job fails or is canceled).
VideoAnalysisResponse result = await client.Video.AnalyzeAndWaitAsync(video, pollIntervalMs: 1000, timeoutMs: 600000);

// Or manage the job yourself.
VideoSubmitResult submitted = await client.Video.AnalyzeAsync(video);
JobResponse job = await client.Jobs.WaitAsync(submitted.JobId);
VideoAnalysisResponse fetched = await client.Jobs.GetResultAsync(job.Id);
```

When `Wait = true` and the job finishes inside `WaitTimeoutMs`, `AnalyzeAsync` returns the result directly (`submitted.Result`, with `JobId` taken from the `x-keyframe-job-id` header). A job that finished without succeeding comes back as `submitted.Job`.

## Audio

```csharp
AudioAnalysisRequest audio = new AudioAnalysisRequest
{
    Audio = await MediaSources.FromFileAsync("meeting.mp3"),
    TranscriptionModel = new ModelSelection { Endpoint = "whisper" },
    Functions = new AudioFunctionsSpec
    {
        Transcribe = new TranscribeSpec { Enable = true, Language = "en" },
        Summarize = new SummarizeSpec { Enable = true, Style = SummaryStyleEnum.Bullets },
        Extract = new ExtractSpec
        {
            Enable = true,
            Fields = new List<FieldSpec> { new FieldSpec { Key = "speakerCount", Type = FieldTypeEnum.Integer, Prompt = "How many speakers?" } }
        }
    }
};

AudioAnalysisResponse heard = await client.Audio.AnalyzeAndWaitAsync(audio);
string transcript = heard.Transcript?.Text ?? String.Empty;
```

Audio files (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) and video files (only the audio track is used) are accepted. `AudioFunctionsSpec` defaults to transcription and a summary on, extraction off; the summary and fields run on the transcript with the text model. Audio jobs share the job queue with video, so `Jobs.WaitAsync`, `CancelAsync`, and `DeleteAsync` work the same; read the result with `Jobs.GetAudioResultAsync`. `Telemetry.QueueWaitMs` is the time the job spent queued, and each `ModelCallTelemetry` has `QueueMs` (endpoint concurrency gate), `TimeToFirstTokenMs`, `GenerationMs`, and `TokensPerSecond`.

## Segments

```csharp
SegmentRequest cut = new SegmentRequest
{
    Source = MediaSources.FromUrl("https://example.com/talks/keynote.mp4"),
    StartMs = result.Scenes[2].StartMs,
    EndMs = result.Scenes[2].EndMs,
    Output = SegmentOutputEnum.Video,
    MaxEdge = 640
};

SegmentMedia clip = await client.Segments.GetSegmentAsync(cut);
await File.WriteAllBytesAsync(clip.Filename, clip.Data);
```

`Output` is `Video` (`mp4` or `webm`), `Audio` (`mp3`, `wav`, `flac`, `m4a`, or `ogg`), or `Frame` (`jpg` or `png`, one still at `StartMs`). `SegmentMedia` carries `MediaType`, `Filename`, `StartMs`, `EndMs` (after clamping to the media duration), `DurationMs`, `SizeBytes`, and `Data`. The HTTP stack does not always expose `Content-Disposition`; `Filename` then follows the server's naming (`segment-{StartMs}-{EndMs}.{ext}`, or `segment-{StartMs}.{ext}` for frames). `GetSegmentJsonAsync` returns the same metadata with base64 `Data`. Both methods set `ResponseFormat` themselves and leave the request object unchanged. The source is resolved again on every call, and a range longer than `Media.MaxSegmentMs` fails with 400.

## Analytics

```csharp
using (KeyframeClient admin = new KeyframeClient("http://127.0.0.1:8700", "keyframeadmin"))
{
    AnalyticsSummary day = await admin.Analytics.GetSummaryAsync(new AnalyticsSummaryFilter { MediaKind = MediaKindEnum.Audio });
    double? p95 = day.Totals.AnalysisMs?.P95;
    double? tokensPerSecond = day.Totals.TokensPerSecond?.Avg;

    EnumerationResult<AnalysisRecord> failures = await admin.Analytics.ListAnalysesAsync(new AnalyticsFilter { Success = false, MaxResults = 20 });
    AnalysisRecord detail = await admin.Analytics.GetAnalysisAsync(failures.Items[0].Id);
    DeleteResult pruned = await admin.Analytics.DeleteAnalysesAsync(DateTime.UtcNow.AddDays(-30));
}
```

Every analytics route needs the admin key. The summary defaults to the last 24 hours with a bucket chosen from the range (minute up to 2 hours, hour up to 7 days, otherwise day); `Bucket` in the response is lowercase and `ByMediaKind` always has `Image`, `Video`, and `Audio`. Timings are `Distribution` objects (`Count`, `Min`, `Max`, `Avg`, `P50`, `P90`, `P95`, `P99`, `StdDev`, `Sum`), null when there are no samples. Listed records have `Calls` set to null; `GetAnalysisAsync` fills `Calls` with every model call attempt. `DeleteAnalysesAsync(null)` deletes every stored analysis. Rows are written just after an analysis finishes, so a read immediately after a result can briefly return 404.

## Authentication

Pass an access key or the admin key to the constructor. The client sends `Authorization: Bearer <key>` by default; set `AuthHeader = AuthHeaderEnum.XApiKey` to send `x-api-key` instead. The key is never logged, has no getter, and is not included in `ToString()`. Use `SetApiKey` to change it.

## Errors

| Exception | When |
|---|---|
| `KeyframeApiException` | Non-2xx response. `StatusCode`, `Error` (`ApiErrorEnum`), `ApiError` (the full `ApiErrorResponse` with `Message`, `Details`, `RequestId`), `ResponseBody`, `Method`, `Path`. |
| `KeyframeJobException` | `AnalyzeAndWaitAsync` saw the job end `Failed`, `Canceled`, or `Interrupted`. `Job` holds the final state. |
| `TimeoutException` | A request exceeded `TimeoutMs` (default 300000), or a wait exceeded its limit. |
| `HttpRequestException` | The server could not be reached. |
| `OperationCanceledException` | The caller's token was canceled. |

## Settings

`Settings.GetAsync` returns secrets masked as `****` plus the last four characters. Send the object back through `Settings.UpdateAsync` with masked values unchanged and the server keeps the stored secrets. `UpdateAsync` replaces the whole settings object, so use an SDK version that matches the server: a field this SDK does not know about would be reset to its server default.

## HTTP transport

The SDK uses [RestWrapper](https://www.nuget.org/packages/RestWrapper) 3.3.2 over one pooled `HttpClient` per `KeyframeClient`. You can pass your own `HttpClient` to the constructor; you then own its lifetime. Per-request timeouts are enforced by the SDK with a cancellation token, not by `HttpClient.Timeout`.

## Test harness

`sdk/csharp/Test.Automated` runs end to end against a live server using the mock provider. It generates its media fixtures with ffmpeg at run time.

```bash
cd sdk/csharp
KEYFRAME_URL=http://127.0.0.1:8710 KEYFRAME_ADMIN_KEY=keyframeadmin KEYFRAME_ACCESS_KEY=default \
  dotnet run --project Test.Automated -c Release -f net10.0
```

The defaults are `http://127.0.0.1:8710`, `keyframeadmin`, and `default`. Use `-f net8.0` to run on .NET 8. The exit code is 0 when every test passes, 1 on failure, and 2 when the server is unreachable. The harness performs one live settings write (it bumps `RequestHistory.RetentionDays` and restores it) and never calls `/v1.0/restart` with the admin key.

## License

MIT. See `LICENSE.md` in the repository root.
