import { useState } from "react";
import { useApi, type RunKind, type StateDetail, type StateSummary } from "./api";
import { Pending, ago, bytes, type StartRunPreset } from "./ui";

const NAME_KEY = "roster-console.requested-by";

function savedName(): string {
  try { return localStorage.getItem(NAME_KEY) ?? ""; } catch { return ""; }
}

export function StartRunDialog({ preset, onClose }: { preset: StartRunPreset; onClose: () => void }) {
  const states = useApi<StateSummary[]>("/states");
  const implemented = (states.data ?? []).filter(s => s.implemented);
  const [picked, setPicked] = useState(preset.stateCode);
  const stateCode = picked ?? implemented[0]?.code ?? "";
  const [year, setYear] = useState(new Date().getUTCFullYear());
  const [kind, setKind] = useState<RunKind>(preset.normalizeCaptureId ? "normalize" : "both");
  const [captureId, setCaptureId] = useState<number | null>(preset.normalizeCaptureId ?? null);
  const [electionFilter, setElectionFilter] = useState("");
  const [requestedBy, setRequestedBy] = useState(savedName);
  const [copied, setCopied] = useState(false);
  const detail = useApi<StateDetail>(`/states/${stateCode || "none"}`);

  if (!states.data) return <div className="backdrop" onClick={onClose}><div className="dialog"><Pending query={states} /></div></div>;

  // Only a succeeded capture whose payloads are still on disk can be normalized.
  const usable = (detail.data?.code === stateCode ? detail.data.captures : []).filter(c => c.status === "succeeded" && c.filesPresent);
  const chosen = usable.find(c => c.captureId === captureId) ?? usable[0];
  const blocked = kind === "normalize" && !chosen;

  const name = requestedBy.trim();
  const command = [
    "dotnet run --project src/StateBallot.Cli --",
    `--state ${stateCode}`,
    kind === "normalize" ? `--normalize ${chosen?.captureId ?? "<capture>"}` : `--year ${year}`,
    kind === "capture" ? "--capture-only" : "",
    kind !== "capture" && electionFilter ? `--election ${electionFilter}` : "",
    name ? `--triggered-by ${/\s/.test(name) ? `"${name}"` : name}` : "",
  ].filter(Boolean).join(" ");

  async function copy() {
    try { localStorage.setItem(NAME_KEY, name); } catch { /* the name is only a convenience */ }
    try {
      await navigator.clipboard.writeText(command);
      setCopied(true);
    } catch { /* the command is on screen to select by hand */ }
  }

  return (
    <div className="backdrop" onClick={onClose}>
      <div className="dialog" onClick={e => e.stopPropagation()}>
        <h2>Start a run</h2>

        <div className="fields">
          <label>State
            <select value={stateCode} onChange={e => { setPicked(e.target.value); setCaptureId(null); setCopied(false); }}>
              {implemented.map(s => <option key={s.code} value={s.code}>{s.code} {s.name}</option>)}
            </select>
          </label>
          <label>Year
            <input type="number" value={year} disabled={kind === "normalize"} onChange={e => setYear(Number(e.target.value))} />
          </label>
        </div>

        <fieldset onChange={() => setCopied(false)}>
          <legend>What to run</legend>
          <label className="choice">
            <input type="radio" checked={kind === "both"} onChange={() => setKind("both")} />
            <span><b>Capture and normalize</b><small>Fetch every source page, then build one run per election from it.</small></span>
          </label>
          <label className="choice">
            <input type="radio" checked={kind === "capture"} onChange={() => setKind("capture")} />
            <span><b>Capture only</b><small>Fetch and save the source pages. Nothing is parsed yet.</small></span>
          </label>
          <label className="choice">
            <input type="radio" checked={kind === "normalize"} onChange={() => setKind("normalize")} />
            <span><b>Normalize an existing capture</b><small>Rebuild runs from saved pages. No network, and the year comes from the capture.</small></span>
          </label>
          {kind === "normalize" && (usable.length === 0
            ? <p className="warn">{detail.data ? `${stateCode} has no succeeded capture with its payloads still on disk.` : "Loading captures…"}</p>
            : <select className="indent" value={chosen!.captureId} onChange={e => setCaptureId(Number(e.target.value))}>
                {usable.map(c => (
                  <option key={c.captureId} value={c.captureId}>
                    capture {c.captureId}, {c.year}, {ago(c.startedAt)}, {c.fetchCount} fetches, {bytes(c.rawBytes)}
                  </option>
                ))}
              </select>)}
        </fieldset>

        <div className="fields">
          <label>Only this election <span className="muted">(optional)</span>
            <input type="date" value={electionFilter} disabled={kind === "capture"} onChange={e => { setElectionFilter(e.target.value); setCopied(false); }} />
          </label>
          <label>Your name <span className="muted">(recorded on the run)</span>
            <input value={requestedBy} onChange={e => { setRequestedBy(e.target.value); setCopied(false); }} />
          </label>
        </div>

        <p className="muted note">The console cannot start runs yet. Until it can, this is the same run from the repo root:</p>
        <pre className="log command">{command}</pre>

        <div className="actions">
          <button type="button" onClick={onClose}>Close</button>
          <button type="button" className="primary" disabled={blocked} onClick={copy}>{copied ? "Copied" : "Copy command"}</button>
        </div>
      </div>
    </div>
  );
}
