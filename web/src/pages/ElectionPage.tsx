import { Link, useParams } from "react-router-dom";
import { electionsFrom, useApi, type StateDetail } from "../api";
import { CaptureLink, Empty, Pending, When, count } from "../ui";

function Delta({ value, previous }: { value: number; previous: number | undefined }) {
  const change = previous === undefined ? 0 : value - previous;
  return (
    <td className="num">
      {count(value)}
      {change !== 0 && <span className={change > 0 ? "up" : "down"}> {change > 0 ? "+" : ""}{count(change)}</span>}
    </td>
  );
}

export function ElectionPage() {
  const { code = "", key = "" } = useParams();
  const state = useApi<StateDetail>(`/states/${code}`, 5000);
  if (!state.data) return <Pending query={state} />;

  // The router hands back the key decoded, so compare both forms.
  const election = electionsFrom(state.data.runs).find(e => e.key === key || decodeURIComponent(e.key) === key);
  if (!election) return <Empty>No such election for {code}.</Empty>;

  const { latest, runs } = election;
  const passes = new Map(state.data.passes.map(p => [p.passId, p]));

  return (
    <>
      <p className="crumbs"><Link to="/">States</Link> / <Link to={`/states/${code}`}>{state.data.name}</Link></p>
      <h1>{latest.electionDate} {latest.electionType}</h1>
      <p className="muted">{latest.electionName}. Source election id {latest.sourceElectionId || "none"}.</p>

      <h2>Run history</h2>
      <p className="muted">Each normalization stores a new run and never changes an old one. Changes are against the run before it.</p>
      <table>
        <thead>
          <tr><th>Run</th><th>Stored</th><th>From</th><th className="num">Candidates</th><th className="num">Measures</th><th className="num">County ballots</th><th className="num">Gaps</th></tr>
        </thead>
        <tbody>
          {runs.map((r, i) => {
            const before = runs[i + 1];
            const p = passes.get(r.passId);
            return (
              <tr key={r.runId}>
                <td><Link to={`/runs/${r.runId}`}><b>run {r.runId}</b></Link>{i === 0 && <span className="muted"> latest</span>}</td>
                <td><When iso={r.createdAt} /></td>
                <td>pass {r.passId}{p && <> of <CaptureLink id={p.captureId} /></>}</td>
                <Delta value={r.candidateCount} previous={before?.candidateCount} />
                <Delta value={r.measureCount} previous={before?.measureCount} />
                <Delta value={r.countyBallotCount} previous={before?.countyBallotCount} />
                <td className="num">{p?.gaps.length ?? ""}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </>
  );
}
