import { Link, useParams } from "react-router-dom";
import { useApi, type CaptureDetail } from "../api";
import { Empty, ErrorText, Pending, StatusBadge, When, bytes, count, duration, useStartRun } from "../ui";

export function CapturePage() {
  const { id = "" } = useParams();
  const startRun = useStartRun();
  const detail = useApi<CaptureDetail>(`/captures/${id}`, data => (data?.capture.status === "running" ? 2000 : false));
  if (!detail.data) return <Pending query={detail} />;

  const { capture: c, fetches, passes, runs } = detail.data;
  const canNormalize = c.status === "succeeded" && c.filesPresent;

  return (
    <>
      <p className="crumbs"><Link to="/">States</Link> / <Link to={`/states/${c.stateCode}`}>{c.stateCode}</Link></p>
      <div className="title">
        <h1>Retrieve {c.captureId} <StatusBadge status={c.status} /></h1>
        {canNormalize && <button className="primary" onClick={() => startRun({ stateCode: c.stateCode, normalizeCaptureId: c.captureId })}>Normalize this retrieve</button>}
      </div>
      <dl className="facts">
        <dt>State and year</dt><dd>{c.stateCode} {c.year}{c.wayback && `, via Wayback ${c.wayback}`}</dd>
        <dt>Started</dt><dd><When iso={c.startedAt} />, {c.finishedAt ? `took ${duration(c.startedAt, c.finishedAt)}` : `running for ${duration(c.startedAt, null)}`}</dd>
        <dt>Requested by</dt><dd>{c.requestedBy} ({c.source})</dd>
        <dt>Fetched</dt><dd>{c.fetchCount} requests, {bytes(c.rawBytes)}</dd>
        <dt>Payloads</dt><dd>{c.filesPresent
          ? `on disk at data/${c.rawDir}`
          : c.status === "running" ? "being written" : "not on disk. The fetch records below remain, but this retrieve cannot be normalized."}</dd>
      </dl>
      <ErrorText text={c.errorText} />

      <h2>Normalizations of this retrieve</h2>
      {passes.length === 0 ? <Empty>None yet.</Empty> : (
        <table>
          <thead><tr><th>Pass</th><th>When</th><th>Runs</th><th className="num">Gaps</th></tr></thead>
          <tbody>
            {passes.map(p => (
              <tr key={p.passId}>
                <td className="nowrap"><StatusBadge status={p.status} /> pass {p.passId}</td>
                <td><When iso={p.requestedAt} /></td>
                <td>
                  {runs.filter(r => r.passId === p.passId).map(r => (
                    <div key={r.runId}><Link to={`/runs/${r.runId}`}>run {r.runId}</Link> {r.electionDate} {r.electionType} <span className="muted">{count(r.candidateCount)} candidates</span></div>
                  ))}
                  <ErrorText text={p.errorText} />
                </td>
                <td className="num">{p.gaps.length}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Fetches</h2>
      {fetches.length === 0 ? <Empty>{c.status === "running" ? "Fetch records are stored when the retrieve finishes." : "No fetches recorded."}</Empty> : (
        <table>
          <thead><tr><th className="num">#</th><th>Role</th><th>Keys</th><th>Request</th><th className="num">HTTP</th><th className="num">Size</th><th className="num">Time</th></tr></thead>
          <tbody>
            {fetches.map(f => (
              <tr key={f.seq}>
                <td className="num">{f.seq}</td>
                <td>{f.role}</td>
                <td className="mono">{f.keys && Object.entries(f.keys).map(([k, v]) => `${k}=${v}`).join(" ")}</td>
                <td className="mono wrap">{f.method} {f.url}{f.error && <div className="warn">{f.error}</div>}</td>
                <td className={`num ${f.status !== null && f.status < 400 ? "" : "warn"}`}>{f.status ?? "none"}</td>
                <td className="num">{bytes(f.bytes)}</td>
                <td className="num">{f.durationMs} ms</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Log</h2>
      <pre className="log">{c.logText ?? "No log yet."}</pre>
    </>
  );
}
