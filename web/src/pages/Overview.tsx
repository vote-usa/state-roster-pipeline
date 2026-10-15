import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, IconButton, InputAdornment, Link, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Tooltip, Typography } from "@mui/material";
import SearchIcon from "@mui/icons-material/Search";
import ExpandMore from "@mui/icons-material/ExpandMore";
import Sync from "@mui/icons-material/Sync";
import PlayCircleOutline from "@mui/icons-material/PlayCircleOutlineOutlined";
import { electionKey, isActive, useApi, type JobsData, type StateSummary } from "../api";
import { PageHeader, Pending, SourceLinks, Stage, StatusText, To, ago, count, duration, kindLabel, useStartRun } from "../ui";

function nextCheck(after: string | null | undefined): string | null {
  if (!after) return null;
  const days = Math.ceil((new Date(`${after}T00:00:00Z`).getTime() - Date.now()) / 86400000);
  return days > 1 ? `${days} days until next retrieve` : days === 1 ? "1 day until next retrieve" : `Due for a retrieve since ${after}`;
}

export function Overview() {
  const startRun = useStartRun();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const states = useApi<StateSummary[]>("/states", 5000);
  const active = (useApi<JobsData>("/jobs", 2000).data?.jobs ?? []).filter(isActive).reverse();

  const needle = search.trim().toLowerCase();
  const shown = [...(states.data ?? [])].sort((a, b) => a.name.localeCompare(b.name)).filter(s => needle === "" || s.name.toLowerCase().includes(needle) || s.code.toLowerCase() === needle);
  const implemented = shown.filter(s => s.implemented);
  const unimplemented = shown.filter(s => !s.implemented);

  return (
    <>
      <PageHeader
        title="National – US States"
        actions={
          <Tooltip title="Refresh">
            <IconButton onClick={() => queryClient.invalidateQueries()} sx={{ border: 1, borderColor: "divider", borderRadius: 1, width: 52, height: 52 }}>
              <Sync sx={{ fontSize: 30 }} />
            </IconButton>
          </Tooltip>
        }
      />

      <TextField
        placeholder="Filtering by State"
        value={search}
        onChange={e => setSearch(e.target.value)}
        sx={{ width: 640, maxWidth: "100%", mb: 2.5 }}
        slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment>, sx: { fontStyle: search ? "normal" : "italic" } } }}
      />

      <Stack direction="row" spacing={1.5} sx={{ mb: 2 }}>
        <Button variant="outlined" size="large" onClick={() => startRun({})}>Start a run</Button>
      </Stack>

      {active.length > 0 && (
        <Alert severity="info" icon={false} sx={{ mb: 2 }}>
          {active.map(j => (
            <Box key={j.jobId} sx={{ display: "flex", gap: 1, alignItems: "center" }}>
              <StatusText status={j.status} />
              <To to={`/states/${j.stateCode}`}><b>{j.stateCode}</b></To> {kindLabel[j.kind].toLowerCase()}
              {j.normalizeCaptureId && ` retrieve ${j.normalizeCaptureId}`}
              {j.status === "running" && <Typography component="span" color="text.secondary">, {duration(j.startedAt, null)} so far</Typography>}
            </Box>
          ))}
        </Alert>
      )}

      {!states.data ? <Pending query={states} /> : (
        <Box sx={{ overflowX: "auto" }}>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>State</TableCell>
                <TableCell>Overview URLs</TableCell>
                <TableCell>Roster URLs</TableCell>
                <TableCell>Latest Retrieve</TableCell>
                <TableCell>Elections</TableCell>
                <TableCell>Recent Executions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {implemented.map(s => <StateRow key={s.code} state={s} onStart={() => startRun({ stateCode: s.code })} />)}
            </TableBody>
          </Table>
          {shown.length === 0 && <Typography color="text.secondary" sx={{ py: 2 }}>No state matches “{search}”.</Typography>}
          {unimplemented.length > 0 && (
            <Accordion variant="outlined" disableGutters sx={{ mt: 4, "&::before": { display: "none" } }}>
              <AccordionSummary expandIcon={<ExpandMore />}>
                <Typography variant="h3">{unimplemented.length} {unimplemented.length === 1 ? "state" : "states"} with no collector yet</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Typography color="text.secondary" sx={{ lineHeight: 1.9 }}>{unimplemented.map(s => s.name).join(", ")}</Typography>
              </AccordionDetails>
            </Accordion>
          )}
        </Box>
      )}
    </>
  );
}

function StateRow({ state: s, onStart }: { state: StateSummary; onStart: () => void }) {
  const stateName = { fontSize: "1.35rem", fontWeight: 500, whiteSpace: "nowrap" };
  const next = nextCheck(s.nextRun?.after);
  return (
    <TableRow hover>
      <TableCell><To to={`/states/${s.code}`} sx={stateName}>{s.name}</To></TableCell>
      <TableCell><SourceLinks sources={s.sources} kind="overview" /></TableCell>
      <TableCell><SourceLinks sources={s.sources} kind="roster" /></TableCell>
      <TableCell sx={{ minWidth: 190 }}>
        {s.lastCapture
          ? <StatusText status={s.lastCapture.status}>{ago(s.lastCapture.startedAt)}</StatusText>
          : <Typography color="text.secondary" sx={{ fontSize: "0.8rem" }}>Never retrieved</Typography>}
        <Box sx={{ fontSize: "0.8rem", mt: 0.75, lineHeight: 1.7 }}>
          {s.lastCapture && <div><To to={`/retrieves/${s.lastCapture.captureId}`}>View Log</To></div>}
          {next && <Tooltip title={s.nextRun?.reason ?? ""}><div>{next}</div></Tooltip>}
          <Link component="button" onClick={onStart} sx={{ display: "inline-flex", alignItems: "center", gap: 0.5, fontSize: "inherit" }}>
            <PlayCircleOutline fontSize="small" /> Start Retrieve
          </Link>
        </Box>
      </TableCell>
      <TableCell sx={{ fontSize: "0.8rem" }}>
        {s.elections.map(e => (
          <Box key={e.runId} sx={{ whiteSpace: "nowrap", lineHeight: 1.7 }}>
            <To to={`/states/${s.code}/elections/${electionKey(e)}`}>{e.electionDate} {e.electionType}</To>
            <Typography component="span" color="text.secondary" sx={{ fontSize: "inherit" }}>
              {" "}{count(e.candidateCount)} cand.{e.measureCount > 0 && `, ${count(e.measureCount)} meas.`}{e.isPending && ", pending"}
            </Typography>
          </Box>
        ))}
      </TableCell>
      <TableCell>
        <Box sx={{ display: "flex", alignItems: "center" }}>
          <Stage label="Retrieve" status={s.lastCapture?.status ?? null}
            detail={s.lastCapture ? `Retrieve ${s.lastCapture.captureId}, ${s.lastCapture.status} ${ago(s.lastCapture.startedAt)}` : "Never run"} />
          <Stage label="Normalize" status={s.lastPass?.status ?? null}
            detail={s.lastPass ? `Pass ${s.lastPass.passId} of retrieve ${s.lastPass.captureId}, ${s.lastPass.status} ${ago(s.lastPass.requestedAt)}` : "Never run"} />
        </Box>
        {s.gapCount !== null && s.gapCount > 0 && <Typography color="error" sx={{ fontSize: "0.8rem", fontWeight: 600 }}>{s.gapCount} {s.gapCount === 1 ? "gap" : "gaps"}</Typography>}
        <Box sx={{ fontSize: "0.8rem", mt: 0.5 }}><To to={`/states/${s.code}`}>Show All Results</To></Box>
      </TableCell>
    </TableRow>
  );
}
