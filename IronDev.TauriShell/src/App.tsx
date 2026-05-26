import { AppProviders } from './app/AppProviders';
import { IronDevShell } from './shell/IronDevShell';

export default function App() {
  return (
    <AppProviders>
      <IronDevShell />
    </AppProviders>
  );
}
