import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import LoginView from './LoginView';
import JobsView from './JobsView';
import SettingsView from './SettingsView';
import ServerInfoView from './ServerInfoView';
import EndpointsView from './EndpointsView';
import { ENDPOINTS } from '../test/fixtures';
import { ApiError } from '../utils/api';
import { makeAuth, renderWithProviders } from '../test/render';

const job = (over) => ({
  Id: 'kfj_1',
  Status: 'Succeeded',
  MediaKind: 'Video',
  PrincipalName: 'default',
  SourceUrl: null,
  SizeBytes: 1024,
  DurationMs: 12000,
  Progress: { Stage: 'Done', Completed: 1, Total: 1, Percent: 100 },
  Error: null,
  CreatedUtc: '2026-11-15T18:00:00Z',
  StartedUtc: '2026-11-15T18:00:01Z',
  CompletedUtc: '2026-11-15T18:00:05Z',
  ResultUrl: '/v1.0/jobs/kfj_1/result',
  ...over
});

describe('LoginView', () => {
  it('defaults the server URL and submits the trimmed key with remember on', async () => {
    const user = userEvent.setup();
    const login = vi.fn().mockResolvedValue({});
    renderWithProviders(<LoginView />, { auth: makeAuth({ isAuthenticated: false, principal: null, login }) });
    expect(screen.getByLabelText('Server URL')).toHaveValue('http://127.0.0.1:8700');
    await user.type(screen.getByLabelText('API key'), '  default  ');
    await user.click(screen.getByRole('button', { name: 'Connect' }));
    expect(login).toHaveBeenCalledWith('http://127.0.0.1:8700', 'default', true);
  });

  it('explains a rejected key and an unreachable server', async () => {
    const user = userEvent.setup();
    const login = vi
      .fn()
      .mockRejectedValueOnce(new ApiError({ status: 401, message: 'The key is not valid.' }))
      .mockRejectedValueOnce(new ApiError({ network: true }));
    renderWithProviders(<LoginView />, { auth: makeAuth({ isAuthenticated: false, principal: null, login }) });
    await user.type(screen.getByLabelText('API key'), 'nope');
    await user.click(screen.getByRole('button', { name: 'Connect' }));
    expect(await screen.findByText(/The server rejected this key/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Connect' }));
    expect(await screen.findByText(/Could not reach http:\/\/127.0.0.1:8700/)).toBeInTheDocument();
  });

  it('rejects a URL that is not http or https without calling the server', async () => {
    const user = userEvent.setup();
    const login = vi.fn();
    renderWithProviders(<LoginView />, { auth: makeAuth({ isAuthenticated: false, principal: null, login }) });
    await user.clear(screen.getByLabelText('Server URL'));
    await user.type(screen.getByLabelText('Server URL'), 'ftp://example');
    await user.click(screen.getByRole('button', { name: 'Connect' }));
    expect(screen.getByText('Enter an absolute http or https URL.')).toBeInTheDocument();
    expect(login).not.toHaveBeenCalled();
  });
});

describe('JobsView', () => {
  it('lists jobs with status badges and passes URL filters to the server', async () => {
    const listJobs = vi.fn().mockResolvedValue({ items: [job(), job({ Id: 'kfj_2', Status: 'Failed', Error: 'boom' })], pageNumber: 1, pageSize: 25, totalCount: 2, totalPages: 1 });
    renderWithProviders(<JobsView />, { auth: makeAuth({ apiClient: { listJobs } }), route: '/dashboard/jobs?status=Failed' });
    expect(await screen.findByText('kfj_2')).toBeInTheDocument();
    expect(listJobs).toHaveBeenCalledWith(expect.objectContaining({ status: 'Failed', pageNumber: 1, pageSize: 25 }));
    const table = screen.getByRole('table');
    expect(within(table).getByText('Succeeded')).toBeInTheDocument();
    expect(within(table).getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText(/Showing 1-2 of 2/)).toBeInTheDocument();
  });

  it('shows the empty state and the error state with retry', async () => {
    const user = userEvent.setup();
    const listJobs = vi
      .fn()
      .mockRejectedValueOnce(new ApiError({ network: true }))
      .mockResolvedValueOnce({ items: [], pageNumber: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
    renderWithProviders(<JobsView />, { auth: makeAuth({ apiClient: { listJobs } }) });
    expect(await screen.findByText('The server could not be reached.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No jobs yet')).toBeInTheDocument();
  });

  it('confirms before deleting a finished job', async () => {
    const user = userEvent.setup();
    const listJobs = vi.fn().mockResolvedValue({ items: [job()], pageNumber: 1, pageSize: 25, totalCount: 1, totalPages: 1 });
    const deleteJob = vi.fn().mockResolvedValue(null);
    renderWithProviders(<JobsView />, { auth: makeAuth({ apiClient: { listJobs, deleteJob } }) });
    await screen.findByText('kfj_1');
    await user.click(screen.getByRole('button', { name: 'Actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Delete job kfj_1/)).toBeInTheDocument();
    expect(deleteJob).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteJob).toHaveBeenCalledWith('kfj_1'));
  });
});

describe('SettingsView', () => {
  it('shows a read-only notice to access keys and never requests settings', () => {
    const getSettings = vi.fn();
    renderWithProviders(<SettingsView />, { auth: makeAuth({ isAdmin: false, apiClient: { getSettings } }) });
    expect(screen.getByText('Settings require the admin key')).toBeInTheDocument();
    expect(getSettings).not.toHaveBeenCalled();
  });
});

describe('ServerInfoView', () => {
  it('lists copyable server URLs', () => {
    renderWithProviders(<ServerInfoView />, { auth: makeAuth() });
    expect(screen.getByText('http://127.0.0.1:8700/openapi.json')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy OpenAPI document' })).toBeInTheDocument();
  });
});

describe('EndpointsView capabilities', () => {
  it('shows capability badges with icon and text, including audio input, and filters by capability', async () => {
    const user = userEvent.setup();
    renderWithProviders(<EndpointsView />, { auth: makeAuth({ apiClient: { listEndpoints: vi.fn().mockResolvedValue(ENDPOINTS) } }) });
    const table = await screen.findByRole('table');
    const row = within(table).getByText('llama-listen').closest('tr');
    for (const name of ['Images', 'Structured output', 'Audio input']) {
      const badge = within(row).getByText(name);
      expect(badge.querySelector('svg')).not.toBeNull();
    }
    const whisper = within(table).getByText('whisper').closest('tr');
    expect(within(whisper).getByText('Transcription', { selector: '.capability-badge' })).toBeInTheDocument();
    expect(within(whisper).queryByText('Audio input')).not.toBeInTheDocument();
    const ollama = within(table).getByText('ollama-gemma3-4b').closest('tr');
    expect(within(ollama).queryByText('Audio input')).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Capability'), 'audioInput');
    expect(within(screen.getByRole('table')).getByText('llama-listen')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).queryByText('ollama-gemma3-4b')).not.toBeInTheDocument();
  });
});

describe('SettingsView listening settings', () => {
  const settings = {
    SettingsFile: '/app/keyframe.json',
    RestartRequired: false,
    Fields: [{ Path: 'Media.ListenFormat', ApplyMode: 'Live' }, { Path: 'Inference.Endpoints[].MaxInlineMediaBytes', ApplyMode: 'Live' }],
    Settings: {
      Rest: { Hostname: '127.0.0.1', Port: 8700, Ssl: false, MaxRequestBodyBytes: 1048576 },
      Auth: { AccessKeys: [] },
      Database: { Type: 'Sqlite', Filename: 'keyframe.db' },
      Jobs: {},
      RequestHistory: {},
      Logging: { Servers: [] },
      Telemetry: {},
      Inference: {
        DefaultVision: 'ollama',
        DefaultListening: 'llama-listen',
        HealthProbeIntervalMs: 300000,
        UnhealthyProbeIntervalMs: 15000,
        Endpoints: [
          { Name: 'ollama', Provider: 'Ollama', Kind: 'Completion', MaxInlineMediaBytes: 20971520, MaxListenSeconds: null, Active: true },
          { Name: 'llama-listen', Provider: 'OpenAiCompatible', Kind: 'Completion', MaxInlineMediaBytes: 20971520, MaxListenSeconds: 120, Active: true },
          { Name: 'gemini', Provider: 'Gemini', Kind: 'Completion', MaxInlineMediaBytes: 20971520, MaxListenSeconds: null, Active: true }
        ]
      },
      Media: { MaxAudioDurationMs: 14400000, ListenFormat: 'Mp3', ListenSampleRate: 16000, ListenChannels: 1, ListenBitrateKbps: 64, MaxListenSeconds: 1200, MeasureSilenceDb: -50, MeasureMinSilenceMs: 500, ClippingTruePeakDbtp: -0.1, ClippingMinPeaksPerMinute: 10, MonoMaxSideDb: -40 }
    }
  };

  it('offers only audio-capable endpoints as the default listening endpoint and shows the new media settings', async () => {
    renderWithProviders(<SettingsView />, { auth: makeAuth({ apiClient: { getSettings: vi.fn().mockResolvedValue(settings) } }) });
    const select = await screen.findByLabelText('Default listening endpoint');
    expect(select).toHaveValue('llama-listen');
    const names = within(select).getAllByRole('option').map((o) => o.value);
    expect(names).toEqual(['', 'llama-listen', 'gemini']);
    expect(screen.getByLabelText('Max audio duration (ms)')).toHaveValue(14400000);
    expect(screen.getByLabelText(/Listen format/)).toHaveValue('Mp3');
    expect(screen.getByLabelText('Silence threshold (dBFS)')).toHaveValue(-50);
    expect(screen.getByLabelText('Unhealthy re-probe interval (ms)')).toHaveValue(15000);
    expect(screen.getAllByLabelText(/Listen budget \(seconds\)/).length).toBe(4);
    expect(screen.getAllByLabelText(/Max inline media per call/)[0]).toHaveValue(20971520);
    expect(screen.getByText('Listening audio')).toBeInTheDocument();
    expect(screen.getByText('Measurement thresholds')).toBeInTheDocument();
  });
});
