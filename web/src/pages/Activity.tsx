import { Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from "@mui/material";
import { useApi, type ActivityData, type JobsData } from "../api";
import { CaptureLink, Empty, ErrorText, PageHeader, Pending, StatusText, To, When, bytes, duration, kindLabel, runType } from "../ui";

const small = { fontSize: "0.8rem" };

export function Activity() {
  const jobs = useApi<JobsData>("/jobs", 2000);
  const activity = useApi<ActivityData>("/activity", 5000);

  return (
    <>
      <PageHeader crumbs={[["National – US States", "/"]]} title="Activity" />

      <Typography variant="h2" sx={{ mt: 0 }}>Runs started here</Typography>
      <Typography color="text.secondary" sx={{ mb: 1.5 }}>Jobs run one at a time, oldest first. A run made from the CLI has no job and appears only in the tables below.</Typography>
      {!jobs.data ? <Pending query={jobs} /> : jobs.data.jobs.length === 0 ? <Empty>Nothing has been started from the console yet.</Empty> : (
        <Box sx={{ overflowX: "auto" }}>
          <Table size="small">
            <TableHead><TableRow><TableCell>Job</TableCell><TableCell>Status</TableCell><TableCell>State</TableCell><TableCell>What</TableCell><TableCell>Requested</TableCell><TableCell>Duration</TableCell><TableCell>Result</TableCell></TableRow></TableHead>
            <TableBody>
              {jobs.data.jobs.map(j => (
                <TableRow key={j.jobId} hover>
                  <TableCell>{j.jobId}</TableCell>
                  <TableCell><StatusText status={j.status} /></TableCell>
                  <TableCell><To to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></To> {j.year}</TableCell>
                  <TableCell>
                    {kindLabel[j.kind]}{j.normalizeCaptureId && ` retrieve ${j.normalizeCaptureId}`}
                    {j.electionFilter && <Typography color="text.secondary" sx={small}>only {j.electionFilter}</Typography>}
                  </TableCell>
                  <TableCell sx={small}><When iso={j.requestedAt} /><br />by {j.requestedBy}</TableCell>
                  <TableCell>{j.startedAt ? duration(j.startedAt, j.finishedAt) : ""}</TableCell>
                  <TableCell sx={small}>
                    {j.captureId !== null && <div><CaptureLink id={j.captureId} /></div>}
                    {jobs.data.runs.filter(r => r.passId === j.passId).map(r => (
                      <div key={r.runId}><To to={`/runs/${r.runId}`}>Run {r.runId}</To> {r.electionDate} {r.electionType}</div>
                    ))}
                    <ErrorText text={j.errorText} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      )}

      {!activity.data ? <Pending query={activity} /> : (
        <>
          <Typography variant="h2">Retrieves</Typography>
          <Typography color="text.secondary" sx={{ mb: 1.5 }}>The 50 most recent across every state, from the console or the CLI.</Typography>
          {activity.data.captures.length === 0 ? <Empty>Nothing retrieved yet.</Empty> : (
            <Box sx={{ overflowX: "auto" }}>
              <Table size="small">
                <TableHead><TableRow><TableCell>Retrieve</TableCell><TableCell>Status</TableCell><TableCell>State</TableCell><TableCell>Run Type</TableCell><TableCell>Started</TableCell><TableCell>Duration</TableCell><TableCell>Fetched</TableCell><TableCell>By</TableCell></TableRow></TableHead>
                <TableBody>
                  {activity.data.captures.map(c => (
                    <TableRow key={c.captureId} hover>
                      <TableCell><CaptureLink id={c.captureId} /></TableCell>
                      <TableCell><StatusText status={c.status} /><ErrorText text={c.errorText} /></TableCell>
                      <TableCell><To to={`/states/${c.stateCode}`}><b>{c.stateCode}</b></To> {c.year}</TableCell>
                      <TableCell>{runType(c.source)}</TableCell>
                      <TableCell sx={small}><When iso={c.startedAt} /></TableCell>
                      <TableCell>{duration(c.startedAt, c.finishedAt)}</TableCell>
                      <TableCell sx={small}>{c.fetchCount} requests, {bytes(c.rawBytes)}</TableCell>
                      <TableCell>{c.requestedBy}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}

          <Typography variant="h2">Normalizations</Typography>
          <Typography color="text.secondary" sx={{ mb: 1.5 }}>The 50 most recent.</Typography>
          {activity.data.passes.length === 0 ? <Empty>Nothing normalized yet.</Empty> : (
            <Box sx={{ overflowX: "auto" }}>
              <Table size="small">
                <TableHead><TableRow><TableCell>Pass</TableCell><TableCell>Status</TableCell><TableCell>State</TableCell><TableCell>Of</TableCell><TableCell>Run Type</TableCell><TableCell>Started</TableCell><TableCell>Duration</TableCell><TableCell>Runs</TableCell><TableCell>By</TableCell></TableRow></TableHead>
                <TableBody>
                  {activity.data.passes.map(p => (
                    <TableRow key={p.passId} hover>
                      <TableCell>Pass {p.passId}</TableCell>
                      <TableCell><StatusText status={p.status} /></TableCell>
                      <TableCell><To to={`/states/${p.stateCode}`}><b>{p.stateCode}</b></To> {p.year}</TableCell>
                      <TableCell><CaptureLink id={p.captureId} /></TableCell>
                      <TableCell>{runType(p.source)}</TableCell>
                      <TableCell sx={small}><When iso={p.requestedAt} /></TableCell>
                      <TableCell>{duration(p.requestedAt, p.finishedAt)}</TableCell>
                      <TableCell sx={small}>
                        {activity.data.runs.filter(r => r.passId === p.passId).map(r => (
                          <div key={r.runId}><To to={`/runs/${r.runId}`}>Run {r.runId}</To> {r.electionDate} {r.electionType}</div>
                        ))}
                        <ErrorText text={p.errorText} />
                      </TableCell>
                      <TableCell>{p.requestedBy}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
        </>
      )}
    </>
  );
}
