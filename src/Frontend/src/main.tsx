import '@fontsource-variable/public-sans/wght.css';
import './styles/tokens.css';
import './styles/base.css';
import './styles/ui.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Falta el elemento #root.');
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
