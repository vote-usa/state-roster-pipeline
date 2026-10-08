import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { candidatesFor, countyBallotsFor, electionKey, measuresFor, pass, rowsPerRun, run, stateInfo } from "../data";
import { CaptureLink, Empty, StatusBadge, When, count } from "../ui";

type Tab = "candidates" | "measures" | "ballots" | "sources" | "log";

export function RunPage() {
  const { id = "" } = useParams();
  const [tab, setTab] = useState<Tab>("candidates");
  const [filter, setFilter] = useState("");
  const r = run(Number(id));
  const p = r && pass(r.passId);
  if (!r || !p) return <Empty>No run {id}.</Empty>;

  const candidates = candidatesFor(r.runId);
  const needle = filter.trim().toLowerCase();
  const shown = needle === ""
    ? candidates
    : candidates.filter(c => [c.office, c.district, c.county, c.name, c.party, c.status].some(v => v?.toLowerCase().includes(needle)));
  const measures = measuresFor(r.runId);
  const ballots = countyBallotsFor(r.runId);

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
        <Link to="/">States</Link> / <Link to={`/states/${r.stateCode}`}>{stateInfo(r.stateCode)?.name}</Link> / <Link to={`/states/${r.stateCode}/elections/${electionKey(r)}`}>{r.electionDate} {r.electionType}</Link>
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

      {tab === "candidates" && (
        <>
          <div className="toolbar">
            <input placeholder="Filter by office, name, county, party" value={filter} onChange={e => setFilter(e.target.value)} />
            <span className="muted">
              {shown.length} shown.{r.candidateCount > rowsPerRun && ` The mock holds the first ${rowsPerRun} of ${count(r.candidateCount)}.`}
            </span>
          </div>
          {candidates.length === 0 ? <Empty>This run has no candidates.</Empty> : (
            <table>
              <thead><tr><th>Office</th><th>District</th><th>County</th><th>Candidate</th><th>Party</th><th>Filing status</th><th>OCD division</th></tr></thead>
              <tbody>
                {shown.map((c, i) => (
                  <tr key={i}>
                    <td>{c.office}</td><td>{c.district}</td><td>{c.county}</td><td>{c.name}</td><td>{c.party}</td><td>{c.status}</td>
                    <td className="mono">{c.ocdDivisionId ?? <span className="muted">not mapped</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      {tab === "measures" && (measures.length === 0 ? <Empty>This run has no measures.</Empty> : (
        <table>
          <thead><tr><th>Measure</th><th>Title</th><th>Jurisdiction</th><th>County</th></tr></thead>
          <tbody>{measures.map((m, i) => <tr key={i}><td>{m.measureId}</td><td>{m.title}</td><td>{m.jurisdiction}</td><td>{m.county}</td></tr>)}</tbody>
        </table>
      ))}

      {tab === "ballots" && (ballots.length === 0 ? <Empty>This source does not publish county ballots.</Empty> : (
        <table className="narrow">
          <thead><tr><th>County</th><th className="num">Candidates</th><th className="num">Measures</th></tr></thead>
          <tbody>{ballots.map(b => <tr key={b.county}><td>{b.county}</td><td className="num">{count(b.candidateCount)}</td><td className="num">{count(b.measureCount)}</td></tr>)}</tbody>
        </table>
      ))}

      {tab === "sources" && (
        <>
          <h3>Gaps</h3>
          {p.gaps.length === 0 ? <Empty>None reported.</Empty> : <ul>{p.gaps.map(g => <li key={g} className="warn">{g}</li>)}</ul>}
          {p.nextRun && <p><b>Check again after {p.nextRun.after}.</b> {p.nextRun.reason}</p>}
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
