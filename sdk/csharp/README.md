# Keyframe.Sdk (C#)

C# client for the Keyframe REST API. Keyframe summarizes images and video and extracts typed metadata fields. This package targets `net8.0` and `net10.0`.

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
| `Probe.ProbeAsync` | `POST /v1.0/probe` |
| `Jobs.ListAsync`, `GetAsync`, `GetResultAsync`, `CancelAsync`, `DeleteAsync`, `WaitAsync` | `/v1.0/jobs` |
| `Endpoints.ListAsync`, `Endpoints.TestAsync` (admin) | `/v1.0/endpoints` |
| `RequestHistory.ListAsync`, `SummaryAsync`, `GetAsync`, `DeleteAsync` (admin), `DeleteManyAsync` (admin) | `/v1.0/api/request-history` |
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
