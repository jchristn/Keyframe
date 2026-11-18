"""End-to-end harness for keyframe_sdk against a live Keyframe server using the mock provider."""

from __future__ import annotations

import copy
import json
import time
import uuid
from datetime import datetime, timedelta, timezone
from typing import Callable

import pytest

from conftest import ACCESS_KEY, ADMIN_KEY, URL, Media
from keyframe_sdk import (
    AccessKeySettings,
    AnalysisRecord,
    AnalyticsSummary,
    AudioAnalysisRequest,
    AudioAnalysisResponse,
    AudioFunctionsSpec,
    ContextItem,
    EndpointKind,
    ExtractMode,
    ExtractSpec,
    FieldSpec,
    FieldType,
    FunctionsSpec,
    ImageAnalysisRequest,
    JobStatus,
    KeyframeApiError,
    KeyframeClient,
    KeyframeConnectionError,
    KeyframeJobError,
    KeyframeSettings,
    ListenClipScope,
    ListenFormat,
    ListeningMode,
    ListenSkipReason,
    ListenSpec,
    MediaKind,
    MediaSource,
    ModelCallPurpose,
    ModelSelection,
    ProbeRequest,
    ProviderType,
    SceneDetectionSpec,
    SegmentMedia,
    SegmentOutput,
    SegmentRequest,
    SegmentResponse,
    ScenesSpec,
    SectionsSpec,
    SummarizeSpec,
    SummaryStyle,
    TranscribeSpec,
    VideoAnalysisRequest,
    VideoAnalysisResponse,
    VideoFunctionsSpec,
    media_from_bytes,
    media_from_file,
    normalize_context,
)


def expect_api_error(status: int, fn: Callable[[], object]) -> KeyframeApiError:
    with pytest.raises(KeyframeApiError) as info:
        fn()
    assert info.value.status_code == status, str(info.value)
    return info.value


FIELDS = [
    FieldSpec(key="hasCats", type=FieldType.BOOLEAN, prompt="Does this image have cats?"),
    FieldSpec(key="dogCount", type=FieldType.INTEGER, prompt="How many dogs?", minimum=0, maximum=20),
    FieldSpec(key="setting", type=FieldType.ENUM, prompt="Where was this taken?", options=["Indoors", "Outdoors", "Unknown"]),
    FieldSpec(key="colors", type=FieldType.STRING_LIST, prompt="List the dominant colors.", max_items=5),
    FieldSpec(key="brightness", type=FieldType.NUMBER, prompt="Brightness 0 to 100.", minimum=0, maximum=100),
    FieldSpec(key="caption", type=FieldType.STRING, prompt="Short caption.", max_length=80),
    FieldSpec(key="details", type=FieldType.OBJECT, prompt="Any structured details."),
]


def video_request(path, wait: bool) -> VideoAnalysisRequest:
    return VideoAnalysisRequest(
        video=media_from_file(path),
        context=[ContextItem(label="Event", text="Synthetic test video.")],
        wait=wait,
        wait_timeout_ms=60000,
        functions=VideoFunctionsSpec(
            summarize=SummarizeSpec(enable=True),
            extract=ExtractSpec(enable=True, fields=[FieldSpec(key="topics", type=FieldType.STRING_LIST, prompt="Main topics.", max_items=4)]),
            scenes=ScenesSpec(
                enable=True,
                frames_per_scene=2,
                summarize=SummarizeSpec(enable=True, max_words=20),
                extract=ExtractSpec(enable=True, fields=[FieldSpec(key="showsSlide", type=FieldType.BOOLEAN, prompt="Is a slide visible?")]),
            ),
            transcribe=TranscribeSpec(enable=True, language="en"),
        ),
    )


def check_video_result(result: VideoAnalysisResponse) -> None:
    assert isinstance(result, VideoAnalysisResponse)
    assert result.success, "; ".join(result.errors)
    assert result.media_kind == MediaKind.VIDEO
    assert result.scenes, "no scenes"
    assert all(s.keyframes for s in result.scenes), "scene without keyframes"
    assert "showsSlide" in result.scenes[0].results
    assert result.transcript is not None and result.transcript.segments
    assert isinstance(result.results.get("topics"), list)
    assert result.properties is not None and result.properties.has_audio


# System


class TestSystem:
    def test_health_anonymous(self, anonymous: KeyframeClient, admin: KeyframeClient) -> None:
        health = anonymous.health.get()
        assert health.product == "Keyframe"
        assert health.status in ("Healthy", "Degraded")
        assert health.version
        assert "mock" in health.endpoints

    def test_is_alive(self, anonymous: KeyframeClient) -> None:
        assert anonymous.health.is_alive()

    def test_raw_openapi(self, anonymous: KeyframeClient) -> None:
        raw = anonymous.send_raw("GET", "/openapi.json")
        assert raw.status_code == 200
        assert "/v1.0/image" in raw.body

    def test_unreachable(self) -> None:
        with KeyframeClient("http://127.0.0.1:1", ACCESS_KEY, timeout_s=5) as dead:
            assert not dead.health.is_alive()
            with pytest.raises(KeyframeConnectionError):
                dead.health.get()

    def test_key_never_in_repr(self, admin: KeyframeClient, anonymous: KeyframeClient) -> None:
        assert ADMIN_KEY not in repr(admin)
        assert ADMIN_KEY not in str(admin)
        assert admin.has_api_key
        assert not anonymous.has_api_key

    def test_rejects_bad_base_url(self) -> None:
        with pytest.raises(ValueError):
            KeyframeClient("ftp://127.0.0.1")


# Auth


class TestAuth:
    def test_whoami_no_key_401(self, anonymous: KeyframeClient) -> None:
        e = expect_api_error(401, anonymous.whoami.get)
        assert e.error == "AuthenticationFailed"
        assert e.api_error is not None and e.api_error.status_code == 401
        assert e.request_id
        assert e.method == "GET" and e.path == "/v1.0/whoami"

    def test_whoami_bad_key_401(self) -> None:
        with KeyframeClient(URL, "not-a-real-key-" + uuid.uuid4().hex) as bad:
            expect_api_error(401, bad.whoami.get)

    def test_whoami_access_key_bearer(self, user: KeyframeClient) -> None:
        me = user.whoami.get()
        assert me.is_authenticated and not me.is_admin and me.principal_name

    def test_whoami_access_key_x_api_key(self) -> None:
        with KeyframeClient(URL, ACCESS_KEY, auth_header="x-api-key") as client:
            me = client.whoami.get()
            assert me.is_authenticated and not me.is_admin

    def test_whoami_admin(self, admin: KeyframeClient) -> None:
        assert admin.whoami.get().is_admin

    def test_admin_only_routes_403(self, user: KeyframeClient) -> None:
        e = expect_api_error(403, user.settings.get)
        assert e.error == "AuthorizationFailed"
        expect_api_error(403, lambda: user.settings.update(KeyframeSettings()))
        expect_api_error(403, user.settings.restart)
        expect_api_error(403, lambda: user.endpoints.test("mock"))
        expect_api_error(403, lambda: user.request_history.delete("req_doesnotexist"))
        expect_api_error(403, lambda: user.request_history.delete_many(path_contains="/nothing"))


# Image


class TestImage:
    def test_context_normalization(self) -> None:
        items = normalize_context(["plain", {"Label": "L", "Text": "labeled"}, ContextItem(text="obj")])
        assert [i.text for i in items] == ["plain", "labeled", "obj"]
        assert items[1].label == "L"
        assert normalize_context("just text")[0].text == "just text"
        body = ImageAnalysisRequest(context=["a", {"Label": "L", "Text": "b"}]).to_dict(omit_none=True)
        assert body["Context"] == [{"Text": "a"}, {"Text": "b", "Label": "L"}]
        parsed = ImageAnalysisRequest.from_dict({"Context": "single"})
        assert parsed.context[0].text == "single"

    def test_analysis_with_context_and_typed_fields(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        response = user.analyze_image_file(
            media.image,
            ImageAnalysisRequest(
                context=["There are no animals in test patterns.", {"Label": "Source", "Text": "Synthetic ffmpeg test pattern."}],
                functions=FunctionsSpec(
                    summarize=SummarizeSpec(enable=True, max_words=40, style=SummaryStyle.CAPTION),
                    extract=ExtractSpec(enable=True, fields=FIELDS),
                ),
            ),
        )
        assert response.success, "; ".join(response.errors)
        assert response.id.startswith("kfa_")
        assert response.media_kind == MediaKind.IMAGE
        assert response.properties is not None
        assert (response.properties.width, response.properties.height) == (640, 360)
        assert isinstance(response.results["Summary"], str)
        assert len(response.fields) == 7
        assert all(f.valid for f in response.fields)
        r = response.results
        assert isinstance(r["hasCats"], bool)
        assert isinstance(r["dogCount"], int) and 0 <= r["dogCount"] <= 20
        assert r["setting"] in ("Indoors", "Outdoors", "Unknown")
        assert isinstance(r["colors"], list) and len(r["colors"]) <= 5
        assert isinstance(r["brightness"], (int, float)) and 0 <= r["brightness"] <= 100
        assert isinstance(r["caption"], str)
        assert isinstance(r["details"], dict)
        dog = response.field_result("dogCount")
        assert dog is not None and dog.type == FieldType.INTEGER and dog.value == r["dogCount"]
        assert response.telemetry.model_calls
        assert any(s.name == "Extract" for s in response.telemetry.stages)
        assert response.telemetry.totals.model_calls >= 2

    def test_per_field_mode(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        response = user.image.analyze(ImageAnalysisRequest(
            image=media_from_file(media.image),
            functions=FunctionsSpec(extract=ExtractSpec(enable=True, mode=ExtractMode.PER_FIELD, fields=FIELDS)),
        ))
        calls = [c for c in response.telemetry.model_calls if c.purpose == ModelCallPurpose.EXTRACT]
        assert len(calls) == 7
        assert all(c.target for c in calls)

    def test_dict_request(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        response = user.image.analyze({
            "Image": media_from_file(media.image).to_dict(omit_none=True),
            "Context": "plain string context",
            "Functions": {"Summarize": {"Enable": True}},
        })
        assert response.success and "Summary" in response.results

    def test_validation_400(self, user: KeyframeClient) -> None:
        e = expect_api_error(400, lambda: user.image.analyze(ImageAnalysisRequest(
            image=MediaSource(url="https://example.com/a.jpg", data="AAAA"),
            functions=FunctionsSpec(extract=ExtractSpec(enable=True, fields=[FieldSpec(key="bad key!", type=FieldType.ENUM, prompt="")])),
        )))
        assert e.error == "BadRequest"
        assert len(e.details) >= 3, e.response_body

    def test_undecodable_422(self, user: KeyframeClient) -> None:
        e = expect_api_error(422, lambda: user.image.analyze(ImageAnalysisRequest(
            image=media_from_bytes(b"this is not an image at all, just text bytes", "image/jpeg"),
            functions=FunctionsSpec(summarize=SummarizeSpec(enable=True)),
        )))
        assert e.error == "UnprocessableMedia"

    def test_unknown_endpoint_400(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        expect_api_error(400, lambda: user.image.analyze(ImageAnalysisRequest(
            image=media_from_file(media.image),
            model=ModelSelection(endpoint="no-such-endpoint"),
            functions=FunctionsSpec(summarize=SummarizeSpec(enable=True)),
        )))


# Probe


class TestProbe:
    def test_probe_image(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        props = user.probe.probe(media_from_file(media.image))
        assert props.media_kind == MediaKind.IMAGE
        assert props.width == 640
        assert props.sha256

    def test_probe_video(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        props = user.probe.probe(ProbeRequest(media=media_from_file(media.video)))
        assert props.media_kind == MediaKind.VIDEO
        assert props.duration_ms is not None and abs(props.duration_ms - 6000) < 500
        assert props.has_audio
        assert props.video_codec == "h264"

    def test_probe_invalid_400(self, user: KeyframeClient) -> None:
        expect_api_error(400, lambda: user.probe.probe(ProbeRequest()))


# Video and jobs


class TestVideo:
    def test_lifecycle(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        submitted = user.video.analyze(video_request(media.video, False))
        assert submitted.status_code == 202
        assert submitted.job is not None and submitted.result is None
        assert submitted.job_id.startswith("kfj_")
        assert submitted.job.status in (JobStatus.QUEUED, JobStatus.RUNNING)

        done = user.jobs.wait(submitted.job_id, poll_interval_s=0.2, timeout_s=120)
        assert done.status == JobStatus.SUCCEEDED
        assert done.result_url and done.completed_utc
        assert done.progress.percent == 100

        check_video_result(user.jobs.get_result(submitted.job_id))

        page = user.jobs.list(status=JobStatus.SUCCEEDED, media_kind="Video", page_size=100)
        assert any(j.id == submitted.job_id for j in page.items)
        assert all(j.status == JobStatus.SUCCEEDED for j in page.items)

        user.jobs.delete(submitted.job_id)
        expect_api_error(404, lambda: user.jobs.get(submitted.job_id))

    def test_wait_returns_result(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        submitted = user.video.analyze(video_request(media.video, True))
        assert submitted.status_code == 200
        assert submitted.result is not None and submitted.job is None
        assert submitted.job_id.startswith("kfj_")
        check_video_result(submitted.result)
        user.jobs.delete(submitted.job_id)

    def test_analyze_video_and_wait(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        check_video_result(user.analyze_video_and_wait(video_request(media.video, False), poll_interval_s=0.2, timeout_s=120))

    def test_cancel_queued(self, admin: KeyframeClient, user: KeyframeClient, media: Media) -> None:
        media.require()
        workers = admin.settings.get().settings.jobs.max_concurrent_jobs
        blocker = video_request(media.long_video, False)
        blocker.functions.scenes.detection = SceneDetectionSpec(min_scene_ms=1000, max_scene_ms=2000)
        blockers = [user.video.analyze(blocker).job_id for _ in range(workers)]
        target = user.video.analyze(video_request(media.video, False))
        try:
            before = user.jobs.get(target.job_id)
            if before.status != JobStatus.QUEUED:
                pytest.skip(f"target job was {before.status} rather than Queued")
            conflict = expect_api_error(409, lambda: user.jobs.get_result(target.job_id))
            assert conflict.error == "Conflict"
            expect_api_error(409, lambda: user.jobs.delete(target.job_id))
            canceled = user.jobs.cancel(target.job_id)
            assert canceled.status == JobStatus.CANCELED and canceled.is_terminal
            expect_api_error(409, lambda: user.jobs.cancel(target.job_id))
            expect_api_error(409, lambda: user.jobs.get_result(target.job_id))
            user.jobs.delete(target.job_id)
        finally:
            for job_id in blockers:
                user.jobs.wait(job_id, poll_interval_s=0.25, timeout_s=180)
                user.jobs.delete(job_id)

    def test_failed_job_raises(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        request = video_request(media.video, False)
        request.video = media_from_file(media.image, "video/mp4")
        try:
            user.video.analyze_and_wait(request, poll_interval_s=0.2, timeout_s=60)
        except KeyframeJobError as e:
            assert e.job.status == JobStatus.FAILED
            assert e.job.error
            user.jobs.delete(e.job.id)
        except KeyframeApiError as e:
            if e.status_code in (400, 422):
                pytest.skip(f"server rejected the mismatched media at submit time ({e.status_code})")
            raise
        else:
            pytest.fail("an image submitted as video unexpectedly succeeded")

    def test_list_filters_and_paging(self, user: KeyframeClient) -> None:
        one = user.jobs.list(page_size=1)
        assert one.page_size == 1 and len(one.items) <= 1
        none = user.jobs.list(from_utc=datetime.now(timezone.utc) + timedelta(days=1))
        assert none.items == []
        expect_api_error(400, lambda: user.jobs.list(status="Bogus"))

    def test_unknown_job_404(self, user: KeyframeClient) -> None:
        e = expect_api_error(404, lambda: user.jobs.get("kfj_doesnotexist"))
        assert e.error == "NotFound"
        expect_api_error(404, lambda: user.jobs.cancel("kfj_doesnotexist"))
        expect_api_error(404, lambda: user.jobs.delete("kfj_doesnotexist"))


# Audio


def audio_request(path, wait: bool) -> AudioAnalysisRequest:
    return AudioAnalysisRequest(
        audio=media_from_file(path),
        context="Synthetic test tone.",
        wait=wait,
        wait_timeout_ms=60000,
        functions=AudioFunctionsSpec(
            transcribe=TranscribeSpec(enable=True, language="en"),
            summarize=SummarizeSpec(enable=True, max_words=30),
            extract=ExtractSpec(enable=True, fields=[FieldSpec(key="speakerCount", type=FieldType.INTEGER, prompt="How many speakers?")]),
        ),
    )


def check_audio_result(result: AudioAnalysisResponse) -> None:
    assert isinstance(result, AudioAnalysisResponse)
    assert result.success, "; ".join(result.errors)
    assert result.media_kind == MediaKind.AUDIO
    assert result.id.startswith("kfa_")
    assert result.request_id
    assert result.transcript is not None and result.transcript.text and result.transcript.segments
    assert "Summary" in result.results and "speakerCount" in result.results
    assert result.properties is not None and result.properties.has_audio and result.properties.duration_ms


@pytest.fixture(scope="module")
def audio_analysis(user: KeyframeClient, media: Media) -> AudioAnalysisResponse:
    media.require()
    return user.audio.analyze_and_wait(audio_request(media.audio, False), poll_interval_s=0.2, timeout_s=120)


class TestAudio:
    def test_defaults(self) -> None:
        body = AudioAnalysisRequest().to_dict(omit_none=True)
        assert body["Functions"]["Transcribe"]["Enable"] is True
        assert body["Functions"]["Summarize"]["Enable"] is True
        assert body["Functions"]["Extract"]["Enable"] is False
        assert body["WaitTimeoutMs"] == 120000
        assert body["Listen"]["Enable"] is True
        assert body["Listen"]["SampleClips"] == 4 and body["Listen"]["ClipSeconds"] == 30
        assert body["Listen"]["Rendition"] == {}
        assert body["Functions"]["Measure"]["Enable"] is True
        assert body["Functions"]["Sections"]["Enable"] is False
        assert body["Functions"]["Sections"]["Detection"]["MinSilenceMs"] == 1500
        assert ScenesSpec().to_dict(omit_none=True)["Audio"]["Enable"] is True

    def test_lifecycle(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        submitted = user.audio.analyze(audio_request(media.audio, False))
        assert submitted.status_code == 202
        assert submitted.job is not None and submitted.result is None
        assert submitted.job_id.startswith("kfj_")
        assert submitted.job.media_kind == MediaKind.AUDIO

        done = user.jobs.wait(submitted.job_id, poll_interval_s=0.2, timeout_s=120)
        assert done.status == JobStatus.SUCCEEDED, done.error
        check_audio_result(user.jobs.get_audio_result(submitted.job_id))

        page = user.jobs.list(media_kind="Audio", page_size=100)
        assert any(j.id == submitted.job_id for j in page.items)
        assert all(j.media_kind == MediaKind.AUDIO for j in page.items)
        user.jobs.delete(submitted.job_id)

    def test_wait_returns_result(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        submitted = user.audio.analyze(audio_request(media.audio, True))
        assert submitted.status_code == 200
        assert submitted.result is not None and submitted.job is None
        assert submitted.job_id.startswith("kfj_")
        check_audio_result(submitted.result)
        user.jobs.delete(submitted.job_id)

    def test_analyze_and_wait(self, audio_analysis: AudioAnalysisResponse) -> None:
        check_audio_result(audio_analysis)

    def test_result_content(self, audio_analysis: AudioAnalysisResponse) -> None:
        t = audio_analysis.telemetry
        assert t.queue_wait_ms is not None and t.queue_wait_ms >= 0
        assert t.queued_ms is not None
        stages = [s.name for s in t.stages]
        assert "AudioExtraction" in stages and "Transcribe" in stages and "Summarize" in stages
        transcribe = [c for c in t.model_calls if c.purpose == ModelCallPurpose.TRANSCRIBE]
        assert transcribe and all(c.input_audio_bytes > 0 for c in transcribe)
        completion = [c for c in t.model_calls if c.purpose in (ModelCallPurpose.SUMMARIZE, ModelCallPurpose.EXTRACT)]
        assert completion, "no completion calls"
        for call in completion:
            assert call.success, call.error
            assert call.queue_ms >= 0
            assert call.generation_ms is not None
            assert call.tokens_per_second is not None
        field = audio_analysis.field_result("speakerCount")
        assert field is not None and field.type == FieldType.INTEGER

    def test_default_listens_through_mock(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        result = user.audio.analyze_and_wait(AudioAnalysisRequest(audio=media_from_file(media.audio)), poll_interval_s=0.2, timeout_s=120)
        assert result.success, "; ".join(result.errors)
        listening = result.listening
        assert listening.listened, f"not listened, reason {listening.reason}"
        assert listening.mode == ListeningMode.FULL
        assert listening.reason is None
        assert listening.endpoint == "mock-listen"
        assert listening.rendition.format == ListenFormat.MP3
        assert listening.rendition.sample_rate and listening.rendition.channels
        assert listening.duration_ms > 0 and listening.heard_ms > 0
        assert any(c.scope == ListenClipScope.RECORDING and c.index is None and c.end_ms > c.start_ms and c.size_bytes > 0 for c in listening.clips)
        assert not listening.extra and all(not c.extra for c in listening.clips), "unmapped listening fields"
        assert result.measurements is not None, "Measurements missing (Measure is on by default)"
        assert result.sections is None
        assert "ListenExtraction" in [st.name for st in result.telemetry.stages]
        assert not result.extra, f"unmapped audio response fields: {sorted(result.extra)}"

    def test_listen_disabled_not_requested(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        request = AudioAnalysisRequest(audio=media_from_file(media.audio), listen=ListenSpec(enable=False))
        result = user.audio.analyze_and_wait(request, poll_interval_s=0.2, timeout_s=120)
        assert result.success, "; ".join(result.errors)
        assert not result.listening.listened
        assert result.listening.mode == ListeningMode.NONE
        assert result.listening.reason == ListenSkipReason.NOT_REQUESTED
        assert result.listening.clips == []

    def test_measure_only(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        request = AudioAnalysisRequest(
            audio=media_from_file(media.audio),
            functions=AudioFunctionsSpec(transcribe=TranscribeSpec(enable=False), summarize=SummarizeSpec(enable=False)),
        )
        result = user.audio.analyze_and_wait(request, poll_interval_s=0.2, timeout_s=120)
        assert result.success, "; ".join(result.errors)
        assert result.transcript is None
        m = result.measurements
        assert m is not None, "Measurements missing"
        assert not m.extra, f"unmapped measurement fields: {sorted(m.extra)}"
        assert m.channels == 1
        assert isinstance(m.integrated_lufs, float) and isinstance(m.true_peak_dbtp, float)
        assert m.stereo_width_db is None and m.effectively_mono
        assert isinstance(m.clipping_likely, bool)
        assert 0 <= m.silence_ratio <= 1
        assert result.telemetry.model_calls == [], "measure-only made model calls"
        assert result.telemetry.totals.model_calls == 0
        assert result.listening.reason == ListenSkipReason.NO_MODEL_FUNCTIONS
        assert "Measure" in [st.name for st in result.telemetry.stages]

    def test_sections(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        functions = AudioFunctionsSpec(sections=SectionsSpec(enable=True, summarize=SummarizeSpec(enable=True)))
        result = user.audio.analyze_and_wait(AudioAnalysisRequest(audio=media_from_file(media.audio), functions=functions), poll_interval_s=0.2, timeout_s=120)
        assert result.success, "; ".join(result.errors)
        assert result.sections, "Sections missing or empty"
        first = result.sections[0]
        assert not first.extra, f"unmapped section fields: {sorted(first.extra)}"
        assert first.index == 0
        assert first.end_ms > first.start_ms and first.duration_ms > 0
        assert isinstance(first.results.get("Summary"), str)
        assert any(c.purpose == ModelCallPurpose.SECTION_SUMMARIZE for c in result.telemetry.model_calls)
        assert any(c.scope == ListenClipScope.SECTION and c.index == 0 for c in result.listening.clips)

    def test_validation_400(self, user: KeyframeClient) -> None:
        expect_api_error(400, lambda: user.audio.analyze(AudioAnalysisRequest()))
        expect_api_error(400, lambda: user.audio.analyze(AudioAnalysisRequest(
            audio=MediaSource(url="https://example.com/a.mp3"), transcription_model=ModelSelection(endpoint="no-such-endpoint"))))


# Segments


class TestSegments:
    def test_audio_binary(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        seg = user.segments.get(SegmentRequest(source=media_from_file(media.video), start_ms=1000, end_ms=3000, output=SegmentOutput.AUDIO))
        assert isinstance(seg, SegmentMedia)
        assert seg.data and seg.size_bytes == len(seg.data)
        assert seg.media_type == "audio/mpeg"
        assert seg.filename == "segment-1000-3000.mp3"
        assert (seg.start_ms, seg.end_ms, seg.duration_ms) == (1000, 3000, 2000)
        assert seg.request_id

    def test_audio_binary_from_audio_source(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        seg = user.segments.get({"Source": media_from_file(media.audio), "StartMs": 0, "EndMs": 60000, "Output": "Audio", "Format": "wav",
                                 "ResponseFormat": "Json"})
        assert seg.media_type == "audio/wav"
        assert seg.data[:4] == b"RIFF"
        assert seg.start_ms == 0 and seg.end_ms is not None and seg.end_ms <= 5100, "EndMs not clamped"
        assert seg.duration_ms == seg.end_ms - seg.start_ms

    def test_frame_binary(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        seg = user.segments.get(SegmentRequest(source=media_from_file(media.video), start_ms=2000, output="Frame", max_edge=160))
        assert seg.media_type == "image/jpeg"
        assert seg.data[:2] == b"\xff\xd8"
        assert seg.filename == "segment-2000.jpg"
        assert seg.start_ms == 2000

    def test_video_json(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        seg = user.segments.get_json(SegmentRequest(source=media_from_file(media.video), start_ms=0, end_ms=1500))
        assert isinstance(seg, SegmentResponse)
        assert seg.media_type == "video/mp4"
        assert seg.filename == "segment-0-1500.mp4"
        data = seg.decode_data()
        assert len(data) == seg.size_bytes > 0
        assert data[4:8] == b"ftyp"

    def test_validation_400(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        source = media_from_file(media.video)
        expect_api_error(400, lambda: user.segments.get(SegmentRequest(source=source, start_ms=3000, end_ms=3000, output="Audio")))
        expect_api_error(400, lambda: user.segments.get(SegmentRequest(source=source, start_ms=3000, end_ms=1000)))
        expect_api_error(400, lambda: user.segments.get_json(SegmentRequest(source=source, start_ms=0, end_ms=1000, output="Audio", format="png")))

    def test_audio_only_frame_422(self, user: KeyframeClient, media: Media) -> None:
        media.require()
        expect_api_error(422, lambda: user.segments.get(SegmentRequest(source=media_from_file(media.audio), start_ms=0, output="Frame")))


# Analytics


class TestAnalytics:
    def test_summary(self, admin: KeyframeClient, audio_analysis: AudioAnalysisResponse) -> None:
        summary = admin.analytics.summary(bucket="hour")
        assert isinstance(summary, AnalyticsSummary)
        assert summary.bucket == "hour"
        assert summary.totals.count > 0
        assert summary.totals.count == summary.totals.succeeded + summary.totals.failed
        assert summary.totals.analysis_ms is not None and summary.totals.analysis_ms.count > 0
        assert len(summary.buckets) >= 24
        assert sum(b.stats.count for b in summary.buckets) == summary.totals.count
        assert set(summary.by_media_kind) >= {"Image", "Video", "Audio"}
        assert summary.by_media_kind["Audio"].count > 0
        assert summary.by_model and summary.by_purpose and summary.by_stage

    def test_summary_filters(self, admin: KeyframeClient, audio_analysis: AudioAnalysisResponse) -> None:
        now = datetime.now(timezone.utc)
        summary = admin.analytics.summary(from_utc=now - timedelta(minutes=30), to_utc=now + timedelta(minutes=1),
                                          bucket="minute", media_kind=MediaKind.AUDIO)
        assert summary.bucket == "minute" and summary.media_kind == MediaKind.AUDIO
        assert summary.totals.count > 0
        assert summary.by_media_kind["Image"].count == 0
        expect_api_error(400, lambda: admin.analytics.summary(bucket="fortnight"))

    def test_list_by_media_kind(self, admin: KeyframeClient, audio_analysis: AudioAnalysisResponse) -> None:
        page = admin.analytics.list_analyses(media_kind="Audio", success=True, max_results=5)
        assert page.items and page.total_count > 0
        assert all(isinstance(a, AnalysisRecord) for a in page.items)
        assert all(a.media_kind == MediaKind.AUDIO and a.success and a.calls is None for a in page.items)
        assert any(a.id == audio_analysis.id for a in admin.analytics.list_analyses(media_kind="Audio", max_results=100).items)

    def test_get_with_calls(self, admin: KeyframeClient, audio_analysis: AudioAnalysisResponse) -> None:
        record = admin.analytics.get_analysis(audio_analysis.id)
        assert record.id == audio_analysis.id
        assert record.media_kind == MediaKind.AUDIO and record.job_id and record.job_id.startswith("kfj_")
        assert record.request_id == audio_analysis.request_id
        assert record.transcription_endpoint and record.stages
        assert record.calls, "no model calls"
        assert all(c.id.startswith("kfc_") and c.analysis_id == record.id for c in record.calls)
        assert any(c.purpose == ModelCallPurpose.TRANSCRIBE for c in record.calls)
        assert any(c.generation_ms is not None for c in record.calls if c.purpose == ModelCallPurpose.SUMMARIZE)
        expect_api_error(404, lambda: admin.analytics.get_analysis("kfa_doesnotexist"))

    def test_delete_one(self, admin: KeyframeClient, user: KeyframeClient, media: Media) -> None:
        media.require()
        result = user.analyze_image_file(media.image, {"Functions": {"Summarize": {"Enable": True}}})
        record = admin.analytics.get_analysis(result.id)
        assert record.media_kind == MediaKind.IMAGE and record.job_id is None
        assert admin.analytics.delete_analysis(result.id).deleted_count == 1
        expect_api_error(404, lambda: admin.analytics.get_analysis(result.id))
        expect_api_error(404, lambda: admin.analytics.delete_analysis(result.id))

    def test_delete_older_than(self, admin: KeyframeClient) -> None:
        result = admin.analytics.delete_analyses(older_than_utc=datetime(2000, 1, 1, tzinfo=timezone.utc))
        assert isinstance(result.deleted_count, int) and result.deleted_count == 0

    def test_non_admin_403(self, user: KeyframeClient) -> None:
        expect_api_error(403, user.analytics.summary)
        expect_api_error(403, user.analytics.list_analyses)
        expect_api_error(403, lambda: user.analytics.get_analysis("kfa_doesnotexist"))
        expect_api_error(403, lambda: user.analytics.delete_analysis("kfa_doesnotexist"))
        expect_api_error(403, lambda: user.analytics.delete_analyses(older_than_utc="2000-01-01T00:00:00Z"))


# Endpoints


class TestEndpoints:
    def test_list(self, user: KeyframeClient) -> None:
        endpoints = user.endpoints.list()
        mock = next((e for e in endpoints if e.name == "mock"), None)
        assert mock is not None
        assert mock.provider == ProviderType.MOCK
        assert mock.is_default
        assert any(e.kind == EndpointKind.TRANSCRIPTION for e in endpoints)
        assert all(e.supports_transcription for e in endpoints if e.kind == EndpointKind.TRANSCRIPTION)
        assert mock.supports_audio_input
        assert any(e.name == "mock-listen" and e.supports_audio_input for e in endpoints), "mock-listen missing or without SupportsAudioInput"
        assert not mock.extra, f"unmapped endpoint fields: {sorted(mock.extra)}"

    def test_test_admin(self, admin: KeyframeClient) -> None:
        result = admin.endpoints.test("mock")
        assert result.success, result.error
        assert result.name == "mock"

    def test_unknown_404(self, admin: KeyframeClient) -> None:
        expect_api_error(404, lambda: admin.endpoints.test("no-such-endpoint"))


# Settings


class TestSettings:
    def test_read_masks_secrets(self, admin: KeyframeClient) -> None:
        response = admin.settings.get()
        s = response.settings
        assert s.auth.admin_api_key.startswith("****")
        assert s.auth.admin_api_key != ADMIN_KEY
        assert all(k.key.startswith("****") for k in s.auth.access_keys)
        assert all(e.api_key is None or e.api_key.startswith("****") for e in s.inference.endpoints)
        assert any(f.path == "Auth.AdminApiKey" and f.secret for f in response.fields)
        assert any(f.apply_mode == "Restart" for f in response.fields) and any(f.apply_mode == "Live" for f in response.fields)

    def test_live_write_round_trips_masked_secrets(self, admin: KeyframeClient, user: KeyframeClient) -> None:
        original = admin.settings.get()
        original_days = original.settings.request_history.retention_days
        masked_admin = original.settings.auth.admin_api_key
        changed = original_days - 1 if original_days >= 3650 else original_days + 1
        edited = copy.deepcopy(original.settings)
        edited.request_history.retention_days = changed
        saved = admin.settings.update(edited)
        try:
            assert saved.settings.request_history.retention_days == changed
            assert saved.settings.auth.admin_api_key == masked_admin
            assert admin.whoami.get().is_admin, "admin key stopped working"
            assert user.whoami.get().is_authenticated, "access key stopped working"
            assert admin.settings.get().settings.request_history.retention_days == changed
        finally:
            restore = admin.settings.get().settings
            restore.request_history.retention_days = original_days
            assert admin.settings.update(restore).settings.request_history.retention_days == original_days
        bad = copy.deepcopy(original.settings)
        bad.auth.access_keys.append(AccessKeySettings(name="sdk-harness-invalid", key="****nope", active=True))
        expect_api_error(400, lambda: admin.settings.update(bad))

    def test_analytics_and_segment_settings(self, admin: KeyframeClient) -> None:
        s = admin.settings.get().settings
        assert isinstance(s.analytics.enable, bool)
        assert 1 <= s.analytics.retention_days <= 3650
        assert 1000 <= s.media.max_segment_ms <= 14400000
        assert s.media.max_audio_duration_ms > 0
        assert not s.analytics.extra and not s.media.extra
        assert not s.inference.extra and all(not e.extra for e in s.inference.endpoints)
        assert s.inference.default_listening == "mock-listen"
        assert s.media.listen_format == ListenFormat.MP3
        assert 8000 <= s.media.listen_sample_rate <= 48000
        assert all(e.max_inline_media_bytes >= 1048576 for e in s.inference.endpoints)
        raw = json.loads(admin.send_raw("GET", "/v1.0/settings").body)["Settings"]
        assert "Analytics" in raw and "MaxSegmentMs" in raw["Media"]

    def test_round_trip_is_lossless(self, admin: KeyframeClient) -> None:
        raw = json.loads(admin.send_raw("GET", "/v1.0/settings").body)["Settings"]
        model = KeyframeSettings.from_dict(raw)
        assert model.to_dict() == raw


# Request history


class TestRequestHistory:
    def test_list_and_filter(self, user: KeyframeClient) -> None:
        user.whoami.get()
        page = user.request_history.list(path_contains="/v1.0/whoami", page_size=10)
        assert page.items
        assert all("/v1.0/whoami" in e.path for e in page.items)
        assert all(e.request_body is None and e.response_body is None for e in page.items)
        failures = user.request_history.list(status_code=401, page_size=5)
        assert failures.items and all(e.status_code == 401 and not e.success for e in failures.items)

    def test_summary(self, user: KeyframeClient) -> None:
        summary = user.request_history.summary(bucket_minutes=30)
        assert summary.bucket_minutes == 30
        assert len(summary.buckets) >= 48
        assert summary.total_count > 0 and summary.total_count == summary.total_success + summary.total_failure

    def test_read_redacts_credentials(self, admin: KeyframeClient, user: KeyframeClient) -> None:
        admin.settings.get()
        page = user.request_history.list(path_contains="/v1.0/settings", method="GET", status_code=200, page_size=1)
        assert page.items
        entry = user.request_history.get(page.items[0].id)
        assert entry.id == page.items[0].id
        assert entry.request_headers
        assert ADMIN_KEY not in json.dumps(entry.request_headers)
        assert f'"{ADMIN_KEY}"' not in (entry.response_body or "")
        expect_api_error(404, lambda: user.request_history.get("req_doesnotexist"))

    def test_delete_admin(self, admin: KeyframeClient) -> None:
        admin.send_raw("GET", "/openapi.json")
        time.sleep(0.2)
        page = admin.request_history.list(path_contains="/openapi.json", page_size=1)
        if not page.items:
            pytest.skip("no /openapi.json entry to delete")
        entry_id = page.items[0].id
        admin.request_history.delete(entry_id)
        expect_api_error(404, lambda: admin.request_history.get(entry_id))
        expect_api_error(404, lambda: admin.request_history.delete(entry_id))

    def test_bulk_delete_no_match(self, admin: KeyframeClient) -> None:
        result = admin.request_history.delete_many(path_contains="/no-such-path-" + uuid.uuid4().hex)
        assert result.deleted_count == 0
