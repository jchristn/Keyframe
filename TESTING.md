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
- [What to look at](#what-to-look-at)

## Prerequisites

- A running server. Either the Docker stack (`cd docker && docker compose up -d`, server on port 8700, dashboard on 8701) or `dotnet run -c Release` in `src/Keyframe.Server`.
- The default model endpoints:
  - Ollama on the host at `127.0.0.1:11434` with `gemma3:4b`, `qwen3-vl:8b`, and `gpt-oss:20b` pulled (`ollama pull gemma3:4b` and so on).
  - A Whisper server for transcription. The Docker stack runs one (`keyframe-whisper`, with `keyframe-whisper-init` downloading `Systran/faster-whisper-small` on first start). Without Docker, run `docker compose -f docker/compose.yaml up -d keyframe-whisper keyframe-whisper-init`.
- `curl`, `jq`, and `base64`.

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
aget /v1.0/endpoints | jq -r '.[] | [.Name, .Provider, .Kind, .Model, .ProviderAvailable, .Healthy, .HealthMessage] | @tsv'

# Probe each default endpoint now (a short prompt for completion endpoints).
for e in ollama-gemma3-4b ollama-qwen3-vl-8b ollama-gpt-oss-20b whisper; do
  curl -sS -X POST $KF/v1.0/endpoints/$e/test -H "x-api-key: $ADMIN" | jq -c '{Name, Success, DurationMs, Error}'
done
```

Expect `Success: true` for all four. A failure here means the model is not pulled or the Whisper model is not installed yet; fix that before going on.

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

Audio analysis transcribes with the Whisper endpoint and then summarizes and extracts from the transcript with the text model (`gpt-oss:20b`).

### Meeting: summary and action items

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

### Voicemail: structured extraction

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

### Language detection, and audio with no speech

```bash
jq -n --rawfile d <(b64 $TD/audio/spanish-hotel-reservation.flac) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true, MaxWords: 30}}}' \
  | kpost /v1.0/audio | jq '{Language: .Transcript.Language, Text: .Transcript.Text, Summary: .Results.Summary}'

jq -n --rawfile d <(b64 $TD/audio/tone-and-silence-no-speech.ogg) '{Audio: {Data: $d}, Wait: true,
  Functions: {Transcribe: {Enable: true}, Summarize: {Enable: true}}}' \
  | kpost /v1.0/audio | jq '{Success, Text: .Transcript.Text, Summary: .Results.Summary, Warnings}'
```

The Spanish clip should report language `es`. The tone should give an empty transcript: Whisper tends to invent a word such as "You" over silence, and Keyframe drops segments whose no-speech probability is above 0.6 when the model is also unsure of the words (average log probability below -0.7).

### The audio track of a video

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

This script runs the images and audio files that have known answers and prints a pass or fail line per check, the model calls, and timings. Save it as `evaluate.sh`, run it after the [setup](#setup) block, and compare runs across models by setting `VISION` and `TEXT` to other endpoint names.

```bash
#!/usr/bin/env bash
# Usage: VISION=ollama-gemma3-4b TEXT=ollama-gpt-oss-20b ./evaluate.sh
set -u
KF=${KF:-http://127.0.0.1:8700}; KEY=${KEY:-default}; TD=${TD:-test-data}
VISION=${VISION:-ollama-gemma3-4b}; TEXT=${TEXT:-ollama-gpt-oss-20b}
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
  jq -n --rawfile d <(b64 "$TD/audio/$1") --arg t "$TEXT" --argjson f "$2" \
    '{Audio: {Data: $d}, Wait: true, TextModel: {Endpoint: $t}, Functions: {Transcribe: {Enable: true}, Summarize: {Enable: false}, Extract: {Enable: true, Fields: $f}}}' | post /v1.0/audio
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
echo "passed $pass, failed $fail (vision $VISION, text $TEXT)"
curl -sS "$KF/v1.0/analytics/summary?fromUtc=$(date -u -v-1H +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ)" \
  -H "x-api-key: ${ADMIN:-keyframeadmin}" | jq -r '.ByModel[] | "\(.Model): \(.Calls) calls, TTFT p50 \(.TimeToFirstTokenMs.P50 // "n/a") ms, \(.TokensPerSecond.P50 // "n/a") tok/s"'
```

## What to look at

- **Accuracy:** the evaluation script, plus the expected answers in [test-data/README.md](test-data/README.md). Swap `VISION` between `ollama-gemma3-4b` and `ollama-qwen3-vl-8b` to compare models.
- **Field validity:** `Fields[].Valid`, `Error`, and `Warnings` show coercion problems (an out-of-range integer, an enum value that was not offered).
- **Timing:** `Telemetry.Stages` (where the time went), `Telemetry.ModelCalls` (queue, time to first token, generation time, tokens per second, attempts, repairs), and `Telemetry.Totals`. The Analytics page shows averages and percentiles over time.
- **Robustness:** the no-speech audio, the test pattern, and the error cases in section 8 should fail cleanly or return nulls, never invented content.
- **Throughput:** run several analyses at once (for example the audio examples in parallel with `&`) and watch queue wait and p95 times in analytics. `MaxConcurrentRequests` on each endpoint and `Jobs.MaxConcurrentJobs` control concurrency.
