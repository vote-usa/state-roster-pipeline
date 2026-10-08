import { Link, useParams } from "react-router-dom";
import { capturesFor, electionKey, electionsFor, passesForCapture, runsForPass, stateInfo } from "../data";
import { CaptureLink, Empty, ErrorText, StatusBadge, When, bytes, count, duration, useStartRun } from "../ui";

export function StatePage() {
  const { code = "" } = useParams();
  const startRun = useStartRun();
  const state = stateInfo(code);
  if (!state) return <Empty>No state {code}.</Empty>;

  const elections = electionsFor(code);
  const captures = capturesFor(code);

  return (
    <>
      <p className="crumbs"><Link to="/">States</Link></p>
      <div className="title">
        <h1>{state.name}</h1>
        <button className="primary" onClick={() => startRun({ stateCode: code })}>Start a run</button>
      </div>

      <h2>Elections</h2>
      {elections.length === 0 && <Empty>No runs stored for {state.name} yet.</Empty>}
      <div className="cards">
        {elections.map(e => (
          <Link key={e.key} className="card" to={`/states/${code}/elections/${electionKey(e.latest)}`}>
            <b>{e.latest.electionDate}</b> {e.latest.electionType}
            {e.latest.isPending && <> <StatusBadge status="pending" /></>}
            <div className="muted">{e.latest.electionName}</div>
            <div className="stats">
              <span><b>{count(e.latest.candidateCount)}</b> candidates</span>
              <span><b>{count(e.latest.measureCount)}</b> measures</span>
              <span><b>{count(e.latest.countyBallotCount)}</b> county ballots</span>
            </div>
            <div className="muted">{e.runs.length} {e.runs.length === 1 ? "run" : "runs"}, latest is run {e.latest.runId}</div>
          </Link>
        ))}
      </div>

      <h2>Captures and what was built from them</h2>
      {captures.length === 0 && <Empty>Nothing captured yet.</Empty>}
      {captures.map(c => {
        const passes = passesForCapture(c.captureId);
        return (
          <section key={c.captureId} className="block">
            <div className="blockhead">
              <StatusBadge status={c.status} />
              <b><CaptureLink id={c.captureId} /></b>
              <When iso={c.startedAt} />
              <span className="muted">{c.fetchCount} fetches, {bytes(c.rawBytes)}, {duration(c.startedAt, c.finishedAt)}, by {c.requestedBy} ({c.source})</span>
              <span className="spacer" />
              {c.status === "succeeded" && (c.filesPresent
                ? <button onClick={() => startRun({ stateCode: code, normalizeCaptureId: c.captureId })}>Normalize</button>
                : <StatusBadge status="pruned" />)}
            </div>
            <ErrorText text={c.errorText} />
            {passes.length === 0 && c.status === "succeeded" && <p className="muted indent">Captured only. Not normalized yet.</p>}
            {passes.map(p => (
              <div key={p.passId} className="indent passrow">
                <StatusBadge status={p.status} /> pass {p.passId} <When iso={p.requestedAt} />
                {p.gaps.length > 0 && <span className="warn"> {p.gaps.length} {p.gaps.length === 1 ? "gap" : "gaps"}</span>}
                {p.unassignedRowCount > 0 && <span className="warn"> {p.unassignedRowCount} unassigned rows</span>}
                <ErrorText text={p.errorText} />
                <ul>
                  {runsForPass(p.passId).map(r => (
                    <li key={r.runId}>
                      <Link to={`/runs/${r.runId}`}>run {r.runId}</Link> {r.electionDate} {r.electionType}
                      <span className="muted"> {count(r.candidateCount)} candidates, {count(r.measureCount)} measures, {count(r.countyBallotCount)} county ballots</span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </section>
        );
      })}
    </>
  );
}
