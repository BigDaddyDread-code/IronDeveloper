import { AppProviders } from './app/AppProviders';
import { FlowShell } from './flow/FlowShell';

export default function App() {
  return (
    <AppProviders>
      <FlowShell />
    </AppProviders>
  );
}
