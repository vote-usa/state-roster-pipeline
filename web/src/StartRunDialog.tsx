import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { startJob, useApi, type RunKind, type StateDetail, type StateSummary } from "./api";
import { ErrorText, Pending, ago, bytes, type StartRunPreset } from "./ui";

const NAME_KEY = "roster-console.requested-by";

function savedName(): string {
  try { return localStorage.getItem(NAME_KEY) ?? ""; } catch { return ""; }
}

export function StartRunDialog({ preset, onClose }: { preset: StartRunPreset; onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const states = useApi<StateSummary[]>("/states");
  const implemented = (states.data ?? []).filter(s => s.implemented);
  const [picked, setPicked] = useState(preset.stateCode);
  const stateCode = picked ?? implemented[0]?.code ?? "";
  const [year, setYear] = useState(new Date().getUTCFullYear());
  const [kind, setKind] = useState<RunKind>(preset.normalizeCaptureId ? "normalize" : "both");
  const [captureId, setCaptureId] = useState<number | null>(preset.normalizeCaptureId ?? null);
  const [electionFilter, setElectionFilter] = useState("");
  const [requestedBy, setRequestedBy] = useState(savedName);
  const [sending, setSending] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const detail = useApi<StateDetail>(`/states/${stateCode || "none"}`);

  if (!states.data) return <div className="backdrop" onClick={onClose}><div className="dialog"><Pending query={states} /></div></div>;

  // Only a succeeded retrieve whose payloads are still on disk can be normalized.
  const usable = (detail.data?.code === stateCode ? detail.data.captures : []).filter(c => c.status === "succeeded" && c.filesPresent);
  const chosen = usable.find(c => c.captureId === captureId) ?? usable[0];
  const name = requestedBy.trim();
  const blocked = sending || name === "" || (kind === "normalize" && !chosen);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSending(true);
    setRefusal(null);
    try { localStorage.setItem(NAME_KEY, name); } catch { /* the name is only a convenience */ }
    try {
      await startJob({
        stateCode,
        year: kind === "normalize" ? chosen!.year : year,
        kind,
        normalizeCaptureId: kind === "normalize" ? chosen!.captureId : null,
        electionFilter: kind !== "capture" && electionFilter ? electionFilter : null,
        requestedBy: name,
      });
      await queryClient.invalidateQueries({ queryKey: ["/jobs"] });
      onClose();
      navigate("/activity");
    } catch (error) {
      setRefusal(error instanceof Error ? error.message : String(error));
      setSending(false);
    }
  }

  return (
    <div className="backdrop" onClick={onClose}>
      <form className="dialog" onSubmit={submit} onClick={e => e.stopPropagation()}>
        <h2>Start a run</h2>

        <div className="fields">
          <label>State
            <select value={stateCode} onChange={e => { setPicked(e.target.value); setCaptureId(null); }}>
              {implemented.map(s => <option key={s.code} value={s.code}>{s.code} {s.name}</option>)}
            </select>
          </label>
          <label>Year
            <input type="number" value={kind === "normalize" && chosen ? chosen.year : year} disabled={kind === "normalize"} onChange={e => setYear(Number(e.target.value))} />
          </label>
        </div>

        <fieldset>
          <legend>What to run</legend>
          <label className="choice">
            <input type="radio" checked={kind === "both"} onChange={() => setKind("both")} />
            <span><b>Retrieve and normalize</b><small>Fetch every source page, then build one run per election from it.</small></span>
          </label>
          <label className="choice">
            <input type="radio" checked={kind === "capture"} onChange={() => setKind("capture")} />
            <span><b>Retrieve only</b><small>Fetch and save the source pages. Nothing is parsed yet.</small></span>
          </label>
          <label className="choice">
            <input type="radio" checked={kind === "normalize"} onChange={() => setKind("normalize")} />
            <span><b>Normalize an existing retrieve</b><small>Rebuild runs from saved pages. No network, and the year comes from the retrieve.</small></span>
          </label>
          {kind === "normalize" && (usable.length === 0
            ? <p className="warn">{detail.data ? `${stateCode} has no succeeded retrieve with its payloads still on disk.` : "Loading retrieves…"}</p>
            : <select className="indent" value={chosen!.captureId} onChange={e => setCaptureId(Number(e.target.value))}>
                {usable.map(c => (
                  <option key={c.captureId} value={c.captureId}>
                    retrieve {c.captureId}, {c.year}, {ago(c.startedAt)}, {c.fetchCount} fetches, {bytes(c.rawBytes)}
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

        <ErrorText text={refusal} />

        <div className="actions">
          <button type="button" onClick={onClose}>Cancel</button>
          <button type="submit" className="primary" disabled={blocked}>{sending ? "Starting…" : "Start"}</button>
        </div>
      </form>
    </div>
  );
}
