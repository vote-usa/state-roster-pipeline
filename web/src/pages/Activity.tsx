import { Link } from "react-router-dom";
import { runsForPass, useJobs } from "../data";
import { CaptureLink, ErrorText, StatusBadge, When, duration, kindLabel } from "../ui";

export function Activity() {
  const jobs = useJobs();

  return (
    <>
      <h1>Activity</h1>
      <p className="muted">Every run started from this console, newest first. Jobs run one at a time.</p>
      <table>
        <thead><tr><th>Job</th><th>State</th><th>What</th><th>Requested</th><th>Took</th><th>Result</th></tr></thead>
        <tbody>
          {jobs.map(j => (
            <tr key={j.jobId}>
              <td className="nowrap"><StatusBadge status={j.status} /> {j.jobId}</td>
              <td><Link to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></Link> {j.year}</td>
              <td>
                {kindLabel[j.kind]}{j.normalizeCaptureId && ` capture ${j.normalizeCaptureId}`}
                {j.electionFilter && <div className="muted">only {j.electionFilter}</div>}
              </td>
              <td><When iso={j.requestedAt} /> <span className="muted">by {j.requestedBy}</span></td>
              <td>{j.status === "queued" ? "" : duration(j.startedAt, j.finishedAt)}</td>
              <td>
                {j.captureId && j.kind !== "normalize" && <div><CaptureLink id={j.captureId} /></div>}
                {j.passId && runsForPass(j.passId).map(r => (
                  <div key={r.runId}><Link to={`/runs/${r.runId}`}>run {r.runId}</Link> {r.electionDate} {r.electionType}</div>
                ))}
                <ErrorText text={j.errorText} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
