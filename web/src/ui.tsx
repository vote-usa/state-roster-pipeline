import { createContext, useContext, type ReactNode } from "react";
import { Link as RouterLink } from "react-router-dom";
import type { UseQueryResult } from "@tanstack/react-query";
import { Alert, Box, Breadcrumbs, CircularProgress, Link, Stack, TablePagination, Tooltip, Typography } from "@mui/material";
import CheckCircleOutline from "@mui/icons-material/CheckCircleOutlineOutlined";
import HighlightOff from "@mui/icons-material/HighlightOff";
import RadioButtonUnchecked from "@mui/icons-material/RadioButtonUnchecked";
import PauseCircleOutline from "@mui/icons-material/PauseCircleOutlineOutlined";
import Schedule from "@mui/icons-material/Schedule";
import PictureAsPdfOutlined from "@mui/icons-material/PictureAsPdfOutlined";
import GridOnOutlined from "@mui/icons-material/GridOnOutlined";
import DataObject from "@mui/icons-material/DataObject";
import DescriptionOutlined from "@mui/icons-material/DescriptionOutlined";
import type { RunKind, Source, Status } from "./api";

type Shown = Status | "pending" | "pruned";

const STATUS: Record<Shown, { label: string; color: string; icon: ReactNode }> = {
  succeeded: { label: "Success", color: "success.main", icon: <CheckCircleOutline fontSize="inherit" /> },
  failed: { label: "Failed", color: "error.main", icon: <HighlightOff fontSize="inherit" /> },
  running: { label: "Running", color: "info.main", icon: <CircularProgress size="0.9em" color="inherit" thickness={6} /> },
  queued: { label: "Queued", color: "warning.main", icon: <Schedule fontSize="inherit" /> },
  pending: { label: "Pending at source", color: "warning.main", icon: <PauseCircleOutline fontSize="inherit" /> },
  pruned: { label: "Payloads pruned", color: "text.secondary", icon: <RadioButtonUnchecked fontSize="inherit" /> },
};

/** A status the way the wireframe draws it: an icon and a bold coloured word, with an optional line under it. */
export function StatusText({ status, children }: { status: Shown; children?: ReactNode }) {
  const s = STATUS[status];
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: "flex-start", display: "inline-flex", verticalAlign: "top" }}>
      <Box sx={{ color: s.color, fontSize: "1.15rem", lineHeight: 1, mt: "1px", display: "flex" }}>{s.icon}</Box>
      <Box sx={{ fontSize: "0.8rem", lineHeight: 1.25 }}>
        <Typography component="span" sx={{ color: s.color, fontWeight: 600, fontSize: "0.8rem" }}>{s.label}</Typography>
        {children && <Typography component="div" sx={{ fontSize: "0.8rem" }}>{children}</Typography>}
      </Box>
    </Stack>
  );
}

export function ago(iso: string | null): string {
  if (!iso) return "";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)} hours ago`;
  const days = Math.round(seconds / 86400);
  return days === 1 ? "1 day ago" : days < 14 ? `${days} days ago` : `${Math.round(days / 7)} weeks ago`;
}

export function When({ iso }: { iso: string | null }) {
  if (!iso) return <Typography component="span" color="text.secondary">not yet</Typography>;
  return (
    <Tooltip title={iso}>
      <span>{iso.slice(0, 16).replace("T", " ")} UTC <Typography component="span" color="text.secondary" sx={{ fontSize: "inherit" }}>({ago(iso)})</Typography></span>
    </Tooltip>
  );
}

export function duration(from: string | null, to: string | null): string {
  if (!from) return "";
  const seconds = Math.max(0, Math.round(((to ? new Date(to).getTime() : Date.now()) - new Date(from).getTime()) / 1000));
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

export function bytes(n: number | null): string {
  if (n === null) return "";
  if (n < 1024) return `${n} B`;
  if (n < 1048576) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / 1048576).toFixed(1)} MB`;
}

export const count = (n: number) => n.toLocaleString("en-US");

/** A link inside the console. */
export function To({ to, children, sx }: { to: string; children: ReactNode; sx?: object }) {
  return <Link component={RouterLink} to={to} sx={sx}>{children}</Link>;
}

// The console says "retrieve" for what the pipeline and its tables call a capture.
export function CaptureLink({ id }: { id: number | null }) {
  return id === null ? <Typography component="span" color="text.secondary">no retrieve</Typography> : <To to={`/retrieves/${id}`}>retrieve {id}</To>;
}

// The source groups a pass reports, as the two URL columns show them. Overview is where a person
// would look (the election index, and the verification-only link). Roster is what the collector parses.
const OVERVIEW: Record<string, string> = { elections: "", verification_only: "" };
const ROSTER: Record<string, string> = { statewide_candidates: "C", statewide_measures: "M", local_measures: "M" };

function shortUrl(url: string): string {
  const short = url.replace(/^https?:\/\/(www\.)?/, "");
  return short.length > 30 ? `${short.slice(0, 29)}…` : short;
}

function FormatIcon({ format }: { format: string }) {
  const f = format.toLowerCase();
  const icon = f.includes("pdf") ? <PictureAsPdfOutlined fontSize="inherit" />
    : f.includes("csv") || f.includes("xls") ? <GridOnOutlined fontSize="inherit" />
    : f.includes("json") ? <DataObject fontSize="inherit" />
    : <DescriptionOutlined fontSize="inherit" />;
  return <Tooltip title={format.toUpperCase()}><Box component="span" sx={{ color: "text.primary", fontSize: "1rem", ml: 0.5, display: "inline-flex", verticalAlign: "-3px" }}>{icon}</Box></Tooltip>;
}

export function SourceLinks({ sources, kind }: { sources: Source[]; kind: "overview" | "roster" }) {
  const labels = kind === "overview" ? OVERVIEW : ROSTER;
  const seen = new Set<string>();
  const shown = sources.filter(s => s.group in labels && s.url.startsWith("http") && !seen.has(s.url) && seen.add(s.url));
  if (shown.length === 0) return null;
  return (
    <>
      {shown.map(s => (
        <Box key={s.url} sx={{ whiteSpace: "nowrap", fontSize: "0.8rem", lineHeight: 1.7 }}>
          {labels[s.group] && <Box component="span" sx={{ color: "primary.main" }}>{labels[s.group]}: </Box>}
          <Tooltip title={s.group === "verification_only" ? `${s.url} (for visual verification only, never a data source)` : s.url}>
            <Link href={s.url} target="_blank" rel="noreferrer">{shortUrl(s.url)}</Link>
          </Tooltip>
          {kind === "roster" && s.format && <FormatIcon format={s.format} />}
        </Box>
      ))}
    </>
  );
}

/** One pipeline stage's latest outcome as a circled icon. The detail is in the tooltip. */
export function Stage({ label, status, detail }: { label: string; status: Status | null; detail: string }) {
  const icon = status === "succeeded" ? <CheckCircleOutline fontSize="inherit" />
    : status === "failed" ? <HighlightOff fontSize="inherit" />
    : status === null ? <RadioButtonUnchecked fontSize="inherit" />
    : <Schedule fontSize="inherit" />;
  const color = status === "succeeded" ? "success.main" : status === "failed" ? "error.main" : status === null ? "text.disabled" : "info.main";
  return (
    <Tooltip title={<><b>{label}</b><br />{detail}</>}>
      <Box component="span" sx={{ color, fontSize: "1.45rem", display: "inline-flex", mr: 0.25 }}>{icon}</Box>
    </Tooltip>
  );
}

/** Label-over-value pairs in a row, as the workflow wireframe lays out a run's facts. */
export function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <Stack direction="row" useFlexGap sx={{ flexWrap: "wrap", columnGap: 5, rowGap: 1.5 }}>
      {items.map(([label, value]) => (
        <Box key={label} sx={{ minWidth: 0 }}>
          <Typography sx={{ color: "primary.main", fontWeight: 500, fontSize: "0.75rem" }}>{label}</Typography>
          <Typography component="div" sx={{ fontSize: "0.85rem", overflowWrap: "anywhere" }}>{value}</Typography>
        </Box>
      ))}
    </Stack>
  );
}

/** Breadcrumbs, the page title and its actions. */
export function PageHeader({ crumbs, title, actions }: { crumbs?: [string, string][]; title: ReactNode; actions?: ReactNode }) {
  return (
    <Box sx={{ mb: 3 }}>
      {crumbs && (
        <Breadcrumbs sx={{ mb: 1, fontSize: "0.85rem" }}>
          {crumbs.map(([label, to]) => <To key={to} to={to}>{label}</To>)}
        </Breadcrumbs>
      )}
      <Stack direction="row" useFlexGap sx={{ alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 2 }}>
        <Typography variant="h1" sx={{ display: "flex", alignItems: "center", gap: 2 }}>{title}</Typography>
        {actions && <Stack direction="row" spacing={1}>{actions}</Stack>}
      </Stack>
    </Box>
  );
}

/** Who started a retrieve or a normalization. Nothing runs on a schedule yet. */
export const runType = (source: string) => (source === "web" ? "Console" : source === "cli" ? "CLI" : source);

export const kindLabel: Record<RunKind, string> = {
  both: "Retrieve and normalize",
  capture: "Retrieve only",
  normalize: "Normalize",
};

export function Empty({ children }: { children: ReactNode }) {
  return <Typography color="text.secondary" sx={{ py: 1.5 }}>{children}</Typography>;
}

export function ErrorText({ text }: { text: string | null }) {
  return text ? (
    <Alert severity="error" icon={false} sx={{ my: 1, "& .MuiAlert-message": { fontFamily: "ui-monospace, Menlo, monospace", fontSize: "0.75rem", whiteSpace: "pre-wrap", overflowWrap: "anywhere" } }}>
      {text}
    </Alert>
  ) : null;
}

export function Log({ text }: { text: string }) {
  return (
    <Box component="pre" sx={{ m: 0, p: 2, maxHeight: 520, overflow: "auto", bgcolor: "#f7f8fa", border: 1, borderColor: "divider", borderRadius: 1, fontSize: "0.75rem", fontFamily: "ui-monospace, Menlo, monospace", whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
      {text}
    </Box>
  );
}

/** What a page shows while its data has not arrived, or when the request failed. */
export function Pending({ query }: { query: UseQueryResult<unknown, Error> }) {
  return query.error
    ? <Alert severity="error" sx={{ my: 2 }}>{query.error.message}</Alert>
    : <Box sx={{ py: 4, display: "flex", justifyContent: "center" }}><CircularProgress size={28} /></Box>;
}

export function Pager({ offset, limit, total, onChange }: { offset: number; limit: number; total: number; onChange: (offset: number) => void }) {
  if (total <= limit) return null;
  return (
    <TablePagination component="div" count={total} page={Math.floor(offset / limit)} rowsPerPage={limit} rowsPerPageOptions={[]}
      onPageChange={(_, page) => onChange(page * limit)} />
  );
}

export interface StartRunPreset { stateCode?: string; normalizeCaptureId?: number }
export const StartRunContext = createContext<(preset: StartRunPreset) => void>(() => {});
export const useStartRun = () => useContext(StartRunContext);
