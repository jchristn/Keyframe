import { createContext, useCallback, useContext, useId, useMemo, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatBytes } from '../i18n/formatters';
import { describeError } from '../utils/errors';
import { SEGMENT_FORMATS, downloadBlob, filenameFromDisposition, readFileAsBase64 } from '../utils/media';
import Icon from './Icon';

const SegmentContext = createContext(null);

/**
 * Supplies the media source for POST /v1.0/segment. Keyframe does not keep uploaded media,
 * so a job with a SourceUrl downloads directly, and an uploaded job needs the original file
 * picked again (sent as Source.Data).
 */
export function SegmentSourceProvider({ job, knownFile = null, children }) {
  const { apiClient } = useAuth();
  const { notify } = useToast();
  const { t } = useLocale();
  const [file, setFile] = useState(null);
  const [data, setData] = useState(null);
  const [reading, setReading] = useState(false);
  const [busy, setBusy] = useState(null);

  const sourceUrl = job && job.SourceUrl ? job.SourceUrl : null;
  // A file the page already holds (the audio page keeps the upload) counts as a source; it is
  // read as base64 on first use.
  const activeFile = file || knownFile;
  const source = useMemo(
    () => (sourceUrl ? { Url: sourceUrl } : data ? { Data: data, MediaType: activeFile && activeFile.type ? activeFile.type : undefined } : activeFile ? { pending: true } : null),
    [sourceUrl, data, activeFile]
  );

  const pickFile = useCallback(async (f) => {
    if (!f) {
      setFile(null);
      setData(null);
      return;
    }
    setReading(true);
    try {
      setData(await readFileAsBase64(f));
      setFile(f);
    } finally {
      setReading(false);
    }
  }, []);

  const download = useCallback(
    async (output, startMs, endMs) => {
      if (!source) return;
      const key = output + ':' + startMs + ':' + endMs;
      setBusy(key);
      try {
        let resolved = source;
        if (source.pending) {
          const encoded = await readFileAsBase64(activeFile);
          setData(encoded);
          resolved = { Data: encoded, MediaType: activeFile.type || undefined };
        }
        const format = SEGMENT_FORMATS[output];
        const request = { Source: resolved, StartMs: Math.max(0, Math.round(startMs)), Output: output, Format: format };
        if (output !== 'Frame') request.EndMs = Math.round(endMs);
        const { blob, filename } = await apiClient.downloadSegment(request);
        const name = filenameFromDisposition(filename) || 'segment-' + request.StartMs + (request.EndMs ? '-' + request.EndMs : '') + '.' + format;
        downloadBlob(blob, name);
        notify('success', 'segments.toast.downloaded', { name });
      } catch (err) {
        notify('error', 'segments.toast.failed', { message: describeError(t, err) });
      } finally {
        setBusy(null);
      }
    },
    [apiClient, notify, source, activeFile, t]
  );

  const value = useMemo(
    () => ({ mediaKind: job ? job.MediaKind : null, hasUrl: !!sourceUrl, source, file: activeFile, reading, pickFile, download, busy }),
    [job, sourceUrl, source, activeFile, reading, pickFile, download, busy]
  );
  return <SegmentContext.Provider value={value}>{children}</SegmentContext.Provider>;
}

export function useSegmentSource() {
  return useContext(SegmentContext);
}

/** Note shown above scenes and transcript when the job's media was uploaded inline. */
export function SegmentSourceNote() {
  const { t, locale } = useLocale();
  const ctx = useSegmentSource();
  const inputId = useId();
  if (!ctx || ctx.hasUrl) return null;
  return (
    <div className="banner banner-info segment-note" role="status">
      <Icon name="info" />
      <div className="banner-body">
        <div className="banner-title">{t('segments.uploadedTitle')}</div>
        <div>{ctx.file ? t('segments.usingFile', { name: ctx.file.name, size: formatBytes(ctx.file.size, locale) }) : t('segments.uploadedMessage')}</div>
      </div>
      <div className="banner-actions">
        <label htmlFor={inputId} className="button button-secondary button-sm">
          <Icon name="upload" />
          {ctx.reading ? t('segments.reading') : ctx.file ? t('segments.chooseOther') : t('segments.chooseFile')}
        </label>
        <input id={inputId} type="file" accept="video/*,audio/*" className="sr-only" onChange={(e) => ctx.pickFile(e.target.files[0] || null)} />
        {ctx.file ? (
          <button type="button" className="button button-link button-sm" onClick={() => ctx.pickFile(null)}>
            {t('segments.clearFile')}
          </button>
        ) : null}
      </div>
    </div>
  );
}

/**
 * Per-segment download buttons. Video jobs offer clip, audio, and (for scenes) frame;
 * audio jobs offer audio only. Buttons stay disabled until a source is available.
 */
export function SegmentActions({ startMs, endMs, frame = false, compact = false }) {
  const { t } = useLocale();
  const ctx = useSegmentSource();
  if (!ctx) return null;
  const outputs = ctx.mediaKind === 'Audio' ? ['Audio'] : frame ? ['Video', 'Audio', 'Frame'] : ['Video', 'Audio'];
  const disabledTitle = ctx.source ? null : t('segments.needSource');
  return (
    <span className="segment-actions" data-no-row-click>
      {outputs.map((o) => {
        const key = o + ':' + startMs + ':' + endMs;
        const running = ctx.busy === key;
        const icon = <Icon name={running ? 'refresh' : o === 'Frame' ? 'image' : o === 'Audio' ? 'audio' : 'film'} className={running ? 'spin' : ''} />;
        if (compact) {
          return (
            <button
              key={o}
              type="button"
              className="icon-button icon-button-sm"
              onClick={() => ctx.download(o, startMs, endMs)}
              disabled={!ctx.source || running}
              aria-label={t('segments.download.' + o)}
              title={disabledTitle || t('segments.download.' + o)}
            >
              {icon}
            </button>
          );
        }
        return (
          <button
            key={o}
            type="button"
            className="button button-secondary button-sm"
            onClick={() => ctx.download(o, startMs, endMs)}
            disabled={!ctx.source || running}
            title={disabledTitle || t('segments.hint.' + o)}
          >
            {icon}
            {t('segments.download.' + o)}
          </button>
        );
      })}
    </span>
  );
}
