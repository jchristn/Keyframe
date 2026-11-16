import { useEffect, useState } from 'react';
import { readStorage, writeStorage } from '../utils/storage';

/** useState that survives reloads through localStorage (falls back to memory). */
export function usePersistedState(key, initial) {
  const [value, setValue] = useState(() => {
    const MISSING = {};
    const saved = readStorage(key, MISSING);
    return saved === MISSING || saved === null ? initial : saved;
  });
  useEffect(() => {
    writeStorage(key, value);
  }, [key, value]);
  return [value, setValue];
}

export default usePersistedState;
