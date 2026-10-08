import { Link } from "react-router-dom";
import { electionKey, isActive, useApi, type JobsData, type StateSummary } from "../api";
import { CaptureLink, Pending, StatusBadge, ago, count, duration, kindLabel, useStartRun } from "../ui";

export function Overview() {
  const startRun = useStartRun();
  const states = useApi<StateSummary[]>("/states", 5000);
  const active = (useApi<JobsData>("/jobs", 2000).data?.jobs ?? []).filter(isActive).reverse();
  if (!states.data) return <Pending query={states} />;

  const implemented = states.data.filter(s => s.implemented);
  const unimplemented = states.data.filter(s => !s.implemented);

  return (
    <>
      <h1>States</h1>

      {active.length > 0 && (
        <ul className="active">
          {active.map(j => (
            <li key={j.jobId}>
              <StatusBadge status={j.status} /> <Link to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></Link> {kindLabel[j.kind].toLowerCase()}
              {j.normalizeCaptureId && ` capture ${j.normalizeCaptureId}`}
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
          {implemented.map(s => (
            <tr key={s.code}>
              <td className="nowrap"><Link to={`/states/${s.code}`}><b>{s.code}</b> {s.name}</Link></td>
              <td className="nowrap">{s.lastCapture
                ? <><StatusBadge status={s.lastCapture.status} /> <CaptureLink id={s.lastCapture.captureId} /> <span className="muted">{ago(s.lastCapture.startedAt)}</span></>
                : <span className="muted">never</span>}</td>
              <td>{s.lastPass
                ? <><StatusBadge status={s.lastPass.status} /> pass {s.lastPass.passId} <span className="muted">of capture {s.lastPass.captureId}, {ago(s.lastPass.requestedAt)}</span></>
                : <span className="muted">never</span>}</td>
              <td>
                {s.elections.map(e => (
                  <div key={e.runId}>
                    <Link to={`/states/${s.code}/elections/${electionKey(e)}`}>{e.electionDate} {e.electionType}</Link>
                    {" "}<span className="muted">{count(e.candidateCount)} candidates
                      {e.measureCount > 0 && `, ${count(e.measureCount)} measures`}</span>
                    {e.isPending && <> <StatusBadge status="pending" /></>}
                  </div>
                ))}
              </td>
              <td className="num">{s.gapCount === null ? "" : s.gapCount > 0 ? <b className="warn">{s.gapCount}</b> : 0}</td>
              <td title={s.nextRun?.reason ?? undefined}>{s.nextRun?.after}</td>
              <td><button onClick={() => startRun({ stateCode: s.code })}>Run</button></td>
            </tr>
          ))}
        </tbody>
      </table>

      <details>
        <summary>{unimplemented.length} states with no collector yet</summary>
        <p className="muted">{unimplemented.map(s => s.name).join(", ")}</p>
      </details>
    </>
  );
}
