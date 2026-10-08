import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { electionKey, useApi, type Candidate, type CountyBallot, type Measure, type Page, type RunDetail } from "../api";
import { CaptureLink, Empty, Pager, Pending, StatusBadge, When, count } from "../ui";

type Tab = "candidates" | "measures" | "ballots" | "sources" | "log";
const PAGE = 100;

export function RunPage() {
  const { id = "" } = useParams();
  const [tab, setTab] = useState<Tab>("candidates");
  const detail = useApi<RunDetail>(`/runs/${id}`);
  if (!detail.data) return <Pending query={detail} />;

  const { run: r, pass: p } = detail.data;
  const tabs: [Tab, string][] = [
    ["candidates", `Candidates ${count(r.candidateCount)}`],
    ["measures", `Measures ${count(r.measureCount)}`],
    ["ballots", `County ballots ${count(r.countyBallotCount)}`],
    ["sources", `Gaps ${p.gaps.length} and sources`],
    ["log", "Log"],
  ];

  return (
    <>
      <p className="crumbs">
        <Link to="/">States</Link> / <Link to={`/states/${r.stateCode}`}>{r.stateCode}</Link> / <Link to={`/states/${r.stateCode}/elections/${electionKey(r)}`}>{r.electionDate} {r.electionType}</Link>
      </p>
      <h1>Run {r.runId} {r.isPending && <StatusBadge status="pending" />}</h1>
      <dl className="facts">
        <dt>Election</dt><dd>{r.electionName} ({r.electionDate}, {r.electionType})</dd>
        <dt>Stored</dt><dd><When iso={r.createdAt} /></dd>
        <dt>Built by</dt><dd>pass {p.passId} of <CaptureLink id={p.captureId} />, requested by {p.requestedBy} ({p.source})</dd>
        <dt>Code</dt><dd>{p.gitSha ? p.gitSha.slice(0, 7) : "unknown"}</dd>
        <dt>Pass also stored</dt><dd>{count(p.countyDirectoryCount)} county directory rows, {count(p.proposedMeasureCount)} proposed measures, {count(p.unassignedRowCount)} unassigned rows</dd>
      </dl>

      <div className="tabs">
        {tabs.map(([key, label]) => <button key={key} className={tab === key ? "on" : ""} onClick={() => setTab(key)}>{label}</button>)}
      </div>

      {tab === "candidates" && <Candidates runId={r.runId} />}
      {tab === "measures" && <Measures runId={r.runId} />}
      {tab === "ballots" && <Ballots runId={r.runId} />}

      {tab === "sources" && (
        <>
          <h3>Gaps</h3>
          {p.gaps.length === 0 ? <Empty>None reported.</Empty> : <ul>{p.gaps.map(g => <li key={g} className="warn">{g}</li>)}</ul>}
          {p.nextRun?.after && <p><b>Check again after {p.nextRun.after}.</b> {p.nextRun.reason}</p>}
          <h3>Sources</h3>
          <table>
            <thead><tr><th>Used for</th><th>URL</th><th>Format</th><th>Payload saved</th></tr></thead>
            <tbody>
              {p.sources.map((s, i) => (
                <tr key={i}><td>{s.group.replace(/_/g, " ")}</td><td className="mono wrap">{s.url}</td><td>{s.format}</td><td>{s.hashed ? "yes" : <span className="muted">no</span>}</td></tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      {tab === "log" && <pre className="log">{p.logText ?? p.summary ?? "No log stored."}</pre>}
    </>
  );
}

function Candidates({ runId }: { runId: number }) {
  const [filter, setFilter] = useState("");
  const [offset, setOffset] = useState(0);
  const page = useApi<Page<Candidate>>(`/runs/${runId}/candidates?q=${encodeURIComponent(filter.trim())}&offset=${offset}&limit=${PAGE}`);

  return (
    <>
      <div className="toolbar">
        <input placeholder="Filter by office, name, county, party" value={filter} onChange={e => { setFilter(e.target.value); setOffset(0); }} />
        {page.data && <span className="muted">{count(page.data.total)} {filter.trim() ? "matching" : "candidates"}</span>}
      </div>
      {!page.data ? <Pending query={page} /> : page.data.total === 0 ? <Empty>{filter.trim() ? "Nothing matches." : "This run has no candidates."}</Empty> : (
        <>
          <table>
            <thead><tr><th>Office</th><th>District</th><th>County</th><th>Candidate</th><th>Party</th><th>Filing status</th><th>OCD division</th></tr></thead>
            <tbody>
              {page.data.rows.map((c, i) => (
                <tr key={offset + i}>
                  <td>{c.office}</td><td>{c.district}</td><td>{c.county}</td><td>{c.name}</td><td>{c.party}</td><td>{c.status}</td>
                  <td className="mono">{c.ocdDivisionId ?? <span className="muted">not mapped</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager offset={offset} limit={PAGE} total={page.data.total} onChange={setOffset} />
        </>
      )}
    </>
  );
}

function Measures({ runId }: { runId: number }) {
  const [offset, setOffset] = useState(0);
  const page = useApi<Page<Measure>>(`/runs/${runId}/measures?offset=${offset}&limit=${PAGE}`);
  if (!page.data) return <Pending query={page} />;
  if (page.data.total === 0) return <Empty>This run has no measures.</Empty>;

  return (
    <>
      <table>
        <thead><tr><th>Measure</th><th>Title</th><th>Jurisdiction</th><th>County</th></tr></thead>
        <tbody>{page.data.rows.map((m, i) => <tr key={offset + i}><td>{m.measureId}</td><td>{m.title}</td><td>{m.jurisdiction}</td><td>{m.county}</td></tr>)}</tbody>
      </table>
      <Pager offset={offset} limit={PAGE} total={page.data.total} onChange={setOffset} />
    </>
  );
}

function Ballots({ runId }: { runId: number }) {
  const ballots = useApi<CountyBallot[]>(`/runs/${runId}/county-ballots`);
  if (!ballots.data) return <Pending query={ballots} />;
  if (ballots.data.length === 0) return <Empty>This source does not publish county ballots.</Empty>;

  return (
    <table className="narrow">
      <thead><tr><th>County</th><th className="num">Candidates</th><th className="num">Measures</th></tr></thead>
      <tbody>{ballots.data.map(b => <tr key={b.county}><td>{b.county}</td><td className="num">{count(b.candidateCount)}</td><td className="num">{count(b.measureCount)}</td></tr>)}</tbody>
    </table>
  );
}
