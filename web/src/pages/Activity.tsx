import { Link } from "react-router-dom";
import { useApi, type ActivityData, type JobsData } from "../api";
import { CaptureLink, Empty, ErrorText, Pending, StatusBadge, When, bytes, duration, kindLabel } from "../ui";

export function Activity() {
  const jobs = useApi<JobsData>("/jobs", 2000);
  const activity = useApi<ActivityData>("/activity", 5000);
  if (!activity.data) return <Pending query={activity} />;

  const { captures, passes, runs } = activity.data;

  return (
    <>
      <h1>Activity</h1>

      <h2>Runs started here</h2>
      <p className="muted">Jobs run one at a time, oldest first. A run made from the CLI has no job and appears only in the tables below.</p>
      {!jobs.data ? <Pending query={jobs} /> : jobs.data.jobs.length === 0 ? <Empty>Nothing has been started from the console yet.</Empty> : (
        <table>
          <thead><tr><th>Job</th><th>State</th><th>What</th><th>Requested</th><th>Took</th><th>Result</th></tr></thead>
          <tbody>
            {jobs.data.jobs.map(j => (
              <tr key={j.jobId}>
                <td className="nowrap"><StatusBadge status={j.status} /> {j.jobId}</td>
                <td><Link to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></Link> {j.year}</td>
                <td>
                  {kindLabel[j.kind]}{j.normalizeCaptureId && ` capture ${j.normalizeCaptureId}`}
                  {j.electionFilter && <div className="muted">only {j.electionFilter}</div>}
                </td>
                <td><When iso={j.requestedAt} /> <span className="muted">by {j.requestedBy}</span></td>
                <td>{j.startedAt ? duration(j.startedAt, j.finishedAt) : ""}</td>
                <td>
                  {j.captureId !== null && <div><CaptureLink id={j.captureId} /></div>}
                  {jobs.data.runs.filter(r => r.passId === j.passId).map(r => (
                    <div key={r.runId}><Link to={`/runs/${r.runId}`}>run {r.runId}</Link> {r.electionDate} {r.electionType}</div>
                  ))}
                  <ErrorText text={j.errorText} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Captures</h2>
      <p className="muted">The 50 most recent across every state, from the console or the CLI.</p>
      {captures.length === 0 ? <Empty>Nothing captured yet.</Empty> : (
        <table>
          <thead><tr><th>Capture</th><th>State</th><th>Started</th><th>Took</th><th>Fetched</th><th>By</th></tr></thead>
          <tbody>
            {captures.map(c => (
              <tr key={c.captureId}>
                <td className="nowrap"><StatusBadge status={c.status} /> <CaptureLink id={c.captureId} /><ErrorText text={c.errorText} /></td>
                <td><Link to={`/states/${c.stateCode}`}><b>{c.stateCode}</b></Link> {c.year}</td>
                <td><When iso={c.startedAt} /></td>
                <td>{duration(c.startedAt, c.finishedAt)}</td>
                <td>{c.fetchCount} requests, {bytes(c.rawBytes)}</td>
                <td>{c.requestedBy} <span className="muted">({c.source})</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Normalizations</h2>
      <p className="muted">The 50 most recent.</p>
      {passes.length === 0 ? <Empty>Nothing normalized yet.</Empty> : (
        <table>
          <thead><tr><th>Pass</th><th>State</th><th>Of</th><th>Started</th><th>Took</th><th>Runs</th><th>By</th></tr></thead>
          <tbody>
            {passes.map(p => (
              <tr key={p.passId}>
                <td className="nowrap"><StatusBadge status={p.status} /> pass {p.passId}</td>
                <td><Link to={`/states/${p.stateCode}`}><b>{p.stateCode}</b></Link> {p.year}</td>
                <td><CaptureLink id={p.captureId} /></td>
                <td><When iso={p.requestedAt} /></td>
                <td>{duration(p.requestedAt, p.finishedAt)}</td>
                <td>
                  {runs.filter(r => r.passId === p.passId).map(r => (
                    <div key={r.runId}><Link to={`/runs/${r.runId}`}>run {r.runId}</Link> {r.electionDate} {r.electionType}</div>
                  ))}
                  <ErrorText text={p.errorText} />
                </td>
                <td>{p.requestedBy} <span className="muted">({p.source})</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
