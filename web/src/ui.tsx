import { createContext, useContext, type ReactNode } from "react";
import { Link } from "react-router-dom";
import type { UseQueryResult } from "@tanstack/react-query";
import type { RunKind, Status } from "./api";

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

export function CaptureLink({ id }: { id: number | null }) {
  return id === null ? <span className="muted">no capture</span> : <Link to={`/captures/${id}`}>capture {id}</Link>;
}

export const kindLabel: Record<RunKind, string> = {
  both: "Capture and normalize",
  capture: "Capture only",
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
