import { useState } from "react";
import { NavLink, Route, Routes } from "react-router-dom";
import { isActive, useApi, type JobsData } from "./api";
import { StartRunContext, type StartRunPreset } from "./ui";
import { StartRunDialog } from "./StartRunDialog";
import { Overview } from "./pages/Overview";
import { StatePage } from "./pages/StatePage";
import { ElectionPage } from "./pages/ElectionPage";
import { RunPage } from "./pages/RunPage";
import { CapturePage } from "./pages/CapturePage";
import { Activity } from "./pages/Activity";

export function App() {
  const [preset, setPreset] = useState<StartRunPreset | null>(null);
  const active = (useApi<JobsData>("/jobs", 2000).data?.jobs ?? []).filter(isActive).length;

  return (
    <StartRunContext.Provider value={setPreset}>
      <header className="top">
        <strong>Roster console</strong>
        <nav>
          <NavLink to="/" end>States</NavLink>
          <NavLink to="/activity">Activity{active > 0 && <span className="pill">{active}</span>}</NavLink>
        </nav>
        <span className="spacer" />
        <button className="primary" onClick={() => setPreset({})}>Start a run</button>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<Overview />} />
          <Route path="/states/:code" element={<StatePage />} />
          <Route path="/states/:code/elections/:key" element={<ElectionPage />} />
          <Route path="/runs/:id" element={<RunPage />} />
          <Route path="/retrieves/:id" element={<CapturePage />} />
          <Route path="/activity" element={<Activity />} />
        </Routes>
      </main>
      {preset && <StartRunDialog preset={preset} onClose={() => setPreset(null)} />}
    </StartRunContext.Provider>
  );
}
