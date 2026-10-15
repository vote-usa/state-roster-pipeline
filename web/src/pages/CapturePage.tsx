import { useParams } from "react-router-dom";
import { Box, Button, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from "@mui/material";
import { useApi, type CaptureDetail } from "../api";
import { Empty, ErrorText, Facts, Log, PageHeader, Pending, StatusText, To, bytes, count, duration, runType, useStartRun } from "../ui";

const mono = { fontFamily: "ui-monospace, Menlo, monospace", fontSize: "0.75rem" };

export function CapturePage() {
  const { id = "" } = useParams();
  const startRun = useStartRun();
  const detail = useApi<CaptureDetail>(`/captures/${id}`, data => (data?.capture.status === "running" ? 2000 : false));
  if (!detail.data) return <Pending query={detail} />;

  const { capture: c, fetches, passes, runs } = detail.data;
  const canNormalize = c.status === "succeeded" && c.filesPresent;

  return (
    <>
      <PageHeader
        crumbs={[["National – US States", "/"], [c.stateCode, `/states/${c.stateCode}`]]}
        title={<>Retrieve {c.captureId} <StatusText status={c.status} /></>}
        actions={canNormalize && <Button variant="contained" size="large" onClick={() => startRun({ stateCode: c.stateCode, normalizeCaptureId: c.captureId })}>Normalize this retrieve</Button>}
      />

      <Paper variant="outlined" sx={{ p: 2.5 }}>
        <Facts items={[
          ["State and Year", `${c.stateCode} ${c.year}${c.wayback ? `, via Wayback ${c.wayback}` : ""}`],
          ["Run Type", runType(c.source)],
          ["Start Date", c.startedAt.slice(0, 16).replace("T", " ")],
          ["End Date", c.finishedAt ? c.finishedAt.slice(0, 16).replace("T", " ") : "running"],
          ["Duration", duration(c.startedAt, c.finishedAt)],
          ["Fetched", `${c.fetchCount} requests, ${bytes(c.rawBytes)}`],
          ["Requested By", c.requestedBy],
          ["Payloads", c.filesPresent ? `on disk at data/${c.rawDir}` : c.status === "running" ? "being written" : "not on disk, so this retrieve cannot be normalized"],
        ]} />
        <ErrorText text={c.errorText} />
      </Paper>

      <Typography variant="h2">Normalizations of this retrieve</Typography>
      {passes.length === 0 ? <Empty>None yet.</Empty> : (
        <Table size="small">
          <TableHead><TableRow><TableCell>Pass</TableCell><TableCell>Run Type</TableCell><TableCell>Status</TableCell><TableCell>Date Created</TableCell><TableCell>Runs</TableCell><TableCell align="right">Gaps</TableCell></TableRow></TableHead>
          <TableBody>
            {passes.map(p => (
              <TableRow key={p.passId}>
                <TableCell>Pass {p.passId}</TableCell>
                <TableCell>{runType(p.source)}</TableCell>
                <TableCell><StatusText status={p.status} /></TableCell>
                <TableCell>{p.requestedAt.slice(0, 16).replace("T", " ")}</TableCell>
                <TableCell>
                  {runs.filter(r => r.passId === p.passId).map(r => (
                    <Box key={r.runId}><To to={`/runs/${r.runId}`}>Run {r.runId} – {r.electionDate} {r.electionType}</To> <Typography component="span" color="text.secondary" sx={{ fontSize: "0.8rem" }}>{count(r.candidateCount)} candidates</Typography></Box>
                  ))}
                  <ErrorText text={p.errorText} />
                </TableCell>
                <TableCell align="right">{p.gaps.length}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <Typography variant="h2">Fetches</Typography>
      {fetches.length === 0 ? <Empty>{c.status === "running" ? "Fetch records are stored when the retrieve finishes." : "No fetches recorded."}</Empty> : (
        <Box sx={{ overflowX: "auto" }}>
          <Table size="small">
            <TableHead><TableRow><TableCell align="right">#</TableCell><TableCell>Role</TableCell><TableCell>Keys</TableCell><TableCell>Request</TableCell><TableCell align="right">HTTP</TableCell><TableCell align="right">Size</TableCell><TableCell align="right">Time</TableCell></TableRow></TableHead>
            <TableBody>
              {fetches.map(f => (
                <TableRow key={f.seq} hover>
                  <TableCell align="right">{f.seq}</TableCell>
                  <TableCell>{f.role}</TableCell>
                  <TableCell sx={mono}>{f.keys && Object.entries(f.keys).map(([k, v]) => `${k}=${v}`).join(" ")}</TableCell>
                  <TableCell sx={{ ...mono, overflowWrap: "anywhere" }}>{f.method} {f.url}{f.error && <Typography color="error" sx={{ fontSize: "inherit" }}>{f.error}</Typography>}</TableCell>
                  <TableCell align="right" sx={{ color: f.status !== null && f.status < 400 ? undefined : "error.main" }}>{f.status ?? "none"}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>{bytes(f.bytes)}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>{f.durationMs} ms</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      )}

      <Typography variant="h2">Log</Typography>
      <Log text={c.logText ?? "No log yet."} />
    </>
  );
}
