# Testing and evaluating Keyframe

This guide exercises a running Keyframe server with the sample media in [test-data/](test-data/README.md), using cURL and `jq`. It covers images, video, audio, segments, jobs, endpoints, analytics, and error handling, and ends with a small evaluation script that checks extracted values against known answers.

For the automated test suites (unit, integration, database, SDK), see the build and test commands in the [README](README.md#getting-started).

## Contents

- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [1. Health and endpoints](#1-health-and-endpoints)
- [2. Images](#2-images)
- [3. Video](#3-video)
- [4. Audio](#4-audio)
- [5. Segments](#5-segments)
- [6. Jobs](#6-jobs)
- [7. Analytics](#7-analytics)
- [8. Error handling](#8-error-handling)
- [9. Evaluation script](#9-evaluation-script)
- [Live provider credentials](#live-provider-credentials)
- [What to look at](#what-to-look-at)

## Prerequisites

- A running server. Either the Docker stack (`cd docker && docker compose up -d`, server on port 8700, dashboard on 8701) or `dotnet run -c Release` in `src/Keyframe.Server`.
- The default model endpoints:
  - Ollama on the host at `127.0.0.1:11434` with `gemma3:4b`, `qwen3-vl:8b`, and `gpt-oss:20b` pulled (`ollama pull gemma3:4b` and so on).
  - A Whisper server for transcription. The Docker stack runs one (`keyframe-whisper`, with `keyframe-whisper-init` downloading `Systran/faster-whisper-small` on first start). Without Docker, run `docker compose -f docker/compose.yaml up -d keyframe-whisper keyframe-whisper-init`.
  - A listening server for the audio scenarios: `llama-server -hf ggml-org/Voxtral-Mini-3B-2507-GGUF:Q4_K_M --port 8720` on the host (add `--host 0.0.0.0` on Linux so the Docker stack can reach it), which the shipped `llama-listen` endpoint points at. The first start downloads 3 to 4 GB. The [README](README.md#the-local-listening-server) has the install steps per operating system and the container alternatives.
- `curl`, `jq`, `base64`, and `ffmpeg` (one audio scenario builds a longer file).

## Setup

Run these in the repository root. Every later snippet uses these variables and helpers.

```bash
export KF=http://127.0.0.1:8700
export KEY=default          # access key
export ADMIN=keyframeadmin  # admin key
export TD=test-data

# Base64 of a file on one line (works on macOS and Linux).
b64() { base64 < "$1" | tr -d '\n'; }

# POST JSON from stdin with the access key.
kpost() { curl -sS -X POST "$KF$1" -H "x-api-key: $KEY" -H "Content-Type: application/json" --data-binary @-; }

# GET with the admin key.
aget() { curl -sS "$KF$1" -H "x-api-key: $ADMIN"; }
```

Request bodies with inline media are large, so the examples build them with `jq -n --rawfile d <(b64 file)` and pipe them to `kpost`. Passing base64 as a command-line argument (`--arg`) fails with "Argument list too long" for files over about 1 MB. The snippets need bash (process substitution); in zsh they also work, in plain sh they do not.

## 1. Health and endpoints

```bash
curl -sS $KF/v1.0/health | jq '{Status, Version, FfmpegAvailable}'

# Every endpoint with its provider, capabilities, and health.
aget /v1.0/endpoints | jq -r '.[] | [.Name, .Provider, .Kind, .Model, .ProviderAvailable, .SupportsAudioInput, .Healthy, .HealthMessage] | @tsv'

# Probe each default endpoint now (a short prompt for completion endpoints).
for e in ollama-gemma3-4b ollama-qwen3-vl-8b ollama-gpt-oss-20b whisper llama-listen; do
  curl -sS -X POST $KF/v1.0/endpoints/$e/test -H "x-api-key: $ADMIN" | jq -c '{Name, Success, DurationMs, Error}'
done
```

Expect `Success: true` for all five. `SupportsAudioInput` is true for `llama-listen` (and for `vllm-voxtral`, which exists only in the Docker settings, where it ships inactive and is not probed) and false for the Ollama and Whisper endpoints. A failure here means a model is not pulled, or llama-server is not running or still loading; fix that before going on. The Whisper test only confirms its settings, not the server, so check Whisper itself with `curl -sS http://127.0.0.1:8000/v1/models` and confirm `Systran/faster-whisper-small` is listed. Without `llama-listen` the audio scenarios still run, but they fall back to the transcript.

## 2. Images

### Summary only

```bash
jq -n --rawfile d <(b64 $TD/images/cat.jpg) '{Image: {Data: $d}, Functions: {Summarize: {Enable: true, MaxWords: 40}}}' \
  | kpost /v1.0/image | jq '{Success, Summary: .Results.Summary, Ms: .Telemetry.TotalMs}'
```

### Typed fields

```bash
jq -n --rawfile d <(b64 $TD/images/receipt.jpg) '{
  Image: {Data: $d},
  Functions: {
    Summarize: {Enable: true, MaxWords: 30},
    Extract: {Enable: true, Fields: [
      {Key: "merchant", Type: "String",  Prompt: "Name of the business that issued the receipt."},
      {Key: "total",    Type: "Number",  Prompt: "The total amount charged."},
      {Key: "currency", Type: "String",  Prompt: "ISO currency code of the total."},
      {Key: "itemCount",Type: "Integer", Prompt: "How many line items are listed?", Minimum: 0, Maximum: 50}
    ]}
  }
}' | kpost /v1.0/image | jq '{Success, Results, Invalid: [.Fields[] | select(.Valid | not) | {Key, Error}]}'
```

Expected: merchant mentions Grosse Scheidegg, total 54.5, currency CHF, 4 items.

### Per-field mode, a specific model, and context

```bash
jq -n --rawfile d <(b64 $TD/images/dog.jpg) '{
  Image: {Data: $d},
  Context: ["Photo from a pet adoption listing.", {Label: "Shelter", Text: "Bayside Animal Rescue"}],
  VisionModel: {Endpoint: "ollama-qwen3-vl-8b"},
  Functions: {
    Summarize: {Enable: false},
    Extract: {Enable: true, Mode: "PerField", Fields: [
      {Key: "breed",      Type: "String",  Prompt: "Most likely dog breed."},
      {Key: "isOutdoors", Type: "Boolean", Prompt: "Was the photo taken outdoors?"},
      {Key: "setting",    Type: "Enum",    Prompt: "Where is the dog?", Options: ["Indoors", "Grass", "Beach", "Street"]}
    ]}
  }
}' | kpost /v1.0/image | jq '{Results, Calls: [.Telemetry.ModelCalls[] | {Purpose, Target, Model, DurationMs, TimeToFirstTokenMs, TokensPerSecond}]}'
```

### Image by URL

```bash
echo '{"Image":{"Url":"https://upload.wikimedia.org/wikipedia/commons/3/3a/Cat03.jpg"},"Functions":{"Summarize":{"Enable":true}}}' \
  | kpost /v1.0/image | jq '{Success, Summary: .Results.Summary}'
```

### Hallucination check

The test pattern has no people, animals, or text.

```bash
jq -n --rawfile d <(b64 $TD/images/test-pattern.png) '{Image: {Data: $d}, Functions: {Extract: {Enable: true, Fields: [
  {Key: "hasPeople", Type: "Boolean", Prompt: "Are there any people in the image?"},
  {Key: "hasAnimals", Type: "Boolean", Prompt: "Are there any animals in the image?"}]}}}' \
  | kpost /v1.0/image | jq .Results
```

Both should be `false`.

## 3. Video

Video analysis runs as a job. `Wait: true` holds the request open until the job finishes (up to `WaitTimeoutMs`) and returns the full result.

### Narrated slideshow: scenes, captions, transcript, fields

```bash
jq -n --rawfile d <(b64 $TD/video/narrated-slideshow.mp4) '{
  Video: {Data: $d, Filename: "narrated-slideshow.mp4"},
  Wait: true, WaitTimeoutMs: 600000,
  Functions: {
    Summarize: {Enable: true, MaxWords: 80},
    Extract: {Enable: true, Fields: [
      {Key: "animals", Type: "StringList", Prompt: "Animals shown in the video.", MaxItems: 5},
      {Key: "landmark", Type: "String", Prompt: "Any famous landmark shown."}
    ]},
    Scenes: {Enable: true, FramesPerScene: 1, Summarize: {Enable: true, Style: "Caption"}},
    Transcribe: {Enable: true, Language: "en"}
  }
}' | kpost /v1.0/video > /tmp/kf-slideshow.json

jq '{Success, Results, Errors, Warnings}' /tmp/kf-slideshow.json
jq -r '.Scenes[] | "\(.StartMs)-\(.EndMs) ms: \(.Results.Summary)"' /tmp/kf-slideshow.json
jq -r '.Transcript.Segments[] | "\(.StartMs)-\(.EndMs) ms: \(.Text)"' /tmp/kf-slideshow.json
```

Expected: four scenes (cat, dog, Eiffel Tower, food) whose captions match the narration, `animals` containing cat and dog, `landmark` Eiffel Tower. Scene captions come from frames and the transcript slice, so a caption can mention a neighboring slide when the cut falls mid-sentence.

### Asynchronous job with polling

```bash
JOB=$(jq -n --rawfile d <(b64 $TD/video/big-buck-bunny-45s.mp4) '{
  Video: {Data: $d}, Functions: {Summarize: {Enable: true}, Scenes: {Enable: true, Summarize: {Enable: true}}, Transcribe: {Enable: false}}
}' | kpost /v1.0/video | jq -r .Id)
echo "job $JOB"   # null here means the submit failed; print the response instead of piping it to jq -r .Id

while :; do
  S=$(curl -sS $KF/v1.0/jobs/$JOB -H "x-api-key: $KEY")
  echo "$S" | jq -c '{Status, Stage: .Progress.Stage, Percent: .Progress.Percent}'
  case $(echo "$S" | jq -r .Status) in Succeeded|Failed|Canceled|Interrupted) break;; null|"") echo "no such job"; break;; esac
  sleep 3
done

curl -sS $KF/v1.0/jobs/$JOB/result -H "x-api-key: $KEY" | jq '{Summary: .Results.Summary, Scenes: (.Scenes | length), TotalMs: .Telemetry.TotalMs}'
```

### Dialogue transcription

```bash
jq -n --rawfile d <(b64 $TD/video/elephants-dream-dialogue-60s.mp4) '{Video: {Data: $d}, Wait: true,
  Functions: {Summarize: {Enable: true}, Scenes: {Enable: false}, Transcribe: {Enable: true, Language: "en"}}}' \
  | kpost /v1.0/video | jq '{Language: .Transcript.Language, Text: .Transcript.Text, Summary: .Results.Summary}'
```

### Scene detection on hard cuts, no audio

```bash
jq -n --rawfile d <(b64 $TD/video/scene-cuts-12s-silent.mp4) '{Video: {Data: $d}, Wait: true,
  Functions: {Scenes: {Enable: true, Detection: {Threshold: 0.3, MinSceneMs: 1000}}, Transcribe: {Enable: true}}}' \
  | kpost /v1.0/video | jq '{Scenes: [.Scenes[] | [.StartMs, .EndMs]], Warnings}'
```

Expected: three scenes, at roughly 0 to 4000, 4000 to 8000, and 8000 to 12000 ms, and a warning that there is no audio to transcribe.

## 4. Audio

By default an audio analysis does three things before it answers: ffmpeg measures the recording (`Measurements`), Whisper transcribes it, and the listening endpoint (`llama-listen`, Voxtral Mini 3B on the host llama-server) hears the audio itself, with the transcript as a hint. When no listening endpoint is usable, the summary and fields come from the transcript with the text model (`gpt-oss:20b`), and `Listening` says why. Check `Listening` in every result below.

Local listening is slow on a CPU: tens of seconds to a few minutes per clip, and `llama-listen` handles one request at a time. The scenarios set `WaitTimeoutMs: 600000`; if a request still comes back as a job, poll it as in [section 6](#6-jobs).

### Listening: a recording heard whole

```bash
jq -n --rawfile d <(b64 $TD/audio/meeting-two-speakers.mp3) '{Audio: {Data: $d}, Wait: true, WaitTimeoutMs: 600000,
  Functions: {Summarize: {Enable: true, MaxWords: 40}, Extract: {Enable: true, Fields: [
    {Key: "hasSpeech", Type: "Boolean", Prompt: "Does the recording contain human speech?"},
    {Key: "content",   Type: "Enum",    Prompt: "What does the recording contain?", Options: ["Speech", "Music", "Tone", "Silence"]}]}}}' \
  | kpost /v1.0/audio > /tmp/kf-listen.json

jq '{Success, Listening: (.Listening | {Listened, Mode, Reason, Endpoint, Model, DurationMs, HeardMs, Clips}), Results, Warnings}' /tmp/kf-listen.json
jq -r '.Telemetry.ModelCalls[] | [.Purpose, .Endpoint, .InputAudioBytes] | @tsv' /tmp/kf-listen.json
```

Expected: `Listening.Listened` true, `Mode` `Full`, `Endpoint` `llama-listen`, `Model` `voxtral-mini-3b`, one `Recording` clip covering the whole 46 seconds, and `HeardMs` equal to `DurationMs`. The summary describes the Atlas status meeting, `hasSpeech` is true, and `content` is `Speech`. Do not use `tone-and-silence-no-speech.ogg` to judge the default model: Voxtral Mini 3B is a speech model and, given a pure tone, it can invent speech that is not there. A hosted listening model (Gemini, an OpenAI audio model) describes the tone correctly, and with any listening model the transcript-empty skip no longer applies. The `Summarize` and `Extract` calls go to `llama-listen` with `InputAudioBytes` above zero. Without listening, the same request skips Summarize and Extract with the warning "Summarize and Extract skipped: the transcript is empty."; add `Listen: {Enable: false}` to see it.

### Measurements only

```bash
jq -n --rawfile d <(b64 docker/factory/samples/sample.mp3) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: false}, Summarize: {Enable: false}}}' \
  | kpost /v1.0/audio | jq '{Success, Measurements, Reason: .Listening.Reason, Calls: (.Telemetry.ModelCalls | length), Stages: [.Telemetry.Stages[].Name]}'
```

Expected: `Measurements` filled, `Reason` `NoModelFunctions`, zero model calls, and stages `Resolve`, `Probe`, `Measure`. The factory sample is an 89-second mono file of three spoken clips from `test-data/audio` (the meeting, the podcast intro, and the voicemail) separated by 4-second silences, so it measures `Channels` 1, `StereoWidthDb` null, `EffectivelyMono` true, `ClippingLikely` false, `SilenceCount` 3 (the two gaps and about a second at the end), an integrated loudness around -18 LUFS, and a true peak around -1.7 dBTP. Measure counts as a function, so this request is valid even with every model function off.

### Sections

```bash
jq -n --rawfile d <(b64 docker/factory/samples/sample.mp3) '{Audio: {Data: $d}, Wait: true, WaitTimeoutMs: 600000,
  Functions: {Transcribe: {Enable: false}, Summarize: {Enable: false},
    Sections: {Enable: true, Summarize: {Enable: true, Style: "Caption"}}}}' \
  | kpost /v1.0/audio > /tmp/kf-sections.json

jq -r '.Sections[] | "\(.Index) \(.StartMs)-\(.EndMs) ms: \(.Results.Summary)"' /tmp/kf-sections.json
jq '{Mode: .Listening.Mode, Clips: [.Listening.Clips[] | {Scope, Index, StartMs, EndMs}]}' /tmp/kf-sections.json
```

Expected: three sections cut at the middle of each silence, about 0 to 47600, 47600 to 68800, and 68800 to 89300 ms (the meeting, the podcast intro, and the voicemail), each with a caption. Every section heard its own clip (`Scope` `Section`, one per index), and `Mode` is `Sections` because only section functions ran. Add `TextModel: {Endpoint: "ollama-gpt-oss-20b"}` and turn the top-level `Summarize` back on to get a whole-recording summary reduced from the three section results; `Mode` stays `Sections`. With the top-level `Summarize` on and no `TextModel`, the whole-recording summary hears the full audio instead and `Mode` is `Full`. Each section can be cut back out with `POST /v1.0/segment` ([section 5](#5-segments)).

### Excerpts of a long recording

`llama-listen` has a 120-second listen budget (`MaxListenSeconds`), so anything longer is heard as excerpts. Build a three-minute file from the meeting and analyze it:

```bash
ffmpeg -y -v error -stream_loop 3 -i $TD/audio/meeting-two-speakers.mp3 -c copy /tmp/meeting-x4.mp3

jq -n --rawfile d <(b64 /tmp/meeting-x4.mp3) '{Audio: {Data: $d}, Wait: true, WaitTimeoutMs: 600000,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true, MaxWords: 60}}}' \
  | kpost /v1.0/audio | jq '{Listening: (.Listening | {Mode, DurationMs, HeardMs, Clips: [.Clips[] | [.StartMs, .EndMs]]}), Warnings}'

# Six 30-second excerpts do not fit a 120-second budget, so each is shortened to 20 seconds.
jq -n --rawfile d <(b64 /tmp/meeting-x4.mp3) '{Audio: {Data: $d}, Wait: true, WaitTimeoutMs: 600000,
  Listen: {SampleClips: 6, ClipSeconds: 30},
  Functions: {Transcribe: {Enable: false}, Summarize: {Enable: true, MaxWords: 60}}}' \
  | kpost /v1.0/audio | jq '{Mode: .Listening.Mode, HeardMs: .Listening.HeardMs, Clips: (.Listening.Clips | length), Warnings}'
```

Expected for the first request: `Mode` `Excerpts`, four evenly spaced 30-second clips (the first starting at 0, the last ending at the end of the recording), `HeardMs` 120000, and a warning such as `Listened to 4 excerpts (2m00s) of a 3m03s recording.` (the length depends on the file). The second reports six clips, `HeardMs` 120000, and adds `Excerpts were shortened to 20s each to fit the listen budget of 2m00s.` A Gemini or vLLM endpoint, with the default 1200-second budget, hears the same file whole.

### Scene audio on video

```bash
jq -n --rawfile d <(b64 $TD/video/narrated-slideshow.mp4) '{Video: {Data: $d}, Wait: true, WaitTimeoutMs: 600000,
  Functions: {Scenes: {Enable: true, FramesPerScene: 1, Summarize: {Enable: true, Style: "Caption"}}, Transcribe: {Enable: true}}}' \
  | kpost /v1.0/video | jq '{Listening: (.Listening | {Listened, Mode, Reason, Endpoint}), Warnings}'

jq -n --rawfile d <(b64 $TD/video/scene-cuts-12s-silent.mp4) '{Video: {Data: $d}, Wait: true,
  Functions: {Scenes: {Enable: true, Summarize: {Enable: true}}}}' \
  | kpost /v1.0/video | jq '{Reason: .Listening.Reason, Scenes: (.Scenes | length)}'
```

Expected on the shipped settings: the slideshow reports `Listened` false, `Mode` `None`, and `Reason` `EndpointLacksAudioInput`, because the default vision endpoint is Ollama, which takes no audio. There is no listening warning, and the scenes are captioned from frames and transcript as before. The silent video reports `NoAudioTrack` and still returns three scenes. With an audio-capable vision endpoint, for example the Gemini endpoint from the [README](README.md#hosted-listening-endpoints) passed as `Model: {Endpoint: "gemini-listen"}`, the slideshow reports `Mode` `Scenes` and one `Scene` clip per scene. Scene audio never returns a `400`.

### Fallback when the listening server is down

```bash
# Stop the host llama-server (Ctrl+C in its terminal), then record its health now instead of waiting for the next probe.
curl -sS -X POST $KF/v1.0/endpoints/llama-listen/test -H "x-api-key: $ADMIN" | jq -c '{Name, Success, Error}'

jq -n --rawfile d <(b64 $TD/audio/voicemail.m4a) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true, Style: "Caption"}}}' \
  | kpost /v1.0/audio | jq '{Success, Listening: (.Listening | {Listened, Mode, Reason}), Summary: .Results.Summary, Warnings}'
```

Expected: `Success` true, `Listened` false, `Mode` `None`, `Reason` `EndpointUnhealthy`, a summary written from the transcript, and the warning `The listening endpoint 'llama-listen' is unhealthy; results come from the transcript only.` Start llama-server again. An unhealthy endpoint is probed every 15 seconds (`Inference.UnhealthyProbeIntervalMs`), so within about that long `aget /v1.0/endpoints | jq '.[] | select(.Name == "llama-listen") | .Healthy'` is true and the same request reports `Listened` true. The test route above records health right away. Without it, the first request after the stop tries to listen, reports the failed call in `Errors`, and marks the endpoint unhealthy, because a call that gets no HTTP response does that at once; the request after it falls back as shown.

The same fallback shows up on a first start: while llama-server is still downloading or loading its model its probe fails, and requests fall back with `EndpointUnhealthy` until the model is ready.

### Rejected listening requests

```bash
# 400: the named endpoint cannot take audio.
echo '{"Audio":{"Url":"https://example.com/a.mp3"},"Listen":{"Model":{"Endpoint":"ollama-gemma3-4b"}}}' | kpost /v1.0/audio | jq '{Error, Details}'

# 400: Flac to an OpenAI-compatible endpoint (llama-server takes only mp3 and wav).
echo '{"Audio":{"Url":"https://example.com/a.mp3"},"Listen":{"Rendition":{"Format":"Flac"}}}' | kpost /v1.0/audio | jq '{Error, Details}'

# 400 while llama-server is down: Transcribe is off, so the job would have no evidence.
echo '{"Audio":{"Url":"https://example.com/a.mp3"},"Functions":{"Transcribe":{"Enable":false},"Summarize":{"Enable":true}}}' | kpost /v1.0/audio | jq '{Error, Details}'
```

Expected `Details`: `Endpoint 'ollama-gemma3-4b' cannot take audio input. ...`, then `Endpoint 'llama-listen' (OpenAiCompatible) accepts only Mp3 or Wav audio. ...`, then, only while `llama-listen` is unhealthy, `Nothing to analyze: no usable audio-capable endpoint and Transcribe is off.` With the server healthy, the third request is accepted and queued. The rejected requests never download the media or create a job.

### Listening in a container (`listen` profile)

```bash
# Stop the host llama-server first: both bind port 8720.
(cd docker && docker compose --profile listen up -d && docker compose ps keyframe-llama keyframe-server)

# Point llama-listen at the container on the compose network.
aget /v1.0/settings | jq '.Settings | (.Inference.Endpoints[] | select(.Name == "llama-listen") | .Endpoint) = "http://keyframe-llama:8080/v1"' \
  | curl -sS -X PUT $KF/v1.0/settings -H "x-api-key: $ADMIN" -H "Content-Type: application/json" --data-binary @- | jq .RestartRequired
```

Expected: on the first start `keyframe-llama` downloads the model into `docker/llama-models`, and `keyframe-server` does not start until `keyframe-llama` is healthy (the server's `depends_on` waits on it; this needs Docker Compose 2.20 or later). Without any model profile, `docker compose up -d` starts the server at once. The settings change applies live (`RestartRequired` false). Rerun the first listening scenario: it reports `Listened` true from `llama-listen`, much more slowly on a Mac, because the container has no Metal access.

### Listening on a GPU (`listen-gpu` profile)

On a host with an NVIDIA GPU (about 10 GB free) and the NVIDIA container toolkit:

```bash
(cd docker && docker compose --profile listen-gpu up -d)

aget /v1.0/settings | jq '.Settings | .Inference.DefaultListening = "vllm-voxtral" | (.Inference.Endpoints[] | select(.Name == "vllm-voxtral") | .Active) = true' \
  | curl -sS -X PUT $KF/v1.0/settings -H "x-api-key: $ADMIN" -H "Content-Type: application/json" --data-binary @- | jq .RestartRequired
```

Expected: the server starts after `keyframe-vllm` is healthy. The first start downloads the model into `docker/vllm-models`; put `HF_TOKEN` in an untracked `docker/.env` if the download needs it. The first listening scenario then reports `Endpoint` `vllm-voxtral`, `Model` `mistralai/Voxtral-Mini-3B-2507`, and `Mode` `Full`, and the excerpts scenario is heard whole, because `vllm-voxtral` uses the 1200-second default budget. The Postman request "Listen with vLLM (GPU)" does the same.

### Model persistence

Models are gigabytes, and a restart must never download them again. With the model profiles you use running and their models downloaded:

```bash
cd docker
du -sh ollama-models llama-models vllm-models 2>/dev/null

docker compose --profile ollama --profile listen restart
docker compose --profile ollama --profile listen down && docker compose --profile ollama --profile listen up -d
COMPOSE_PROFILES=ollama,listen ./factory/reset.sh -y

du -sh ollama-models llama-models vllm-models 2>/dev/null
docker logs keyframe-llama 2>&1 | tail -20
docker logs keyframe-ollama-init 2>&1 | tail -5
cd ..
```

On a GPU host, add `listen-gpu` to the profile lists and check `docker logs keyframe-vllm` as well. Expected: after each step the containers come back healthy, the directory sizes do not change, and the logs show each server loading its model from disk with no download progress. The factory reset keeps all three model directories and the Whisper model volume. It also restores `keyframe.json`, so `llama-listen` points back at the host llama-server afterward.

### Transcripts: meetings, voicemail, and languages

These scenarios are about the transcript and what can be read from it. They listen too, by default; add `Listen: {Enable: false}` to any of them to compare against a transcript-only answer from `gpt-oss:20b`.

#### Meeting: summary and action items

```bash
jq -n --rawfile d <(b64 $TD/audio/meeting-two-speakers.mp3) '{
  Audio: {Data: $d, Filename: "meeting-two-speakers.mp3"},
  Wait: true,
  Context: ["Weekly project status meeting."],
  Functions: {
    Transcribe: {Enable: true, Language: "en", Prompt: "Atlas, Sarah, Daniel"},
    Summarize: {Enable: true, MaxWords: 80},
    Extract: {Enable: true, Fields: [
      {Key: "project",      Type: "String",     Prompt: "Name of the project discussed."},
      {Key: "speakerNames", Type: "StringList", Prompt: "Names of the people speaking.", MaxItems: 5},
      {Key: "actionItems",  Type: "StringList", Prompt: "Action items, each with an owner.", MaxItems: 10},
      {Key: "isBlocked",    Type: "Boolean",    Prompt: "Is any workstream blocked?"},
      {Key: "weeksBehind",  Type: "Integer",    Prompt: "How many weeks behind is the billing migration?", Minimum: 0, Maximum: 52}
    ]}
  }
}' | kpost /v1.0/audio > /tmp/kf-meeting.json

jq '{Success, Results, Warnings, Errors}' /tmp/kf-meeting.json
jq -r '.Transcript.Segments[] | "\(.StartMs)-\(.EndMs) ms: \(.Text)"' /tmp/kf-meeting.json
```

Expected: project Atlas, speakers Sarah and Daniel, two action items (email the finance director; prepare a rollback plan), `isBlocked` true, `weeksBehind` 2.

#### Voicemail: structured extraction

```bash
jq -n --rawfile d <(b64 $TD/audio/voicemail.m4a) '{Audio: {Data: $d}, Wait: true, Functions: {
  Transcribe: {Enable: true},
  Summarize: {Enable: true, Style: "Caption"},
  Extract: {Enable: true, Fields: [
    {Key: "caller",       Type: "String", Prompt: "Business that called."},
    {Key: "recipient",    Type: "String", Prompt: "Person the message is for."},
    {Key: "appointment",  Type: "String", Prompt: "Appointment date and time."},
    {Key: "callbackNumber", Type: "String", Prompt: "Phone number to call back, digits and dashes only."}]}}}' \
  | kpost /v1.0/audio | jq '{Results, Text: .Transcript.Text}'
```

Expected: Bayside Dental, Michael Chen, Thursday June 12 at 2:30 p.m., 555-214-8839.

#### Language detection, and audio with no speech

```bash
jq -n --rawfile d <(b64 $TD/audio/spanish-hotel-reservation.flac) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true, MaxWords: 30}}}' \
  | kpost /v1.0/audio | jq '{Language: .Transcript.Language, Text: .Transcript.Text, Summary: .Results.Summary}'

jq -n --rawfile d <(b64 $TD/audio/tone-and-silence-no-speech.ogg) '{Audio: {Data: $d}, Wait: true, Listen: {Enable: false},
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true}}}' \
  | kpost /v1.0/audio | jq '{Success, Text: .Transcript.Text, Summary: .Results.Summary, Warnings}'
```

The Spanish clip should report language `es`. The tone request turns listening off to test the transcript on its own. It should give an empty transcript, a null summary, and the warning that Summarize and Extract were skipped: Whisper tends to invent a word such as "You" over silence, and Keyframe drops segments whose no-speech probability is above 0.6 when the model is also unsure of the words (average log probability below -0.7).

#### The audio track of a video

```bash
jq -n --rawfile d <(b64 $TD/video/narrated-slideshow.mp4) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true, MaxWords: 30}}}' \
  | kpost /v1.0/audio | jq '{MediaKind, Text: .Transcript.Text}'
```

## 5. Segments

`POST /v1.0/segment` cuts a clip, an audio excerpt, or a still out of a source. Keyframe does not keep uploaded media, so send the same source again. Typical use: take `StartMs` and `EndMs` from a scene or transcript segment in an analysis result.

```bash
# Scene 2 of the slideshow analyzed in section 3, as an mp4 clip.
read S E < <(jq -r '.Scenes[1] | "\(.StartMs) \(.EndMs)"' /tmp/kf-slideshow.json)
jq -n --rawfile d <(b64 $TD/video/narrated-slideshow.mp4) --argjson s $S --argjson e $E \
  '{Source: {Data: $d}, StartMs: $s, EndMs: $e, Output: "Video", Format: "mp4"}' \
  | curl -sS -X POST $KF/v1.0/segment -H "x-api-key: $KEY" -H "Content-Type: application/json" --data-binary @- \
      -D /tmp/kf-seg-headers.txt -o /tmp/scene2.mp4
grep -i x-keyframe-segment /tmp/kf-seg-headers.txt
ffprobe -v error -show_entries format=duration -of csv=p=0 /tmp/scene2.mp4

# The audio of the first transcript segment of the meeting, as mp3.
read S E < <(jq -r '.Transcript.Segments[0] | "\(.StartMs) \(.EndMs)"' /tmp/kf-meeting.json)
jq -n --rawfile d <(b64 $TD/audio/meeting-two-speakers.mp3) --argjson s $S --argjson e $E \
  '{Source: {Data: $d}, StartMs: $s, EndMs: $e, Output: "Audio", Format: "mp3"}' \
  | curl -sS -X POST $KF/v1.0/segment -H "x-api-key: $KEY" -H "Content-Type: application/json" --data-binary @- -o /tmp/line1.mp3

# A still frame at 10 seconds, as JSON with base64 data.
jq -n --rawfile d <(b64 $TD/video/big-buck-bunny-45s.mp4) \
  '{Source: {Data: $d}, StartMs: 10000, Output: "Frame", Format: "jpg", MaxEdge: 480, ResponseFormat: "Json"}' \
  | kpost /v1.0/segment | tee /tmp/kf-frame.json | jq '{MediaType, Filename, StartMs, SizeBytes}'
jq -r .Data /tmp/kf-frame.json | base64 --decode > /tmp/frame-10s.jpg

# From a URL source.
echo '{"Source":{"Url":"https://upload.wikimedia.org/wikipedia/commons/transcoded/c/c0/Big_Buck_Bunny_4K.webm/Big_Buck_Bunny_4K.webm.360p.webm"},"StartMs":0,"EndMs":5000,"Output":"Audio","Format":"wav"}' \
  | curl -sS -X POST $KF/v1.0/segment -H "x-api-key: $KEY" -H "Content-Type: application/json" --data-binary @- -o /tmp/bbb-5s.wav
```

## 6. Jobs

```bash
curl -sS "$KF/v1.0/jobs?maxResults=10" -H "x-api-key: $KEY" | jq -r '.Items[] | [.Id, .MediaKind, .Status, .DurationMs] | @tsv'

# Cancel a running job.
JOB=$(jq -n --rawfile d <(b64 $TD/video/elephants-dream-dialogue-60s.mp4) '{Video: {Data: $d}, Functions: {Summarize: {Enable: true}}}' | kpost /v1.0/video | jq -r .Id)
curl -sS -X POST $KF/v1.0/jobs/$JOB/cancel -H "x-api-key: $KEY" | jq '{Id, Status}'
```

## 7. Analytics

Every finished analysis is recorded with its stage timings and every model call. These routes need the admin key.

```bash
# Last 24 hours, all media.
aget /v1.0/analytics/summary | jq '{Count: .Totals.Count, SuccessRate: .Totals.SuccessRate,
  AnalysisMs: .Totals.AnalysisMs | {Avg, P50, P95},
  TtftMs: .Totals.TimeToFirstTokenMs | {Avg, P95},
  TokensPerSecond: .Totals.TokensPerSecond | {Avg, P50},
  Tokens: .Totals.TotalTokens}'

# Per media kind.
aget /v1.0/analytics/summary | jq '.ByMediaKind | map_values({Count, AvgMs: .AnalysisMs.Avg, P95Ms: .AnalysisMs.P95, RealtimeFactor: .RealtimeFactor.Avg})'

# Per model and per stage.
aget /v1.0/analytics/summary | jq -r '.ByModel[] | [.Model, .Calls, .Failed, .TimeToFirstTokenMs.P50, .TokensPerSecond.P50] | @tsv'
aget /v1.0/analytics/summary | jq -r '.ByStage[] | [.Stage, .DurationMs.Avg, .DurationMs.P95] | @tsv'

# Hourly buckets for the last 6 hours, audio only.
FROM=$(date -u -v-6H +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -d '-6 hours' +%Y-%m-%dT%H:%M:%SZ)
aget "/v1.0/analytics/summary?mediaKind=Audio&bucket=hour&fromUtc=$FROM" | jq -r '.Buckets[] | [.StartUtc, .Stats.Count, .Stats.AnalysisMs.Avg] | @tsv'

# Recent analyses, then one with every model call.
aget "/v1.0/analytics/analyses?maxResults=5" | jq -r '.Items[] | [.Id, .MediaKind, .Success, .AnalysisMs, .TotalTokens] | @tsv'
ID=$(aget "/v1.0/analytics/analyses?maxResults=1" | jq -r '.Items[0].Id')
aget /v1.0/analytics/analyses/$ID | jq -r '.Calls[] | [.Purpose, .Model, .Attempt, .Success, .QueueMs, .TimeToFirstTokenMs, .GenerationMs, .TokensPerSecond] | @tsv'
```

The dashboard's Analytics page (`http://127.0.0.1:8701/dashboard/analytics`) charts the same data.

## 8. Error handling

```bash
# 401 without a key, 403 for an admin route with an access key.
curl -sS -o /dev/null -w "%{http_code}\n" -X POST $KF/v1.0/image -d '{}'
curl -sS -o /dev/null -w "%{http_code}\n" $KF/v1.0/analytics/summary -H "x-api-key: $KEY"

# 400: no media, a bad field type, and an unknown endpoint.
echo '{}' | kpost /v1.0/image | jq '{Error, Message, Details}'
echo '{"Image":{"Url":"https://example.com/x.jpg"},"Functions":{"Extract":{"Enable":true,"Fields":[{"Key":"a","Type":"Nope","Prompt":"?"}]}}}' | kpost /v1.0/image | jq '{Error, Message, Details}'
echo '{"Image":{"Url":"https://example.com/x.jpg"},"VisionModel":{"Endpoint":"missing"},"Functions":{"Summarize":{"Enable":true}}}' | kpost /v1.0/image | jq '{Error, Message, Details}'

# 422: private network URLs are blocked by default (SSRF guard), reported as a media resolution failure.
echo '{"Image":{"Url":"http://127.0.0.1:8700/favicon.ico"},"Functions":{"Summarize":{"Enable":true}}}' | kpost /v1.0/image | jq '{Error, Message, Details}'

# 422: bytes that are not media.
jq -n --arg d "$(echo 'not an image' | base64)" '{Image: {Data: $d}, Functions: {Summarize: {Enable: true}}}' | kpost /v1.0/image | jq '{Error, Message, Details}'

# 400 or 422 from the segment route: end before start, and Audio from a silent video.
jq -n --rawfile d <(b64 $TD/video/scene-cuts-12s-silent.mp4) '{Source: {Data: $d}, StartMs: 5000, EndMs: 1000, Output: "Video"}' | kpost /v1.0/segment | jq '{Error, Message, Details}'
jq -n --rawfile d <(b64 $TD/video/scene-cuts-12s-silent.mp4) '{Source: {Data: $d}, StartMs: 0, EndMs: 2000, Output: "Audio"}' | kpost /v1.0/segment | jq '{Error, Message, Details}'
```

## 9. Evaluation script

This script runs the images and audio files that have known answers and prints a pass or fail line per check, the model calls, and timings. Save it as `evaluate.sh`, run it after the [setup](#setup) block, and compare runs across models by setting `VISION` and `TEXT` to other endpoint names. The audio checks turn listening off so that `TEXT` is the model that answers; set `LISTEN=true` to have the listening endpoint answer them instead.

```bash
#!/usr/bin/env bash
# Usage: VISION=ollama-gemma3-4b TEXT=ollama-gpt-oss-20b ./evaluate.sh
set -u
KF=${KF:-http://127.0.0.1:8700}; KEY=${KEY:-default}; TD=${TD:-test-data}
VISION=${VISION:-ollama-gemma3-4b}; TEXT=${TEXT:-ollama-gpt-oss-20b}; LISTEN=${LISTEN:-false}
pass=0; fail=0
b64() { base64 < "$1" | tr -d '\n'; }
post() { curl -sS -X POST "$KF$1" -H "x-api-key: $KEY" -H "Content-Type: application/json" --data-binary @-; }
check() { # name, jq expression that must be true, json
  if echo "$3" | jq -e "$2" > /dev/null 2>&1; then echo "PASS  $1"; pass=$((pass+1)); else echo "FAIL  $1  (got $(echo "$3" | jq -c .Results))"; fail=$((fail+1)); fi
}
image() { # file, fields json
  jq -n --rawfile d <(b64 "$TD/images/$1") --arg v "$VISION" --argjson f "$2" \
    '{Image: {Data: $d}, VisionModel: {Endpoint: $v}, Functions: {Summarize: {Enable: false}, Extract: {Enable: true, Fields: $f}}}' | post /v1.0/image
}
audio() { # file, fields json
  jq -n --rawfile d <(b64 "$TD/audio/$1") --arg t "$TEXT" --argjson l "$LISTEN" --argjson f "$2" \
    '{Audio: {Data: $d}, Wait: true, WaitTimeoutMs: 600000, Listen: {Enable: $l}, TextModel: {Endpoint: $t}, Functions: {Transcribe: {Enable: true}, Summarize: {Enable: false}, Extract: {Enable: true, Fields: $f}}}' | post /v1.0/audio
}

r=$(image cat.jpg '[{"Key":"animal","Type":"Enum","Prompt":"Which animal is shown?","Options":["cat","dog","other"]}]')
check "cat.jpg animal=cat" '.Results.animal == "cat"' "$r"

r=$(image dog.jpg '[{"Key":"animal","Type":"Enum","Prompt":"Which animal is shown?","Options":["cat","dog","other"]},{"Key":"isOutdoors","Type":"Boolean","Prompt":"Taken outdoors?"}]')
check "dog.jpg animal=dog" '.Results.animal == "dog"' "$r"
check "dog.jpg outdoors" '.Results.isOutdoors == true' "$r"

r=$(image eiffel-tower.jpg '[{"Key":"city","Type":"String","Prompt":"City where the photo was taken."}]')
check "eiffel-tower.jpg city=Paris" '.Results.city | ascii_downcase | test("paris")' "$r"

r=$(image receipt.jpg '[{"Key":"total","Type":"Number","Prompt":"Total amount charged."},{"Key":"currency","Type":"String","Prompt":"ISO currency code."}]')
check "receipt.jpg total=54.50" '.Results.total == 54.5' "$r"
check "receipt.jpg currency=CHF" '.Results.currency | ascii_upcase | test("CHF")' "$r"

r=$(image test-pattern.png '[{"Key":"hasPeople","Type":"Boolean","Prompt":"Are there any people in the image?"}]')
check "test-pattern.png hasPeople=false" '.Results.hasPeople == false' "$r"

r=$(audio voicemail.m4a '[{"Key":"callbackNumber","Type":"String","Prompt":"Callback phone number, digits only."},{"Key":"recipient","Type":"String","Prompt":"Person the message is for."}]')
check "voicemail.m4a number" '.Results.callbackNumber | gsub("[^0-9]"; "") | test("5552148839")' "$r"
check "voicemail.m4a recipient" '.Results.recipient | test("Michael")' "$r"

r=$(audio meeting-two-speakers.mp3 '[{"Key":"project","Type":"String","Prompt":"Project name."},{"Key":"weeksBehind","Type":"Integer","Prompt":"Weeks behind on billing migration."}]')
check "meeting.mp3 project=Atlas" '.Results.project | test("Atlas"; "i")' "$r"
check "meeting.mp3 weeksBehind=2" '.Results.weeksBehind == 2' "$r"

r=$(audio spanish-hotel-reservation.flac '[{"Key":"hotel","Type":"String","Prompt":"Name of the hotel."}]')
check "spanish.flac language=es" '.Transcript.Language | ascii_downcase | test("^es|spanish")' "$r"
check "spanish.flac hotel=Mirador" '.Results.hotel | test("Mirador"; "i")' "$r"

echo
echo "passed $pass, failed $fail (vision $VISION, text $TEXT, listen $LISTEN)"
curl -sS "$KF/v1.0/analytics/summary?fromUtc=$(date -u -v-1H +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ)" \
  -H "x-api-key: ${ADMIN:-keyframeadmin}" | jq -r '.ByModel[] | "\(.Model): \(.Calls) calls, TTFT p50 \(.TimeToFirstTokenMs.P50 // "n/a") ms, \(.TokensPerSecond.P50 // "n/a") tok/s"'
```

## Live provider credentials

The live provider cases in the automated suites (`dotnet run --project src/Test.Automated -c Release`, suite `LiveProvider`) call real providers and are skipped, with the reason printed, unless their environment variables are set. The listening cases each analyze a short tone recording with Transcribe off and check that the audio was heard in full (`Mode` `Full`), that a summary and a typed field came back, and that every completion call carried audio bytes.

| Case | Variables |
|---|---|
| `GeminiListen` | `GEMINI_API_KEY` (optional `KEYFRAME_TEST_GEMINI_MODEL`) |
| `OpenAiListen` | `OPENAI_API_KEY` (optional `KEYFRAME_TEST_OPENAI_AUDIO_MODEL`, default `gpt-4o-audio-preview`). Runs as an `OpenAiCompatible` endpoint at `https://api.openai.com/v1` |
| `LlamaServerListen` | `KEYFRAME_TEST_LLAMA_URL`, for example `http://127.0.0.1:8720/v1` (optional `KEYFRAME_TEST_LLAMA_MODEL`) |
| `VllmListen` | `KEYFRAME_TEST_VLLM_URL`, for example `http://gpu-host:8730/v1` (optional `KEYFRAME_TEST_VLLM_MODEL`, default `mistralai/Voxtral-Mini-3B-2507`) |

The image cases use `GEMINI_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, and `KEYFRAME_TEST_OLLAMA_URL` with `KEYFRAME_TEST_OLLAMA_MODEL`; `WhisperAudio` uses `KEYFRAME_TEST_WHISPER_URL`. The listening cases need ffmpeg as well.

**Instructions for an agent running these cases.** Do not look for keys in the repository, in shell history, or in other projects, and do not guess a GPU host. Before running any live case, ask the owner for the Gemini API key, the OpenAI API key, and the address of the GPU host running vLLM (the `listen-gpu` profile). Pass what you are given only as the environment variables above, for that one session:

```bash
GEMINI_API_KEY=... OPENAI_API_KEY=... KEYFRAME_TEST_LLAMA_URL=http://127.0.0.1:8720/v1 \
KEYFRAME_TEST_VLLM_URL=http://<gpu-host>:8730/v1 \
  dotnet run --project src/Test.Automated -c Release
```

Never write a key or the GPU host into a file: not `keyframe.json`, a `.env` file, a script, test data, the Postman collection, a commit, or a plan or progress log. Keep keys out of logs and screenshots too; do not echo them, and redact them if a command prints one. Cases whose variables the owner does not provide stay skipped, which is not a failure. The vLLM port (8730) is bound to loopback on the GPU host, so reach it through an SSH tunnel (`ssh -L 8730:127.0.0.1:8730 <gpu-host>`, then use `http://127.0.0.1:8730/v1`) unless the owner says otherwise.

## What to look at

- **Accuracy:** the evaluation script, plus the expected answers in [test-data/README.md](test-data/README.md). Swap `VISION` between `ollama-gemma3-4b` and `ollama-qwen3-vl-8b` to compare models, and run it with `LISTEN=true` to compare a listening model against the transcript-only text model on the same audio.
- **Listening:** `Listening.Listened`, `Mode`, `Reason`, and `HeardMs` against `DurationMs` in every audio result, and the warnings when the model heard excerpts or fell back to the transcript.
- **Field validity:** `Fields[].Valid`, `Error`, and `Warnings` show coercion problems (an out-of-range integer, an enum value that was not offered).
- **Timing:** `Telemetry.Stages` (where the time went), `Telemetry.ModelCalls` (queue, time to first token, generation time, tokens per second, attempts, repairs), and `Telemetry.Totals`. The Analytics page shows averages and percentiles over time.
- **Robustness:** the no-speech audio, the test pattern, and the error cases in section 8 should fail cleanly or return nulls, never invented content.
- **Throughput:** run several analyses at once (for example the audio examples in parallel with `&`) and watch queue wait and p95 times in analytics. `MaxConcurrentRequests` on each endpoint and `Jobs.MaxConcurrentJobs` control concurrency.
