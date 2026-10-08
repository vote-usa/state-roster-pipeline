import { createContext, useContext, type ReactNode } from "react";
import { Link } from "react-router-dom";
import type { UseQueryResult } from "@tanstack/react-query";
import type { RunKind, Source, Status } from "./api";

export function StatusBadge({ status }: { status: Status | "pending" | "pruned" }) {
  return <span className={`badge ${status}`}>{status}</span>;
}

export function ago(iso: string | null): string {
  if (!iso) return "";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h ago`;
  return `${Math.round(seconds / 86400)}d ago`;
}

export function When({ iso }: { iso: string | null }) {
  if (!iso) return <span className="muted">not yet</span>;
  return <span title={iso}>{iso.slice(0, 16).replace("T", " ")} UTC <span className="muted">({ago(iso)})</span></span>;
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

// The console says "retrieve" for what the pipeline and its tables call a capture.
export function CaptureLink({ id }: { id: number | null }) {
  return id === null ? <span className="muted">no retrieve</span> : <Link to={`/retrieves/${id}`}>retrieve {id}</Link>;
}

// The source groups a pass reports, as the two URL columns show them. Overview is where a person
// would look (the election index, and the verification-only link). Roster is what the collector parses.
const OVERVIEW: Record<string, string> = { elections: "", verification_only: "check" };
const ROSTER: Record<string, string> = { statewide_candidates: "C", statewide_measures: "M", local_measures: "M" };

function shortUrl(url: string): string {
  const short = url.replace(/^https?:\/\/(www\.)?/, "");
  return short.length > 34 ? `${short.slice(0, 33)}…` : short;
}

export function SourceLinks({ sources, kind }: { sources: Source[]; kind: "overview" | "roster" }) {
  const labels = kind === "overview" ? OVERVIEW : ROSTER;
  const seen = new Set<string>();
  const shown = sources.filter(s => s.group in labels && s.url.startsWith("http") && !seen.has(s.url) && seen.add(s.url));
  if (shown.length === 0) return <span className="muted">none recorded</span>;
  return (
    <>
      {shown.map(s => (
        <div key={s.url} className="source">
          {labels[s.group] && <span className="muted">{labels[s.group]}: </span>}
          <a href={s.url} target="_blank" rel="noreferrer" title={s.url}>{shortUrl(s.url)}</a>
          {kind === "roster" && s.format && <span className="format">{s.format}</span>}
        </div>
      ))}
    </>
  );
}

/** One pipeline stage's latest outcome as a small icon. The detail is in the tooltip. */
export function Stage({ label, status, detail }: { label: string; status: Status | null; detail: string }) {
  const glyph = status === "succeeded" ? "✓" : status === "failed" ? "✕" : status === null ? "–" : "…";
  return <span className={`stage ${status ?? "none"}`} title={`${label}: ${detail}`}>{glyph}</span>;
}

export const kindLabel: Record<RunKind, string> = {
  both: "Retrieve and normalize",
  capture: "Retrieve only",
  normalize: "Normalize",
};

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

export function ErrorText({ text }: { text: string | null }) {
  return text ? <pre className="error">{text}</pre> : null;
}

/** What a page shows while its data has not arrived, or when the request failed. */
export function Pending({ query }: { query: UseQueryResult<unknown, Error> }) {
  return query.error ? <pre className="error">{query.error.message}</pre> : <Empty>Loading…</Empty>;
}

export function Pager({ offset, limit, total, onChange }: { offset: number; limit: number; total: number; onChange: (offset: number) => void }) {
  if (total <= limit) return null;
  return (
    <div className="pager">
      <button disabled={offset === 0} onClick={() => onChange(Math.max(0, offset - limit))}>Previous</button>
      <span className="muted">{count(offset + 1)} to {count(Math.min(total, offset + limit))} of {count(total)}</span>
      <button disabled={offset + limit >= total} onClick={() => onChange(offset + limit)}>Next</button>
    </div>
  );
}

export interface StartRunPreset { stateCode?: string; normalizeCaptureId?: number }
export const StartRunContext = createContext<(preset: StartRunPreset) => void>(() => {});
export const useStartRun = () => useContext(StartRunContext);
