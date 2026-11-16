import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Load remote data with loading, error, and reload state. The loader receives an
 * AbortSignal-like "isCurrent" guard so stale responses never overwrite newer ones.
 * Previous data is kept while reloading so tables do not flash empty.
 */
export function useRemote(loader, deps, { enabled = true } = {}) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(enabled);
  const [loadedAt, setLoadedAt] = useState(null);
  const seq = useRef(0);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(loader, deps);

  const reload = useCallback(async () => {
    if (!enabled) return;
    seq.current += 1;
    const mine = seq.current;
    setLoading(true);
    setError(null);
    try {
      const result = await run();
      if (mine === seq.current) {
        setData(result);
        setLoadedAt(new Date());
      }
    } catch (err) {
      if (mine === seq.current) setError(err);
    } finally {
      if (mine === seq.current) setLoading(false);
    }
  }, [run, enabled]);

  useEffect(() => {
    reload();
  }, [reload]);

  return { data, error, loading, reload, setData, loadedAt };
}

export default useRemote;
