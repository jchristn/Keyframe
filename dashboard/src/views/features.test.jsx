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
import HomeView from './HomeView';
import LoginView from './LoginView';
import SettingsView from './SettingsView';
import AppearanceMenu from '../components/AppearancePicker';
import { DEFAULT_CHART_HEIGHT } from '../components/ActivityChart';
import ApiClient, { ApiError } from '../utils/api';
import { PALETTES } from '../theme/palettes';
import { makeAuth, renderWithProviders } from '../test/render';
import { ENDPOINTS, OPENAPI, analysisDetail, analysisRecord, audioJob, audioResult, fallbackListening, listening, listeningResult, makeSummary, settingsResponse } from '../test/fixtures';

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

  it('draws the four time charts at double height, each full width in its own panel', async () => {
    const api = client();
    const { container } = renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }) });
    await screen.findByText('Analyses over time');
    const height = (name) => screen.getByRole('img', { name }).getAttribute('height');
    expect(height(/Analyses over time/)).toBe('440');
    expect(height(/Analysis time percentiles/)).toBe('520');
    expect(height('Tokens over time')).toBe('400');
    expect(height('Success rate over time')).toBe('400');
    expect(container.querySelector('.chart-pair')).toBeNull();
    for (const svg of container.querySelectorAll('.chart-panel svg')) expect(svg.closest('.chart-panel').parentElement.classList.contains('chart-panel')).toBe(false);
  });

  it('opens analysis detail as a 95% modal with per-call timing and deletes after confirmation', async () => {
    const user = userEvent.setup();
    const api = client();
    renderWithProviders(<AnalyticsView />, { auth: makeAuth({ apiClient: api }) });
    const table = (await screen.findAllByRole('table')).find((tb) => within(tb).queryByText('Started'));
    const rows = within(table).getAllByRole('row');
    await user.click(rows[2]);
    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Analysis detail')).toBeInTheDocument();
    expect(drawer.classList.contains('modal-full')).toBe(true);
    expect(drawer.classList.contains('drawer')).toBe(false);
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
      expect(sent.Listen).toEqual({ Enable: true, SampleClips: 4, ClipSeconds: 30 });
      expect(sent.Functions.Measure).toEqual({ Enable: true });
      expect(sent.Functions.Sections).toEqual({ Enable: false });
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

describe('buildAudioRequest', () => {
  const base = {
    media: { mode: 'url', url: 'https://x/set.flac', file: null },
    context: [],
    transcriptionModel: { Endpoint: '', Name: '' },
    textModel: { Endpoint: '', Name: '' },
    transcribe: { Enable: true, Language: '', Prompt: '' },
    summarize: { Enable: true, Key: 'Summary', MaxWords: 80, Style: 'Paragraph', Instructions: '' },
    extractEnable: true,
    mode: 'Combined',
    fields: [{ uid: 'f1', Key: 'recording', Type: 'Enum', Prompt: 'Live or studio?', Options: ['Live', 'Studio'] }]
  };
  const sectionsOn = { Enable: true, SilenceDb: '-40', MinSilenceMs: '1500', MinSectionMs: '20000', MaxSectionMs: '600000', MaxSections: '100' };

  it('listens, measures, and leaves sections off by default', () => {
    const body = buildAudioRequest(base);
    expect(body.Listen).toEqual({ Enable: true, SampleClips: 4, ClipSeconds: 30 });
    expect(body.Functions.Measure).toEqual({ Enable: true });
    expect(body.Functions.Sections).toEqual({ Enable: false });
    expect(body.Functions.Extract.Enable).toBe(true);
  });

  it('sends the listening model and excerpt counts clamped to the server ranges', () => {
    const body = buildAudioRequest({ ...base, listen: { Enable: true, Endpoint: 'llama-listen', Name: ' qwen2.5-omni ', SampleClips: '99', ClipSeconds: '1' } });
    expect(body.Listen.Model).toEqual({ Endpoint: 'llama-listen', Name: 'qwen2.5-omni' });
    expect(body.Listen.SampleClips).toBe(32);
    expect(body.Listen.ClipSeconds).toBe(5);
    expect(buildAudioRequest({ ...base, listen: { Enable: true, Endpoint: '', Name: '', SampleClips: '0', ClipSeconds: '900' } }).Listen).toMatchObject({ SampleClips: 1, ClipSeconds: 300 });
  });

  it('sends only the rendition members the user set, with bitrate for Mp3 only', () => {
    const rendition = (r) => buildAudioRequest({ ...base, rendition: { Format: '', SampleRate: '', Channels: '', BitrateKbps: '', ...r } }).Listen.Rendition;
    expect(rendition({})).toBeUndefined();
    expect(rendition({ Format: 'Mp3', SampleRate: '44100', Channels: '2', BitrateKbps: '128' })).toEqual({ Format: 'Mp3', SampleRate: 44100, Channels: 2, BitrateKbps: 128 });
    expect(rendition({ Format: 'Mp3', BitrateKbps: '999' })).toEqual({ Format: 'Mp3', BitrateKbps: 320 });
    expect(rendition({ Format: 'Mp3', BitrateKbps: '4' })).toEqual({ Format: 'Mp3', BitrateKbps: 16 });
    expect(rendition({ Format: 'Wav', BitrateKbps: '128' })).toEqual({ Format: 'Wav' });
    expect(rendition({ BitrateKbps: '128', Channels: '2' })).toEqual({ Channels: 2 });
    expect(rendition({ SampleRate: '96000' })).toEqual({ SampleRate: 48000 });
  });

  it('sends only Listen.Enable false when listening is off', () => {
    const body = buildAudioRequest({ ...base, listen: { Enable: false, Endpoint: 'llama-listen', Name: 'x', SampleClips: '4', ClipSeconds: '30' }, rendition: { Format: 'Wav', SampleRate: '', Channels: '', BitrateKbps: '' } });
    expect(body.Listen).toEqual({ Enable: false });
  });

  it('turns Measure off when asked', () => {
    expect(buildAudioRequest({ ...base, measure: false }).Functions.Measure).toEqual({ Enable: false });
  });

  it('clamps section detection exactly like the backend and sends per-section functions', () => {
    const body = buildAudioRequest({
      ...base,
      sections: { Enable: true, SilenceDb: '-200', MinSilenceMs: '50', MinSectionMs: '999999', MaxSectionMs: '5', MaxSections: '5000' },
      sectionSummarize: { Enable: true, Key: 'Summary', MaxWords: 40, Style: 'Caption', Instructions: '' },
      sectionExtractEnable: true,
      sectionFields: [{ uid: 's1', Key: 'topic', Type: 'String', Prompt: 'Topic?' }]
    });
    expect(body.Functions.Sections.Detection).toEqual({ SilenceDb: -90, MinSilenceMs: 100, MinSectionMs: 600000, MaxSectionMs: 10000, MaxSections: 1000 });
    expect(body.Functions.Sections.Summarize).toMatchObject({ Enable: true, MaxWords: 40, Style: 'Caption' });
    expect(body.Functions.Sections.Extract).toEqual({ Enable: true, Fields: [{ Key: 'topic', Type: 'String', Prompt: 'Topic?' }] });
    const upper = buildAudioRequest({ ...base, sections: { ...sectionsOn, SilenceDb: '0', MinSilenceMs: '99999', MinSectionMs: '-5', MaxSectionMs: '99999999', MaxSections: '0' } }).Functions.Sections.Detection;
    expect(upper).toEqual({ SilenceDb: -10, MinSilenceMs: 30000, MinSectionMs: 0, MaxSectionMs: 3600000, MaxSections: 1 });
    expect(buildAudioRequest({ ...base, sections: { ...sectionsOn, SilenceDb: '-35.5' } }).Functions.Sections.Detection.SilenceDb).toBe(-35.5);
  });

  it('keeps Summarize and Extract with Listen alone, and drops every model function with neither', () => {
    const listenOnly = buildAudioRequest({ ...base, transcribe: { Enable: false, Language: '', Prompt: '' }, sections: sectionsOn, sectionSummarize: { Enable: true, Key: 'Summary', MaxWords: 40, Style: 'Paragraph', Instructions: '' } });
    expect(listenOnly.Functions.Transcribe).toEqual({ Enable: false });
    expect(listenOnly.Functions.Summarize.Enable).toBe(true);
    expect(listenOnly.Functions.Extract.Enable).toBe(true);
    expect(listenOnly.Functions.Sections.Summarize.Enable).toBe(true);
    const neither = buildAudioRequest({
      ...base,
      transcribe: { Enable: false, Language: '', Prompt: '' },
      listen: { Enable: false, Endpoint: '', Name: '', SampleClips: '4', ClipSeconds: '30' },
      sections: sectionsOn,
      sectionSummarize: { Enable: true, Key: 'Summary', MaxWords: 40, Style: 'Paragraph', Instructions: '' },
      sectionExtractEnable: true,
      sectionFields: [{ uid: 's1', Key: 'topic', Type: 'String', Prompt: 'Topic?' }]
    });
    expect(neither.Functions.Summarize.Enable).toBe(false);
    expect(neither.Functions.Extract).toEqual({ Enable: false, Mode: 'Combined', Fields: [] });
    expect(neither.Functions.Sections.Summarize.Enable).toBe(false);
    expect(neither.Functions.Sections.Extract).toEqual({ Enable: false, Fields: [] });
    expect(neither.Functions.Sections.Enable).toBe(true);
  });
});

describe('AnalyzeAudioView listening form', () => {
  const api = (over = {}) => ({ listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS), getSettings: vi.fn().mockResolvedValue(settingsResponse()), ...over });

  it('enables the field builder when Transcribe or Listen is on, and only then', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api() }) });
    await user.click(screen.getByRole('checkbox', { name: 'Extract typed fields' }));
    const key = () => screen.getAllByLabelText(/^Key/)[0];
    expect(key()).toBeEnabled();
    await user.click(screen.getByRole('checkbox', { name: 'Transcribe the audio track' }));
    expect(key()).toBeEnabled();
    expect(screen.getByRole('checkbox', { name: 'Extract typed fields' })).toBeEnabled();
    expect(screen.getByText(/works from the audio alone/)).toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'Listen to the audio' }));
    expect(key()).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Extract typed fields' })).toBeDisabled();
    expect(screen.getAllByText(/Turn on Transcribe or Listen/).length).toBeGreaterThan(0);
    await user.click(screen.getByRole('checkbox', { name: 'Transcribe the audio track' }));
    expect(key()).toBeEnabled();
  });

  it('lists only audio-capable endpoints with their health and shows the default listening endpoint', async () => {
    renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api() }) });
    const select = await screen.findByLabelText('Listening endpoint');
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(2));
    expect(within(select).getByRole('option', { name: /llama-listen \(OpenAI-compatible\) · Healthy/ })).toBeInTheDocument();
    expect(within(select).queryByRole('option', { name: /ollama/ })).not.toBeInTheDocument();
    expect(await screen.findByText('Listening through llama-listen (voxtral-mini-3b).')).toBeInTheDocument();
    expect(screen.queryByTestId('listen-fallback')).not.toBeInTheDocument();
  });

  it('warns that the analysis falls back to the transcript when the listening endpoint is unhealthy or missing', async () => {
    const sick = ENDPOINTS.map((e) => (e.Name === 'llama-listen' ? { ...e, Healthy: false } : e));
    const { unmount } = renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api({ listEndpoints: vi.fn().mockResolvedValue(sick) }) }) });
    const banner = await screen.findByTestId('listen-fallback');
    expect(banner).toHaveTextContent('This analysis will fall back to the transcript');
    expect(banner).toHaveTextContent('llama-listen is unhealthy right now');
    expect(banner).toHaveTextContent(/llama-server may still be downloading its model/);
    expect(screen.getByRole('checkbox', { name: 'Listen to the audio' })).toBeChecked();
    expect(screen.getByText('No usable listening endpoint; results will come from the transcript.')).toBeInTheDocument();
    unmount();
    const none = ENDPOINTS.filter((e) => e.Name !== 'llama-listen');
    renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api({ listEndpoints: vi.fn().mockResolvedValue(none) }) }) });
    expect(await screen.findByTestId('listen-fallback')).toHaveTextContent('No active endpoint on this server can take audio input');
  });

  it('starts the audio quality controls at the server default and enables bitrate for Mp3 only', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api() }) });
    await user.click(screen.getByText('Audio quality'));
    const format = screen.getByLabelText('Format');
    expect(format).toHaveValue('');
    await waitFor(() => expect(within(format).getAllByRole('option')[0]).toHaveTextContent('Server default (MP3)'));
    expect(screen.getByLabelText('Sample rate')).toHaveValue('');
    expect(screen.getByLabelText('Channels')).toHaveValue('');
    expect(screen.getByLabelText('Bitrate (kbps)')).toBeDisabled();
    await user.selectOptions(format, 'Mp3');
    const bitrate = screen.getByLabelText('Bitrate (kbps)');
    expect(bitrate).toBeEnabled();
    await user.type(bitrate, '500');
    await user.tab();
    expect(bitrate).toHaveValue(320);
    await user.selectOptions(format, 'Flac');
    expect(screen.getByText(/accept only MP3 or WAV/)).toBeInTheDocument();
  });

  it('validates the section range and reports a request with nothing to do', async () => {
    const user = userEvent.setup();
    const analyzeAudio = vi.fn();
    renderWithProviders(<AnalyzeAudioView />, { auth: makeAuth({ apiClient: api({ analyzeAudio }) }) });
    await user.click(screen.getByRole('tab', { name: 'URL' }));
    await user.type(screen.getByLabelText('Audio or video URL'), 'https://media.example.com/set.mp3');
    await user.click(screen.getByRole('checkbox', { name: 'Split the recording at silences and analyze each section' }));
    const max = screen.getByLabelText('Max section (ms)');
    await user.clear(max);
    await user.type(max, '15000');
    await user.click(screen.getByRole('button', { name: 'Submit audio job' }));
    expect(screen.getByText('Max section must be at least min section.')).toBeInTheDocument();
    expect(analyzeAudio).not.toHaveBeenCalled();
    await user.click(screen.getByRole('checkbox', { name: 'Split the recording at silences and analyze each section' }));
    await user.click(screen.getByRole('checkbox', { name: 'Transcribe the audio track' }));
    await user.click(screen.getByRole('checkbox', { name: 'Listen to the audio' }));
    await user.click(screen.getByRole('checkbox', { name: 'Measure loudness, levels, silence, and stereo width' }));
    await user.click(screen.getByRole('button', { name: 'Submit audio job' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Enable at least one of Transcribe, Summarize, Extract, Measure, or Sections.');
    expect(analyzeAudio).not.toHaveBeenCalled();
  });
});

describe('Listening, measurements, and sections results', () => {
  const open = (result, job = audioJob('Succeeded')) => {
    const api = { getJob: vi.fn().mockResolvedValue(job), getJobResult: vi.fn().mockResolvedValue(result), downloadSegment: vi.fn().mockResolvedValue({ blob: new Blob(['x']), filename: null }) };
    renderWithProviders(<JobDetailView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/jobs/kfj_900', path: '/dashboard/jobs/:id' });
    return api;
  };

  it('shows the Listening card with a warning badge (icon and text) when the mode is not Full', async () => {
    open(listeningResult());
    const card = await screen.findByTestId('listening-card');
    const badge = within(card).getByTestId('listening-badge');
    expect(badge).toHaveTextContent('Per-section clips');
    expect(badge.classList.contains('badge-warning')).toBe(true);
    expect(badge.querySelector('svg')).not.toBeNull();
    expect(within(card).getByText('llama-listen')).toBeInTheDocument();
    expect(within(card).getByText('voxtral-mini-3b')).toBeInTheDocument();
    expect(within(card).getByText('MP3, 16 kHz, Mono, 64 kb/s')).toBeInTheDocument();
    expect(within(card).getByText('2:30.0 of 3:02.0 (82%)')).toBeInTheDocument();
    expect(within(card).getByText('3 clips sent')).toBeInTheDocument();
    expect(within(card).getByText('Section 3')).toBeInTheDocument();
  });

  it('uses a success badge for a full listen and explains a fallback with the localized reason', async () => {
    const { unmount } = renderWithProviders(<JobDetailView />, {
      auth: makeAuth({ apiClient: { getJob: vi.fn().mockResolvedValue(audioJob('Succeeded')), getJobResult: vi.fn().mockResolvedValue(listeningResult({ Listening: listening({ Mode: 'Full', Clips: [{ Scope: 'Recording', Index: null, StartMs: 0, EndMs: 182000, SizeBytes: 1456000 }] }) })) } }),
      route: '/dashboard/jobs/kfj_900',
      path: '/dashboard/jobs/:id'
    });
    const badge = await screen.findByTestId('listening-badge');
    expect(badge).toHaveTextContent('Full recording');
    expect(badge.classList.contains('badge-success')).toBe(true);
    expect(screen.getByText('1 clip sent')).toBeInTheDocument();
    unmount();
    open(listeningResult({ Listening: fallbackListening() }));
    const fallback = await screen.findByTestId('listening-badge');
    expect(fallback).toHaveTextContent('Not listened');
    expect(fallback.classList.contains('badge-warning')).toBe(true);
    expect(screen.getByText('The listening endpoint was unhealthy')).toBeInTheDocument();
    expect(screen.getByText(/llama-server may still be loading its model/)).toBeInTheDocument();
    expect(screen.queryByText(/clips sent/)).not.toBeInTheDocument();
  });

  it('shows measurements with units and copy buttons', async () => {
    const user = userEvent.setup();
    open(listeningResult());
    await user.click(await screen.findByRole('tab', { name: 'Measurements' }));
    expect(screen.getByText('Integrated loudness')).toBeInTheDocument();
    expect(screen.getByText('-9.4 LUFS')).toBeInTheDocument();
    expect(screen.getByText('5.1 LU')).toBeInTheDocument();
    expect(screen.getByText('-0.2 dBTP')).toBeInTheDocument();
    expect(screen.getByText('-11.8 dBFS')).toBeInTheDocument();
    expect(screen.getByText('11.5 dB')).toBeInTheDocument();
    expect(screen.getByText('3%')).toBeInTheDocument();
    expect(screen.getByText('Stereo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Integrated loudness' })).toBeInTheDocument();
  });

  it('shows the section timeline and downloads a section as audio', async () => {
    const user = userEvent.setup();
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    const api = open(listeningResult());
    await user.click(await screen.findByRole('tab', { name: /Sections/ }));
    const bar = screen.getByRole('listbox', { name: /Sections of a 3 min recording/ });
    fireEvent.keyDown(bar, { key: 'ArrowRight' });
    expect(screen.getByRole('heading', { name: 'Section 2 of 3' })).toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'Download audio' })[0]);
    await waitFor(() => expect(api.downloadSegment).toHaveBeenCalledWith({ Source: { Url: 'https://media.example.com/talk.mp3' }, StartMs: 60000, EndMs: 120000, Output: 'Audio', Format: 'mp3' }));
  });

  it('shows empty and error states on the new tabs', async () => {
    const user = userEvent.setup();
    const { unmount } = renderWithProviders(<JobDetailView />, {
      auth: makeAuth({ apiClient: { getJob: vi.fn().mockResolvedValue(audioJob('Succeeded')), getJobResult: vi.fn().mockResolvedValue(audioResult()) } }),
      route: '/dashboard/jobs/kfj_900',
      path: '/dashboard/jobs/:id'
    });
    await user.click(await screen.findByRole('tab', { name: 'Measurements' }));
    expect(screen.getByText('Measurements were turned off for this analysis.')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Sections' }));
    expect(screen.getByText('Sections were not requested for this analysis.')).toBeInTheDocument();
    unmount();
    open({ ...audioResult(), Errors: ['Measure failed: ffmpeg exited with code 1.'], Sections: [] });
    await user.click(await screen.findByRole('tab', { name: 'Measurements' }));
    expect(screen.getByText('No measurements')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /Sections/ }));
    expect(screen.getByText('Section detection found no sections.')).toBeInTheDocument();
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

describe('AnalyzeVideoView scene audio', () => {
  it('sends scene audio by default, warns when the vision endpoint lacks audio input, and shows Listening on the result', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      const videoJob = (status) => audioJob(status, { Id: 'kfj_902', MediaKind: 'Video', SourceUrl: 'https://media.example.com/talk.mp4' });
      const videoResult = { ...audioResult(), MediaKind: 'Video', Scenes: [{ Index: 0, StartMs: 0, EndMs: 4000, Results: {}, Fields: [], Keyframes: [] }], Listening: listening({ Mode: 'None', Listened: false, Reason: 'EndpointLacksAudioInput', Endpoint: null, Model: null, HeardMs: 0, Clips: [] }) };
      const api = {
        listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS),
        getSettings: vi.fn().mockResolvedValue(settingsResponse()),
        analyzeVideo: vi.fn().mockResolvedValue({ status: 202, data: videoJob('Queued') }),
        getJob: vi.fn().mockResolvedValue(videoJob('Succeeded')),
        getJobResult: vi.fn().mockResolvedValue(videoResult)
      };
      renderWithProviders(<AnalyzeVideoView />, { auth: makeAuth({ apiClient: api }), route: '/dashboard/analyze/video' });
      expect(screen.getByRole('checkbox', { name: "Send each scene's audio to the vision model" })).toBeChecked();
      expect(await screen.findByTestId('scene-audio-warning')).toHaveTextContent('ollama-gemma3-4b cannot take audio input. Scenes will use frames and transcript only.');
      await user.selectOptions(screen.getByLabelText('Vision endpoint'), 'llama-listen');
      expect(screen.queryByTestId('scene-audio-warning')).not.toBeInTheDocument();
      expect(screen.getByText(/llama-listen can take audio input/)).toBeInTheDocument();
      await user.click(screen.getAllByText('Audio quality')[0]);
      await user.selectOptions(screen.getByLabelText('Channels'), '2');
      await user.type(screen.getByLabelText('Video URL'), 'https://media.example.com/talk.mp4');
      await user.click(screen.getByRole('button', { name: 'Submit job' }));
      await waitFor(() => expect(api.analyzeVideo).toHaveBeenCalled());
      expect(api.analyzeVideo.mock.calls[0][0].Functions.Scenes.Audio).toEqual({ Enable: true, Rendition: { Channels: 2 } });
      await act(async () => {
        vi.advanceTimersByTime(1600);
      });
      await waitFor(() => expect(api.getJobResult).toHaveBeenCalledWith('kfj_902'));
      const card = await screen.findByTestId('listening-card');
      expect(within(card).getByTestId('listening-badge')).toHaveTextContent('Not listened');
      expect(within(card).getByText('The endpoint cannot take audio input')).toBeInTheDocument();
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

  it('passes the signed-in key through by default, or sends a typed bearer token instead', async () => {
    const user = userEvent.setup();
    const executeRaw = vi.fn().mockResolvedValue(new Response('{"Status":"Healthy"}', { status: 200, headers: { 'content-type': 'application/json' } }));
    const api = { getOpenApiSpec: vi.fn().mockResolvedValue(OPENAPI), url: (p) => 'http://127.0.0.1:8700' + p, executeRaw };
    renderWithProviders(<ApiExplorerView />, { auth: makeAuth({ apiClient: api }) });
    await user.click(await screen.findByRole('button', { name: 'Choose an API operation' }));
    await user.type(screen.getByRole('combobox'), 'health');
    await user.keyboard('{ArrowDown}{Enter}');

    const useSession = screen.getByRole('checkbox', { name: 'Use my bearer token' });
    expect(useSession).toBeChecked();
    expect(screen.queryByLabelText('API key or bearer token')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Execute' }));
    await waitFor(() => expect(executeRaw).toHaveBeenCalledTimes(1));
    expect(executeRaw.mock.calls[0][0].auth).toBe(true);
    expect(executeRaw.mock.calls[0][0].headers.Authorization).toBeUndefined();

    await user.click(useSession);
    await user.type(screen.getByLabelText('API key or bearer token'), 'other-key');
    await user.click(screen.getByRole('button', { name: 'Execute' }));
    await waitFor(() => expect(executeRaw).toHaveBeenCalledTimes(2));
    expect(executeRaw.mock.calls[1][0].auth).toBe(false);
    expect(executeRaw.mock.calls[1][0].headers.Authorization).toBe('Bearer other-key');
  });
});

describe('RequestHistoryView', () => {
  it('draws the activity chart at 400px, twice the original 200px', async () => {
    const api = {
      getRequestHistory: vi.fn().mockResolvedValue(page([])),
      getRequestHistorySummary: vi.fn().mockResolvedValue({ TotalCount: 0, TotalSuccess: 0, TotalFailure: 0, AverageDurationMs: 0, Buckets: [] })
    };
    const { container } = renderWithProviders(<RequestHistoryView />, { auth: makeAuth({ apiClient: api }) });
    await screen.findByText('Request activity');
    expect(REQUEST_HISTORY_CHART_HEIGHT).toBe(400);
    expect(container.querySelector('.chart-wrap svg').getAttribute('height')).toBe('400');
  });
});

describe('HomeView', () => {
  it('draws the request activity chart at the same 400px height as Request History', async () => {
    const api = {
      listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS),
      listJobs: vi.fn().mockResolvedValue(page([])),
      getRequestHistorySummary: vi.fn().mockResolvedValue({ TotalCount: 0, TotalSuccess: 0, TotalFailure: 0, AverageDurationMs: 0, Buckets: [] })
    };
    const { container } = renderWithProviders(<HomeView />, { auth: makeAuth({ apiClient: api }) });
    await waitFor(() => expect(container.querySelector('.chart-wrap svg')).not.toBeNull());
    expect(DEFAULT_CHART_HEIGHT).toBe(400);
    expect(container.querySelector('.chart-wrap svg').getAttribute('height')).toBe('400');
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
