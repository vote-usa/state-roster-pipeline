import { useParams } from "react-router-dom";
import { Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from "@mui/material";
import { electionsFrom, useApi, type StateDetail } from "../api";
import { CaptureLink, Empty, PageHeader, Pending, To, When, count, runType } from "../ui";

function Delta({ value, previous }: { value: number; previous: number | undefined }) {
  const change = previous === undefined ? 0 : value - previous;
  return (
    <TableCell align="right">
      {count(value)}
      {change !== 0 && <Typography component="span" sx={{ fontSize: "inherit", color: change > 0 ? "success.main" : "error.main" }}> {change > 0 ? "+" : ""}{count(change)}</Typography>}
    </TableCell>
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
      <PageHeader crumbs={[["National – US States", "/"], [state.data.name, `/states/${code}`]]} title={`${latest.electionDate} ${latest.electionType}`} />
      <Typography color="text.secondary" sx={{ mt: -2, mb: 3 }}>{latest.electionName}. Source election id {latest.sourceElectionId || "none"}.</Typography>

      <Typography variant="h2">Run history</Typography>
      <Typography color="text.secondary" sx={{ mb: 1.5 }}>Each normalization stores a new run and never changes an old one. Changes are against the run before it.</Typography>
      <Box sx={{ overflowX: "auto" }}>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Run</TableCell><TableCell>Run Type</TableCell><TableCell>Date Created</TableCell><TableCell>From</TableCell>
              <TableCell align="right">Candidates</TableCell><TableCell align="right">Measures</TableCell><TableCell align="right">County ballots</TableCell><TableCell align="right">Gaps</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {runs.map((r, i) => {
              const before = runs[i + 1];
              const p = passes.get(r.passId);
              return (
                <TableRow key={r.runId} hover>
                  <TableCell><To to={`/runs/${r.runId}`}><b>Run {r.runId}</b></To>{i === 0 && <Typography component="span" color="text.secondary"> latest</Typography>}</TableCell>
                  <TableCell>{p ? runType(p.source) : ""}</TableCell>
                  <TableCell><When iso={r.createdAt} /></TableCell>
                  <TableCell>pass {r.passId}{p && <> of <CaptureLink id={p.captureId} /></>}</TableCell>
                  <Delta value={r.candidateCount} previous={before?.candidateCount} />
                  <Delta value={r.measureCount} previous={before?.measureCount} />
                  <Delta value={r.countyBallotCount} previous={before?.countyBallotCount} />
                  <TableCell align="right">{p?.gaps.length ?? ""}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </Box>
    </>
  );
}
