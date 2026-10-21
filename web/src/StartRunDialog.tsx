import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, FormControlLabel, FormLabel, InputLabel, MenuItem, Radio, RadioGroup, Select, TextField, Typography } from "@mui/material";
import { startJob, useApi, type RunKind, type StateDetail, type StateSummary } from "./api";
import { Pending, ago, bytes, kindLabel, type StartRunPreset } from "./ui";

const NAME_KEY = "roster-console.requested-by";

function savedName(): string {
  try { return localStorage.getItem(NAME_KEY) ?? ""; } catch { return ""; }
}

const KINDS: [RunKind, string][] = [
  ["both", "Fetch every source page, then build one run per election from it."],
  ["capture", "Fetch and save the source pages. Nothing is parsed yet."],
  ["normalize", "Rebuild runs from saved pages. No network, and the year comes from the retrieve."],
];

export function StartRunDialog({ preset, onClose }: { preset: StartRunPreset; onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const states = useApi<StateSummary[]>("/states");
  const implemented = (states.data ?? []).filter(s => s.implemented);
  const [picked, setPicked] = useState(preset.stateCode);
  const stateCode = picked ?? implemented[0]?.code ?? "";
  const [year, setYear] = useState(new Date().getUTCFullYear());
  const [kind, setKind] = useState<RunKind>(preset.normalizeCaptureId ? "normalize" : "both");
  const [captureId, setCaptureId] = useState<number | null>(preset.normalizeCaptureId ?? null);
  const [electionFilter, setElectionFilter] = useState("");
  const [requestedBy, setRequestedBy] = useState(savedName);
  const [sending, setSending] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const detail = useApi<StateDetail>(`/states/${stateCode || "none"}`);

  // Only a succeeded retrieve whose payloads are still on disk can be normalized.
  const usable = (detail.data?.code === stateCode ? detail.data.captures : []).filter(c => c.status === "succeeded" && c.filesPresent);
  const chosen = usable.find(c => c.captureId === captureId) ?? usable[0];
  const name = requestedBy.trim();
  const blocked = sending || name === "" || stateCode === "" || (kind === "normalize" && !chosen);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSending(true);
    setRefusal(null);
    try { localStorage.setItem(NAME_KEY, name); } catch { /* the name is only a convenience */ }
    try {
      await startJob({
        stateCode,
        year: kind === "normalize" ? chosen!.year : year,
        kind,
        normalizeCaptureId: kind === "normalize" ? chosen!.captureId : null,
        electionFilter: kind !== "capture" && electionFilter ? electionFilter : null,
        requestedBy: name,
      });
      await queryClient.invalidateQueries({ queryKey: ["/jobs"] });
      onClose();
      navigate("/activity");
    } catch (error) {
      setRefusal(error instanceof Error ? error.message : String(error));
      setSending(false);
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth slotProps={{ paper: { component: "form", onSubmit: submit } as never }}>
      <DialogTitle sx={{ color: "secondary.main", fontWeight: 500, fontSize: "1.4rem" }}>Start a run</DialogTitle>
      <DialogContent>
        {!states.data ? <Pending query={states} /> : (
          <>
            <Box sx={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: 2, mt: 1 }}>
              <FormControl fullWidth>
                <InputLabel id="run-state">State</InputLabel>
                <Select labelId="run-state" label="State" value={stateCode} onChange={e => { setPicked(e.target.value); setCaptureId(null); }}>
                  {implemented.map(s => <MenuItem key={s.code} value={s.code}>{s.name}</MenuItem>)}
                </Select>
              </FormControl>
              <TextField label="Year" type="number" value={kind === "normalize" && chosen ? chosen.year : year}
                disabled={kind === "normalize"} onChange={e => setYear(Number(e.target.value))} />
            </Box>

            <FormControl sx={{ mt: 3, width: "100%" }}>
              <FormLabel sx={{ fontSize: "0.8rem", mb: 0.5 }}>What to run</FormLabel>
              <RadioGroup value={kind} onChange={e => setKind(e.target.value as RunKind)}>
                {KINDS.map(([value, help]) => (
                  <FormControlLabel key={value} value={value} control={<Radio />} sx={{ alignItems: "flex-start", mb: 0.5, "& .MuiRadio-root": { pt: 0.5 } }}
                    label={<Box sx={{ pt: 0.5 }}>
                      <Typography sx={{ fontWeight: 500 }}>{value === "normalize" ? "Normalize an existing retrieve" : kindLabel[value]}</Typography>
                      <Typography color="text.secondary" sx={{ fontSize: "0.8rem" }}>{help}</Typography>
                    </Box>} />
                ))}
              </RadioGroup>
              {kind === "normalize" && (usable.length === 0
                ? <Alert severity="warning" sx={{ ml: 4 }}>{detail.data ? `${stateCode} has no succeeded retrieve with its payloads still on disk.` : "Loading retrieves…"}</Alert>
                : <Select size="small" sx={{ ml: 4 }} value={chosen!.captureId} onChange={e => setCaptureId(Number(e.target.value))}>
                    {usable.map(c => (
                      <MenuItem key={c.captureId} value={c.captureId}>Retrieve {c.captureId}, {c.year}, {ago(c.startedAt)}, {c.fetchCount} fetches, {bytes(c.rawBytes)}</MenuItem>
                    ))}
                  </Select>)}
            </FormControl>

            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2, mt: 3 }}>
              <TextField label="Only this election (optional)" type="date" value={electionFilter} disabled={kind === "capture"}
                onChange={e => setElectionFilter(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
              <TextField label="Your name" helperText="Recorded on the run" value={requestedBy} onChange={e => setRequestedBy(e.target.value)} />
            </Box>

            {refusal && <Alert severity="error" sx={{ mt: 2 }}>{refusal}</Alert>}
          </>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose}>Cancel</Button>
        <Button type="submit" variant="contained" disabled={blocked}>{sending ? "Starting…" : "Start"}</Button>
      </DialogActions>
    </Dialog>
  );
}
