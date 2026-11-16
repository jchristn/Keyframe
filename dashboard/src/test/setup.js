import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import i18n from '../i18n';

// Tests run in English regardless of the host locale.
await i18n.changeLanguage('en');

afterEach(() => {
  cleanup();
  localStorage.clear();
});

if (typeof window !== 'undefined' && !window.matchMedia) {
  window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
}
