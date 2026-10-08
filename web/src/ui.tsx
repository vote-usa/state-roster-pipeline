import { createContext, useContext, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { now, type Job, type Status } from "./data";

export function StatusBadge({ status }: { status: Status | "pending" | "pruned" }) {
  return <span className={`badge ${status}`}>{status}</span>;
}

export function ago(iso: string | null): string {
  if (!iso) return "";
  const seconds = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 1000));
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
  const seconds = Math.round(((to ? new Date(to) : now).getTime() - new Date(from).getTime()) / 1000);
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

export function bytes(n: number | null): string {
  if (n === null) return "";
  if (n < 1024) return `${n} B`;
  if (n < 1048576) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / 1048576).toFixed(1)} MB`;
}

export const count = (n: number) => n.toLocaleString("en-US");

export function CaptureLink({ id }: { id: number }) {
  return <Link to={`/captures/${id}`}>capture {id}</Link>;
}

export const kindLabel: Record<Job["kind"], string> = {
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

export interface StartRunPreset { stateCode?: string; normalizeCaptureId?: number }
export const StartRunContext = createContext<(preset: StartRunPreset) => void>(() => {});
export const useStartRun = () => useContext(StartRunContext);
