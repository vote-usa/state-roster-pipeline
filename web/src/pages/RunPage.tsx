import { useState } from "react";
import { useParams } from "react-router-dom";
import { Box, FormControl, InputAdornment, Link, MenuItem, Paper, Select, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from "@mui/material";
import SearchIcon from "@mui/icons-material/Search";
import { electionKey, useApi, type Candidate, type CountyBallot, type Measure, type Page, type RunDetail } from "../api";
import { CaptureLink, Empty, Facts, Log, PageHeader, Pager, Pending, StatusText, count, runType } from "../ui";

type TabKey = "candidates" | "measures" | "ballots" | "sources" | "log";
const PAGE = 100;
const mono = { fontFamily: "ui-monospace, Menlo, monospace", fontSize: "0.75rem" };

export function RunPage() {
  const { id = "" } = useParams();
  const [tab, setTab] = useState<TabKey>("candidates");
  const [county, setCounty] = useState("");
  const detail = useApi<RunDetail>(`/runs/${id}`);
  const ballots = useApi<CountyBallot[]>(`/runs/${id}/county-ballots`);
  if (!detail.data) return <Pending query={detail} />;

  const { run: r, pass: p } = detail.data;
  const counties = ballots.data ?? [];

  return (
    <>
      <PageHeader
        crumbs={[["National – US States", "/"], [r.stateCode, `/states/${r.stateCode}`], [`${r.electionDate} ${r.electionType}`, `/states/${r.stateCode}/elections/${electionKey(r)}`]]}
        title={<>Run {r.runId} <StatusText status={r.isPending ? "pending" : p.status} /></>}
      />

      <Paper variant="outlined" sx={{ p: 2.5 }}>
        <Typography sx={{ fontSize: "1.6rem", fontWeight: 500, color: "secondary.main", mb: 1.5 }}>{r.electionName}</Typography>
        <Facts items={[
          ["Election Date", r.electionDate],
          ["Election Type", r.electionType],
          ["Run Type", runType(p.source)],
          ["Date Created", r.createdAt.slice(0, 16).replace("T", " ")],
          ["Built By", <>pass {p.passId} of <CaptureLink id={p.captureId} /></>],
          ["Requested By", p.requestedBy],
          ["Version", p.gitSha ? p.gitSha.slice(0, 7) : "unknown"],
          ["Pass Also Stored", `${count(p.countyDirectoryCount)} county offices, ${count(p.proposedMeasureCount)} proposed measures, ${count(p.unassignedRowCount)} unassigned rows`],
        ]} />
      </Paper>

      <Tabs value={tab} onChange={(_, value: TabKey) => setTab(value)} sx={{ mt: 3, mb: 2, borderBottom: 1, borderColor: "divider" }} variant="scrollable">
        <Tab value="candidates" label={`Candidates ${count(r.candidateCount)}`} />
        <Tab value="measures" label={`Measures ${count(r.measureCount)}`} />
        <Tab value="ballots" label={`County ballots ${count(r.countyBallotCount)}`} />
        <Tab value="sources" label={`Gaps ${p.gaps.length} and sources`} />
        <Tab value="log" label="Log" />
      </Tabs>

      {(tab === "candidates" || tab === "measures") && counties.length > 0 && (
        <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 2, flexWrap: "wrap" }}>
          <FormControl size="small" sx={{ minWidth: 200 }}>
            <Select value={county} displayEmpty onChange={e => setCounty(e.target.value)}>
              <MenuItem value="">All Counties</MenuItem>
              {counties.map(b => <MenuItem key={b.county} value={b.county}>{b.county}</MenuItem>)}
            </Select>
          </FormControl>
          {county && <Typography color="text.secondary" sx={{ fontSize: "0.85rem" }}>The {county} ballot as the source published it, statewide races included.</Typography>}
        </Box>
      )}
      {tab === "candidates" && <Candidates key={county} runId={r.runId} county={county} />}
      {tab === "measures" && <Measures key={county} runId={r.runId} county={county} />}
      {tab === "ballots" && (!ballots.data ? <Pending query={ballots} /> : <Ballots ballots={counties} onPick={c => { setCounty(c); setTab("candidates"); }} />)}

      {tab === "sources" && (
        <>
          <Typography variant="h3" sx={{ mb: 1 }}>Gaps</Typography>
          {p.gaps.length === 0 ? <Empty>None reported.</Empty> : p.gaps.map(g => <Typography key={g} color="error" sx={{ fontSize: "0.9rem" }}>{g}</Typography>)}
          {p.nextRun?.after && <Typography sx={{ my: 2, fontSize: "0.9rem" }}><b>Check again after {p.nextRun.after}.</b> {p.nextRun.reason}</Typography>}
          <Typography variant="h3" sx={{ mt: 3, mb: 1 }}>Sources</Typography>
          <Table size="small">
            <TableHead><TableRow><TableCell>Used for</TableCell><TableCell>URL</TableCell><TableCell>Format</TableCell><TableCell>Payload saved</TableCell></TableRow></TableHead>
            <TableBody>
              {p.sources.map((s, i) => (
                <TableRow key={i}>
                  <TableCell sx={{ textTransform: "capitalize" }}>{s.group.replace(/_/g, " ")}</TableCell>
                  <TableCell sx={{ ...mono, overflowWrap: "anywhere" }}>{s.url.startsWith("http") ? <Link href={s.url} target="_blank" rel="noreferrer">{s.url}</Link> : s.url}</TableCell>
                  <TableCell>{s.format}</TableCell>
                  <TableCell>{s.hashed ? "Yes" : <Typography component="span" color="text.secondary">No</Typography>}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </>
      )}

      {tab === "log" && <Log text={p.logText ?? p.summary ?? "No log stored."} />}
    </>
  );
}

function Candidates({ runId, county }: { runId: number; county: string }) {
  const [filter, setFilter] = useState("");
  const [offset, setOffset] = useState(0);
  const page = useApi<Page<Candidate>>(`/runs/${runId}/candidates?q=${encodeURIComponent(filter.trim())}&county=${encodeURIComponent(county)}&offset=${offset}&limit=${PAGE}`);

  return (
    <>
      <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 2, flexWrap: "wrap" }}>
        <TextField size="small" placeholder="Filter by office, name, county, party" value={filter} sx={{ width: 380, maxWidth: "100%" }}
          onChange={e => { setFilter(e.target.value); setOffset(0); }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        {page.data && <Typography color="text.secondary" sx={{ fontSize: "0.85rem" }}>{count(page.data.total)} {filter.trim() ? "matching" : "candidates"}</Typography>}
      </Box>
      {!page.data ? <Pending query={page} /> : page.data.total === 0 ? <Empty>{filter.trim() ? "Nothing matches." : county ? `No candidates on the ${county} ballot.` : "This run has no candidates."}</Empty> : (
        <>
          <Box sx={{ overflowX: "auto" }}>
            <Table size="small">
              <TableHead><TableRow><TableCell>Office</TableCell><TableCell>District</TableCell><TableCell>County</TableCell><TableCell>Candidate</TableCell><TableCell>Party</TableCell><TableCell>Filing Status</TableCell><TableCell>OCD Division</TableCell></TableRow></TableHead>
              <TableBody>
                {page.data.rows.map((c, i) => (
                  <TableRow key={offset + i} hover>
                    <TableCell>{c.office}</TableCell><TableCell>{c.district}</TableCell><TableCell>{c.county}</TableCell>
                    <TableCell sx={{ fontWeight: 500 }}>{c.name}</TableCell><TableCell>{c.party}</TableCell><TableCell>{c.status}</TableCell>
                    <TableCell sx={mono}>{c.ocdDivisionId ?? <Typography component="span" color="text.secondary" sx={{ fontSize: "inherit" }}>not mapped</Typography>}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
          <Pager offset={offset} limit={PAGE} total={page.data.total} onChange={setOffset} />
        </>
      )}
    </>
  );
}

function Measures({ runId, county }: { runId: number; county: string }) {
  const [offset, setOffset] = useState(0);
  const page = useApi<Page<Measure>>(`/runs/${runId}/measures?county=${encodeURIComponent(county)}&offset=${offset}&limit=${PAGE}`);
  if (!page.data) return <Pending query={page} />;
  if (page.data.total === 0) return <Empty>{county ? `No measures on the ${county} ballot.` : "This run has no measures."}</Empty>;

  return (
    <>
      <Table size="small">
        <TableHead><TableRow><TableCell>Measure</TableCell><TableCell>Title</TableCell><TableCell>Jurisdiction</TableCell><TableCell>County</TableCell></TableRow></TableHead>
        <TableBody>
          {page.data.rows.map((m, i) => (
            <TableRow key={offset + i} hover><TableCell>{m.measureId}</TableCell><TableCell>{m.title}</TableCell><TableCell>{m.jurisdiction}</TableCell><TableCell>{m.county}</TableCell></TableRow>
          ))}
        </TableBody>
      </Table>
      <Pager offset={offset} limit={PAGE} total={page.data.total} onChange={setOffset} />
    </>
  );
}

function Ballots({ ballots, onPick }: { ballots: CountyBallot[]; onPick: (county: string) => void }) {
  if (ballots.length === 0) return <Empty>This source does not publish county ballots.</Empty>;

  return (
    <Table size="small" sx={{ width: "auto", minWidth: 420 }}>
      <TableHead><TableRow><TableCell>County</TableCell><TableCell align="right">Candidates</TableCell><TableCell align="right">Measures</TableCell></TableRow></TableHead>
      <TableBody>
        {ballots.map(b => (
          <TableRow key={b.county} hover>
            <TableCell><Link component="button" onClick={() => onPick(b.county)}>{b.county}</Link></TableCell>
            <TableCell align="right">{count(b.candidateCount)}</TableCell><TableCell align="right">{count(b.measureCount)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

