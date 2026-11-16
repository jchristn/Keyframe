import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { i18nReady } from './i18n';
import { applyAppearance, installPaletteStyles, readAppearance } from './theme';
import App from './App';
import './index.css';
import './App.css';

// Palette tokens and the saved appearance apply before React renders anything.
installPaletteStyles();
applyAppearance(readAppearance());

// Render once the active locale's catalog is loaded, so there is no English flash.
i18nReady.finally(() => {
  createRoot(document.getElementById('root')).render(
    <StrictMode>
      <App />
    </StrictMode>
  );
});
