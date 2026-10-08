import { useState } from "react";
import { Link } from "react-router-dom";
import { electionKey, isActive, useApi, type JobsData, type StateSummary } from "../api";
import { CaptureLink, Pending, SourceLinks, Stage, StatusBadge, ago, count, duration, kindLabel, useStartRun } from "../ui";

export function Overview() {
  const startRun = useStartRun();
  const [search, setSearch] = useState("");
  const states = useApi<StateSummary[]>("/states", 5000);
  const active = (useApi<JobsData>("/jobs", 2000).data?.jobs ?? []).filter(isActive).reverse();
  if (!states.data) return <Pending query={states} />;

  const needle = search.trim().toLowerCase();
  const matches = (s: StateSummary) => needle === "" || s.name.toLowerCase().includes(needle) || s.code.toLowerCase() === needle;
  const implemented = states.data.filter(s => s.implemented && matches(s));
  const unimplemented = states.data.filter(s => !s.implemented);

  return (
    <>
      <h1>National – US States</h1>
      <input className="search" type="search" placeholder="Search states" value={search} onChange={e => setSearch(e.target.value)} />

      {active.length > 0 && (
        <ul className="active">
          {active.map(j => (
            <li key={j.jobId}>
              <StatusBadge status={j.status} /> <Link to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></Link> {kindLabel[j.kind].toLowerCase()}
              {j.normalizeCaptureId && ` retrieve ${j.normalizeCaptureId}`}
              {j.status === "running" && <span className="muted">, {duration(j.startedAt, null)} so far</span>}
            </li>
          ))}
        </ul>
      )}

      <table>
        <thead>
          <tr>
            <th>State</th><th>Overview URLs</th><th>Roster URLs</th><th>Latest retrieve</th>
            <th>Elections, latest run of each</th><th>Stages</th>
          </tr>
        </thead>
        <tbody>
          {implemented.map(s => (
            <tr key={s.code}>
              <td className="nowrap"><Link to={`/states/${s.code}`}><b>{s.name}</b></Link></td>
              <td><SourceLinks sources={s.sources} kind="overview" /></td>
              <td><SourceLinks sources={s.sources} kind="roster" /></td>
              <td className="nowrap">
                {s.lastCapture
                  ? <div><StatusBadge status={s.lastCapture.status} /> <CaptureLink id={s.lastCapture.captureId} /> <span className="muted">{ago(s.lastCapture.startedAt)}</span></div>
                  : <div className="muted">never retrieved</div>}
                {s.nextRun?.after && <div className="muted" title={s.nextRun.reason ?? undefined}>check again after {s.nextRun.after}</div>}
                <button className="link" onClick={() => startRun({ stateCode: s.code })}>Start a run</button>
              </td>
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
              <td className="nowrap">
                <Stage label="Retrieve" status={s.lastCapture?.status ?? null}
                  detail={s.lastCapture ? `retrieve ${s.lastCapture.captureId} ${s.lastCapture.status}, ${ago(s.lastCapture.startedAt)}` : "never run"} />
                <Stage label="Normalize" status={s.lastPass?.status ?? null}
                  detail={s.lastPass ? `pass ${s.lastPass.passId} of retrieve ${s.lastPass.captureId} ${s.lastPass.status}, ${ago(s.lastPass.requestedAt)}` : "never run"} />
                {s.gapCount !== null && s.gapCount > 0 && <b className="warn">{s.gapCount} {s.gapCount === 1 ? "gap" : "gaps"}</b>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {implemented.length === 0 && <p className="empty">No implemented state matches “{search}”.</p>}

      <details>
        <summary>{unimplemented.length} states with no collector yet</summary>
        <p className="muted">{unimplemented.map(s => s.name).join(", ")}</p>
      </details>
    </>
  );
}
