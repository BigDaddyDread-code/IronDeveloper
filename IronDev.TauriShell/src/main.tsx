import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { applyLocalTestFreshSessionFromUrl } from './app/localTestFreshSession';
import './styles/tokens.css';
import './styles/app.css';

applyLocalTestFreshSessionFromUrl();

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
