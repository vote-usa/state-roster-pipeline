import { Link } from "react-router-dom";
import { useApi, type ActivityData } from "../api";
import { CaptureLink, Empty, ErrorText, Pending, StatusBadge, When, bytes, duration } from "../ui";

export function Activity() {
  const activity = useApi<ActivityData>("/activity", 5000);
  if (!activity.data) return <Pending query={activity} />;

  const { captures, passes, runs } = activity.data;

  return (
    <>
      <h1>Activity</h1>
      <p className="muted">The 50 most recent captures and normalizations across every state, from the CLI or anywhere else.</p>

      <h2>Captures</h2>
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
