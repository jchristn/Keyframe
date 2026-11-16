import { describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AnalyticsView from './AnalyticsView';
import AnalyzeAudioView, { buildAudioRequest } from './AnalyzeAudioView';
import AnalyzeImageView from './AnalyzeImageView';
import AnalyzeVideoView from './AnalyzeVideoView';
import ApiExplorerView from './ApiExplorerView';
import RequestHistoryView, { REQUEST_HISTORY_CHART_HEIGHT } from './RequestHistoryView';
import JobDetailView from './JobDetailView';
import LoginView from './LoginView';
import SettingsView from './SettingsView';
import AppearanceMenu from '../components/AppearancePicker';
import { DEFAULT_CHART_HEIGHT } from '../components/ActivityChart';
import ApiClient, { ApiError } from '../utils/api';
import { PALETTES } from '../theme/palettes';
import { makeAuth, renderWithProviders } from '../test/render';
import { ENDPOINTS, OPENAPI, analysisDetail, analysisRecord, audioJob, audioResult, makeSummary } from '../test/fixtures';

const page = (items) => ({ items, pageNumber: 1, pageSize: 25, totalCount: items.length, totalPages: 1 });

describe('ApiClient additions', () => {
  it('sends audio jobs, analytics queries, and segment downloads to the contract routes', async () => {
    const ok = (body, headers = { 'content-type': 'application/json' }) =>
      Promise.resolve(new Response(typeof body === 'string' ? body : JSON.stringify(body), { status: 200, headers }));
    const fetchImpl = vi
      .fn()
      .mockImplementationOnce(() => ok({ Id: 'kfj_1', Status: 'Queued' }))
      .mockImplementationOnce(() => ok(makeSummary()))
      .mockImplementationOnce(() => ok({ Items: [analysisRecord(0)], PageNumber: 1, PageSize: 10, TotalCount: 31, TotalPages: 4 }))
      .mockImplementationOnce(() => ok('MP4DATA', { 'content-type': 'video/mp4', 'content-disposition': 'attachment; filename="segment-1000-2000.mp4"' }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    await client.analyzeAudio({ Audio: { Url: 'https://x/a.mp3' } });
    expect(fetchImpl.mock.calls[0][0]).toBe('http://127.0.0.1:8700/v1.0/audio');
    await client.getAnalyticsSummary({ fromUtc: '2026-11-15T00:00:00.000Z', mediaKind: 'Audio', endpoint: '' });
    expect(fetchImpl.mock.calls[1][0]).toBe('http://127.0.0.1:8700/v1.0/analytics/summary?fromUtc=2026-11-15T00%3A00%3A00.000Z&mediaKind=Audio');
    const list = await client.listAnalyses({ pageNumber: 3, pageSize: 10, success: 'false' });
    expect(fetchImpl.mock.calls[2][0]).toBe('http://127.0.0.1:8700/v1.0/analytics/analyses?success=false&maxResults=10&skip=20');
    expect(list.totalCount).toBe(31);
    expect(list.pageNumber).toBe(3);
    const seg = await client.downloadSegment({ Source: { Url: 'https://x/v.mp4' }, StartMs: 1000, EndMs: 2000, Output: 'Video', Format: 'mp4' });
    const [url, init] = fetchImpl.mock.calls[3];
    expect(url).toBe('http://127.0.0.1:8700/v1.0/segment');
    expect(JSON.parse(init.body)).toEqual({ ResponseFormat: 'Binary', Source: { Url: 'https://x/v.mp4' }, StartMs: 1000, EndMs: 2000, Output: 'Video', Format: 'mp4' });
    expect(seg.blob.size).toBe(7);
  });
});

describe('AnalyticsView', () => {
  const client = (over = {}) => ({
    getAnalyticsSummary: vi.fn().mockResolvedValue(makeSummary()),
    listAnalyses: vi.fn().mockResolvedValue(page([analysisRecord(0), analysisRecord(1), analysisRecord(3)])),
    listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS),
    getAnalysis: vi.fn().mockResolvedValue(analysisDetail('kfa_1001')),
    deleteAnalysis: vi.fn().mockResolvedValue({}),
    ...over
  });

  it('renders KPIs, breakdowns, and charts and passes filters to the server', async () => {
    const api = client();
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/analytics?range=7d&mediaKind=Audio' });
    expect(await screen.findByText('Analyses over time')).toBeInTheDocument();
    expect(api.getAnalyticsSummary).toHaveBeenCalledWith(expect.objectContaining({ mediaKind: 'Audio', fromUtc: expect.any(String), toUtc: expect.any(String) }));
    const q = api.getAnalyticsSummary.mock.calls[0][0];
    expect(Date.parse(q.toUtc) - Date.parse(q.fromUtc)).toBe(7 * 86400000);
    expect(screen.getByText('By model')).toBeInTheDocument();
    expect(screen.getByText('gpt-oss:20b')).toBeInTheDocument();
    expect(screen.getByText('By stage')).toBeInTheDocument();
    expect(screen.getByText('Scene detection')).toBeInTheDocument();
    expect(screen.getByText('By purpose')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Analyses over time/ })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Analysis time percentiles/ })).toBeInTheDocument();
    expect(screen.getAllByText('Time to first token').length).toBeGreaterThan(0);
    expect(api.listAnalyses).toHaveBeenCalledWith(expect.objectContaining({ mediaKind: 'Audio', pageNumber: 1, pageSize: 25 }));
  });

  it('opens the detail drawer with per-call timing and deletes after confirmation', async () => {
    const user = userEvent.setup();
    const api = client();
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }) });
    const table = (await screen.findAllByRole('table')).find((tb) => within(tb).queryByText('Started'));
    const rows = within(table).getAllByRole('row');
    await user.click(rows[2]);
    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Analysis detail')).toBeInTheDocument();
    await waitFor(() => expect(api.getAnalysis).toHaveBeenCalledWith('kfa_1001'));
    expect(await within(drawer).findByRole('img', { name: /Telemetry waterfall with 4 stages and 5 model calls/ })).toBeInTheDocument();
    expect(within(drawer).getAllByText('Scene summary').length).toBeGreaterThan(0);
    expect(within(drawer).getByText(/ollama-gpt-oss-20b · gpt-oss:20b/, { selector: 'dd' })).toBeInTheDocument();
    await user.click(within(drawer).getByRole('button', { name: 'Delete' }));
    const confirm = screen.getAllByRole('dialog').pop();
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(api.deleteAnalysis).toHaveBeenCalledWith('kfa_1001'));
  });

  it('explains when the server has no analytics routes', async () => {
    const api = client({ getAnalyticsSummary: vi.fn().mockRejectedValue(new ApiError({ status: 404, message: 'Not found' })) });
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }) });
    expect(await screen.findByText('Analytics are not available on this server')).toBeInTheDocument();
  });

  it('shows the empty state when no analyses ran in range', async () => {
    const empty = makeSummary();
    empty.Totals = { ...empty.Totals, Count: 0 };
    const api = client({ getAnalyticsSummary: vi.fn().mockResolvedValue(empty), listAnalyses: vi.fn().mockResolvedValue(page([])) });
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }) });
    expect(await screen.findByText('No analyses in this range')).toBeInTheDocument();
  });

  it('asks access keys for the admin key and never calls the server', () => {
    const api = client();
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ isAdmin: false, apiClient: api }) });
    expect(screen.getByText('Analytics require the admin key')).toBeInTheDocument();
    expect(api.getAnalyticsSummary).not.toHaveBeenCalled();
  });
});

describe('AnalyzeAudioView', () => {
  it('builds the contract request: transcription on, summary on, extract off by default', () => {
    const body = buildAudioRequest({
      media: { mode: 'url', url: ' https://x/talk.mp3 ', file: null },
      context: [],
      transcriptionModel: { Endpoint: 'whisper', Name: '' },
      textModel: { Endpoint: '', Name: '' },
      transcribe: { Enable: true, Language: 'en', Prompt: '' },
      summarize: { Enable: true, Key: 'Summary', MaxWords: 150, Style: 'Paragraph', Instructions: '' },
      extractEnable: false,
      mode: 'Combined',
      fields: []
    });
    expect(body.Audio).toEqual({ Url: 'https://x/talk.mp3' });
    expect(body.TranscriptionModel).toEqual({ Endpoint: 'whisper' });
    expect(body.TextModel).toBeUndefined();
    expect(body.Functions.Transcribe).toEqual({ Enable: true, Language: 'en' });
    expect(body.Functions.Summarize).toMatchObject({ Enable: true, MaxWords: 150, Style: 'Paragraph' });
    expect(body.Functions.Extract).toEqual({ Enable: false, Mode: 'Combined', Fields: [] });
  });

  it('submits, polls the job, and shows the transcript when it finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      const api = {
        listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS),
        analyzeAudio: vi.fn().mockResolvedValue({ status: 202, data: audioJob('Queued') }),
        getJob: vi.fn().mockResolvedValueOnce(audioJob('Running')).mockResolvedValue(audioJob('Succeeded')),
        getJobResult: vi.fn().mockResolvedValue(audioResult())
      };
      renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api }) });
      await user.click(screen.getByRole('tab', { name: 'URL' }));
      await user.type(screen.getByLabelText('Audio or video URL'), 'https://media.example.com/talk.mp3');
      await user.click(screen.getByRole('button', { name: 'Submit audio job' }));
      await waitFor(() => expect(api.analyzeAudio).toHaveBeenCalled());
      const sent = api.analyzeAudio.mock.calls[0][0];
      expect(sent.Audio).toEqual({ Url: 'https://media.example.com/talk.mp3' });
      expect(sent.Functions.Transcribe.Enable).toBe(true);
      expect(await screen.findByText('kfj_900')).toBeInTheDocument();
      await act(async () => {
        vi.advanceTimersByTime(1600);
      });
      await act(async () => {
        vi.advanceTimersByTime(1600);
      });
      await waitFor(() => expect(api.getJobResult).toHaveBeenCalledWith('kfj_900'));
      expect(await screen.findByText(/audio analysis ships before the analytics page/)).toBeInTheDocument();
      expect(screen.getAllByRole('button', { name: 'Download audio' }).length).toBe(3);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('AnalyzeVideoView', () => {
  it('stays on the page like image and audio: polls the job and shows scenes when it finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      const videoJob = (status) => audioJob(status, { Id: 'kfj_901', MediaKind: 'Video', SourceUrl: 'https://media.example.com/talk.mp4' });
      const videoResult = {
        ...audioResult(),
        MediaKind: 'Video',
        Results: { Summary: 'A keynote with slides about the product roadmap.' },
        Scenes: [
          { Index: 0, StartMs: 0, EndMs: 4000, Results: { Summary: 'Title slide.' }, Fields: [], Keyframes: [] },
          { Index: 1, StartMs: 4000, EndMs: 9000, Results: { Summary: 'Speaker at a podium.' }, Fields: [], Keyframes: [] }
        ]
      };
      const api = {
        listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS),
        analyzeVideo: vi.fn().mockResolvedValue({ status: 202, data: videoJob('Queued') }),
        getJob: vi.fn().mockResolvedValueOnce(videoJob('Running')).mockResolvedValue(videoJob('Succeeded')),
        getJobResult: vi.fn().mockResolvedValue(videoResult)
      };
      renderWithProviders(<AnalyzeVideoView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/analyze/video' });
      expect(screen.getByText('No video job yet')).toBeInTheDocument();
      await user.type(screen.getByLabelText('Video URL'), 'https://media.example.com/talk.mp4');
      await user.click(screen.getByRole('button', { name: 'Submit job' }));
      await waitFor(() => expect(api.analyzeVideo).toHaveBeenCalled());
      expect(await screen.findByText('kfj_901')).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Open job' })).toHaveAttribute('href', '/dashboard/jobs/kfj_901');
      await act(async () => {
        vi.advanceTimersByTime(1600);
      });
      await act(async () => {
        vi.advanceTimersByTime(1600);
      });
      await waitFor(() => expect(api.getJobResult).toHaveBeenCalledWith('kfj_901'));
      expect(await screen.findByRole('tab', { name: /Scenes/ })).toHaveAttribute('aria-selected', 'true');
      expect(await screen.findByText('Title slide.')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('JobDetailView segments', () => {
  it('downloads a transcript segment from the job source URL', async () => {
    const user = userEvent.setup();
    const downloadSegment = vi.fn().mockResolvedValue({ blob: new Blob(['x']), filename: 'attachment; filename="segment-4200-9800.mp3"' });
    const api = { getJob: vi.fn().mockResolvedValue(audioJob('Succeeded')), getJobResult: vi.fn().mockResolvedValue(audioResult()), downloadSegment };
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    renderWithProviders(<JobDetailView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/jobs/kfj_900', path: '/dashboard/jobs/:id' });
    await user.click(await screen.findByRole('tab', { name: /Transcript/ }));
    expect(screen.queryByRole('tab', { name: /Scenes/ })).not.toBeInTheDocument();
    const buttons = screen.getAllByRole('button', { name: 'Download audio' });
    await user.click(buttons[1]);
    await waitFor(() => expect(downloadSegment).toHaveBeenCalledWith({ Source: { Url: 'https://media.example.com/talk.mp3' }, StartMs: 4200, EndMs: 9800, Output: 'Audio', Format: 'mp3' }));
  });

  it('asks for the original file when the media was uploaded', async () => {
    const user = userEvent.setup();
    const api = { getJob: vi.fn().mockResolvedValue(audioJob('Succeeded', { SourceUrl: null })), getJobResult: vi.fn().mockResolvedValue(audioResult()), downloadSegment: vi.fn() };
    renderWithProviders(<JobDetailView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/jobs/kfj_900', path: '/dashboard/jobs/:id' });
    await user.click(await screen.findByRole('tab', { name: /Transcript/ }));
    expect(screen.getByText('The source media was uploaded')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Download audio' })[0]).toBeDisabled();
    expect(screen.getByText('Choose original file')).toBeInTheDocument();
  });
});

describe('ApiExplorerView', () => {
  it('picks an operation from the full-width dropdown and lists the new routes', async () => {
    const user = userEvent.setup();
    const api = { getOpenApiSpec: vi.fn().mockResolvedValue(OPENAPI), url: (p) => 'http://127.0.0.1:8700' + p };
    renderWithProviders(<ApiExplorerView />, { auth: makeAuth({ apiClient: api }) });
    const trigger = await screen.findByRole('button', { name: 'Choose an API operation' });
    await user.click(trigger);
    const list = screen.getByRole('listbox');
    expect(within(list).getByText('/v1.0/segment')).toBeInTheDocument();
    expect(within(list).getByText('/v1.0/audio')).toBeInTheDocument();
    await user.type(screen.getByRole('combobox'), 'jobs/{id}');
    const options = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(options).toHaveLength(2);
    await user.keyboard('{ArrowDown}{Enter}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(screen.getByText('Path parameters')).toBeInTheDocument();
    expect(screen.getAllByText(/\/v1\.0\/jobs\//).length).toBeGreaterThan(0);
  });
});

describe('RequestHistoryView', () => {
  it('draws the activity chart at twice the default height', async () => {
    const api = {
      getRequestHistory: vi.fn().mockResolvedValue(page([])),
      getRequestHistorySummary: vi.fn().mockResolvedValue({ TotalCount: 0, TotalSuccess: 0, TotalFailure: 0, AverageDurationMs: 0, Buckets: [] })
    };
    const { container } = renderWithProviders(<RequestHistoryView />, { auth: makeAuth({ apiClient: api }) });
    await screen.findByText('Request activity');
    expect(REQUEST_HISTORY_CHART_HEIGHT).toBe(DEFAULT_CHART_HEIGHT * 2);
    expect(container.querySelector('.chart-wrap svg').getAttribute('height')).toBe(String(DEFAULT_CHART_HEIGHT * 2));
  });
});

describe('AnalyzeImageView', () => {
  it('stacks the form and results in one column', async () => {
    const api = { listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS) };
    const { container } = renderWithProviders(<AnalyzeImageView />, { auth: makeAuth({ apiClient: api }) });
    await screen.findByRole('option', { name: /ollama-gemma3-4b/ });
    const layout = container.querySelector('.analyze-layout');
    expect(layout.querySelector('form')).toBeTruthy();
    expect(layout.querySelector('.analyze-result')).toBeTruthy();
    expect(container.querySelector('.two-col')).toBeNull();
    expect(screen.queryByRole('option', { name: /mock/i })).not.toBeInTheDocument();
  });
});

describe('Appearance', () => {
  it('offers every palette and mode, applies the choice to <html>, and persists it', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AppearanceMenu />);
    await user.click(screen.getByRole('button', { name: /Appearance/ }));
    const palettes = screen.getAllByRole('radio').filter((r) => r.classList.contains('palette-swatch'));
    expect(palettes).toHaveLength(PALETTES.length);
    await user.click(screen.getByRole('radio', { name: /Nocturne/ }));
    await user.click(screen.getByRole('radio', { name: /Dark/ }));
    expect(document.documentElement.getAttribute('data-palette')).toBe('nocturne');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(JSON.parse(localStorage.getItem('keyframe.appearance'))).toMatchObject({ palette: 'nocturne', mode: 'dark' });
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('is available on Settings for access keys too', () => {
    renderWithProviders(<SettingsView />, { auth: makeAuth({ isAdmin: false, apiClient: { getSettings: vi.fn() } }) });
    expect(screen.getByRole('radiogroup', { name: 'Palette' })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'Mode' })).toBeInTheDocument();
  });
});

describe('Language selection', () => {
  it('lists autonyms on the login page and switches the interface language', async () => {
    const user = userEvent.setup();
    renderWithProviders(<LoginView />, { auth: makeAuth({ isAuthenticated: false, principal: null }) });
    const select = screen.getByRole('combobox', { name: 'Language' });
    const names = within(select).getAllByRole('option').map((o) => o.textContent);
    for (const n of ['English', 'Español', 'Português', 'Français', 'Italiano', 'Deutsch', '中文', 'العربية', 'Русский', 'Bahasa Melayu', 'हिन्दी', '日本語']) expect(names).toContain(n);
    await user.selectOptions(select, 'de');
    await waitFor(() => expect(document.documentElement.lang).toBe('de'));
    expect(screen.queryByRole('button', { name: 'Connect' })).not.toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox'), 'en');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Connect' })).toBeInTheDocument());
  });
});
