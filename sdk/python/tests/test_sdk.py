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
    MediaKind,
    MediaSource,
    ModelCallPurpose,
    ModelSelection,
    ProbeRequest,
    ProviderType,
    SceneDetectionSpec,
    ScenesSpec,
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


# Endpoints


class TestEndpoints:
    def test_list(self, user: KeyframeClient) -> None:
        endpoints = user.endpoints.list()
        mock = next((e for e in endpoints if e.name == "mock"), None)
        assert mock is not None
        assert mock.provider == ProviderType.MOCK
        assert mock.is_default
        assert any(e.kind == EndpointKind.TRANSCRIPTION for e in endpoints)

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
