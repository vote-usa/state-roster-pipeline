import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { capturesFor, now, startJob, states, type JobKind } from "./data";
import { ago, bytes, type StartRunPreset } from "./ui";

const NAME_KEY = "roster-console.requested-by";

function savedName(): string {
  try { return localStorage.getItem(NAME_KEY) ?? ""; } catch { return ""; }
}

export function StartRunDialog({ preset, onClose }: { preset: StartRunPreset; onClose: () => void }) {
  const navigate = useNavigate();
  const implemented = states().filter(s => s.implemented);
  const [stateCode, setStateCode] = useState(preset.stateCode ?? implemented[0].code);
  const [year, setYear] = useState(now.getUTCFullYear());
  const [kind, setKind] = useState<JobKind>(preset.normalizeCaptureId ? "normalize" : "both");
  const [captureId, setCaptureId] = useState<number | null>(preset.normalizeCaptureId ?? null);
  const [electionFilter, setElectionFilter] = useState("");
  const [requestedBy, setRequestedBy] = useState(savedName);

  // Only a succeeded capture whose payloads are still on disk can be normalized.
  const usable = capturesFor(stateCode).filter(c => c.status === "succeeded" && c.filesPresent);
  const chosen = usable.find(c => c.captureId === captureId) ?? usable[0];
  const blocked = (kind === "normalize" && !chosen) || requestedBy.trim() === "";

  function submit(e: FormEvent) {
    e.preventDefault();
    try { localStorage.setItem(NAME_KEY, requestedBy.trim()); } catch { /* the name is only a convenience */ }
    startJob({
      stateCode,
      year: kind === "normalize" ? chosen!.year : year,
      kind,
      normalizeCaptureId: kind === "normalize" ? chosen!.captureId : null,
      electionFilter: electionFilter || null,
      requestedBy: requestedBy.trim(),
    });
    onClose();
    navigate("/activity");
  }

  return (
    <div className="backdrop" onClick={onClose}>
      <form className="dialog" onSubmit={submit} onClick={e => e.stopPropagation()}>
        <h2>Start a run</h2>

        <div className="fields">
          <label>State
            <select value={stateCode} onChange={e => { setStateCode(e.target.value); setCaptureId(null); }}>
              {implemented.map(s => <option key={s.code} value={s.code}>{s.code} {s.name}</option>)}
            </select>
          </label>
          <label>Year
            <input type="number" value={year} disabled={kind === "normalize"} onChange={e => setYear(Number(e.target.value))} />
          </label>
        </div>

        <fieldset>
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
            ? <p className="warn">{stateCode} has no succeeded capture with its payloads still on disk.</p>
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
            <input type="date" value={electionFilter} disabled={kind === "capture"} onChange={e => setElectionFilter(e.target.value)} />
          </label>
          <label>Your name <span className="muted">(recorded on the run)</span>
            <input value={requestedBy} onChange={e => setRequestedBy(e.target.value)} />
          </label>
        </div>

        <div className="actions">
          <button type="button" onClick={onClose}>Cancel</button>
          <button type="submit" className="primary" disabled={blocked}>Start</button>
        </div>
      </form>
    </div>
  );
}
