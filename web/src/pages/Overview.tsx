import { Link } from "react-router-dom";
import { capturesFor, electionKey, electionsFor, passesForState, states, useJobs } from "../data";
import { CaptureLink, StatusBadge, ago, count, duration, kindLabel, useStartRun } from "../ui";

export function Overview() {
  const startRun = useStartRun();
  const active = useJobs().filter(j => j.status === "queued" || j.status === "running");
  const implemented = states().filter(s => s.implemented);
  const unimplemented = states().filter(s => !s.implemented);

  return (
    <>
      <h1>States</h1>

      {active.length > 0 && (
        <ul className="active">
          {active.map(j => (
            <li key={j.jobId}>
              <StatusBadge status={j.status} /> <b>{j.stateCode}</b> {kindLabel[j.kind].toLowerCase()}
              {j.captureId && <>, <CaptureLink id={j.captureId} /></>}
              {j.status === "running" && <span className="muted">, {duration(j.startedAt, null)} so far</span>}
            </li>
          ))}
        </ul>
      )}

      <table>
        <thead>
          <tr>
            <th>State</th><th>Last capture</th><th>Last normalization</th><th>Elections, latest run of each</th>
            <th className="num">Gaps</th><th>Check again after</th><th />
          </tr>
        </thead>
        <tbody>
          {implemented.map(s => {
            const lastCapture = capturesFor(s.code)[0];
            const passes = passesForState(s.code);
            const lastPass = passes[0];
            const lastGood = passes.find(p => p.status === "succeeded");
            return (
              <tr key={s.code}>
                <td className="nowrap"><Link to={`/states/${s.code}`}><b>{s.code}</b> {s.name}</Link></td>
                <td className="nowrap">{lastCapture
                  ? <><StatusBadge status={lastCapture.status} /> <CaptureLink id={lastCapture.captureId} /> <span className="muted">{ago(lastCapture.startedAt)}</span></>
                  : <span className="muted">never</span>}</td>
                <td>{lastPass
                  ? <><StatusBadge status={lastPass.status} /> pass {lastPass.passId} <span className="muted">of capture {lastPass.captureId}, {ago(lastPass.requestedAt)}</span></>
                  : <span className="muted">never</span>}</td>
                <td>
                  {electionsFor(s.code).map(e => (
                    <div key={e.key}>
                      <Link to={`/states/${s.code}/elections/${electionKey(e.latest)}`}>{e.latest.electionDate} {e.latest.electionType}</Link>
                      {" "}<span className="muted">{count(e.latest.candidateCount)} candidates
                        {e.latest.measureCount > 0 && `, ${count(e.latest.measureCount)} measures`}</span>
                      {e.latest.isPending && <> <StatusBadge status="pending" /></>}
                    </div>
                  ))}
                </td>
                <td className="num">{lastGood ? (lastGood.gaps.length > 0 ? <b className="warn">{lastGood.gaps.length}</b> : 0) : ""}</td>
                <td title={lastGood?.nextRun?.reason}>{lastGood?.nextRun?.after}</td>
                <td><button onClick={() => startRun({ stateCode: s.code })}>Run</button></td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <details>
        <summary>{unimplemented.length} states with no collector yet</summary>
        <p className="muted">{unimplemented.map(s => s.name).join(", ")}</p>
      </details>
    </>
  );
}
