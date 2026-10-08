// Slice 0: everything is read from a fixture. Slice 1 replaces this module with API calls.
import { useSyncExternalStore } from "react";

export type Status = "queued" | "running" | "succeeded" | "failed";
export type JobKind = "capture" | "normalize" | "both";

export interface StateInfo { code: string; name: string; implemented: boolean }

export interface Capture {
  captureId: number; stateCode: string; year: number; status: Status; source: string; requestedBy: string;
  startedAt: string; finishedAt: string | null; wayback: string | null; rawDir: string | null;
  fetchCount: number; rawBytes: number; gitSha: string | null; logText: string | null; errorText: string | null;
  filesPresent: boolean;
}

export interface Fetch {
  seq: number; role: string | null; keys: Record<string, string> | null; method: string; url: string;
  status: number | null; contentType: string | null; bytes: number | null; durationMs: number; error: string | null;
}

export interface Source { group: string; url: string; format: string; hashed: boolean }

export interface Pass {
  passId: number; captureId: number; stateCode: string; year: number; status: Status; source: string;
  requestedBy: string; requestedAt: string; finishedAt: string | null; electionFilter: string | null; gitSha: string | null;
  runCount: number; candidateCount: number; measureCount: number; countyBallotCount: number;
  countyDirectoryCount: number; proposedMeasureCount: number; unassignedRowCount: number;
  gaps: string[]; nextRun: { after: string; reason: string } | null; sources: Source[];
  summary: string | null; logText: string | null; errorText: string | null;
}

export interface Run {
  runId: number; passId: number; stateCode: string; electionDate: string; electionType: string; electionName: string;
  sourceElectionId: string; isPending: boolean; candidateCount: number; measureCount: number; countyBallotCount: number;
  createdAt: string;
}

export interface Candidate {
  office: string; district: string | null; county: string | null; name: string; party: string | null;
  status: string | null; ocdDivisionId: string | null; sourceOfficeType: string | null;
}

export interface Measure { measureId: string; title: string | null; jurisdiction: string; county: string | null }
export interface CountyBallot { county: string; candidateCount: number; measureCount: number }

export interface Job {
  jobId: number; stateCode: string; year: number; kind: JobKind; normalizeCaptureId: number | null;
  electionFilter: string | null; status: Status; requestedBy: string; requestedAt: string;
  startedAt: string | null; finishedAt: string | null; captureId: number | null; passId: number | null;
  errorText: string | null;
}

interface Fixture {
  now: string; rowsPerRun: number; states: StateInfo[]; captures: Capture[]; fetches: Record<string, Fetch[]>;
  passes: Pass[]; runs: Run[]; candidates: Record<string, Candidate[]>; measures: Record<string, Measure[]>;
  countyBallots: Record<string, CountyBallot[]>; jobs: Job[];
}

import raw from "./fixture.json";
const fx = raw as unknown as Fixture;

export const now = new Date(fx.now);
export const rowsPerRun = fx.rowsPerRun;

const newestFirst = <T,>(rows: T[], id: (r: T) => number) => [...rows].sort((a, b) => id(b) - id(a));

export const states = () => fx.states;
export const stateInfo = (code: string) => fx.states.find(s => s.code === code);

export const capturesFor = (code: string) => newestFirst(fx.captures.filter(c => c.stateCode === code), c => c.captureId);
export const capture = (id: number) => fx.captures.find(c => c.captureId === id);
export const fetchesFor = (captureId: number) => fx.fetches[captureId] ?? [];

export const passesForState = (code: string) => newestFirst(fx.passes.filter(p => p.stateCode === code), p => p.passId);
export const passesForCapture = (captureId: number) => newestFirst(fx.passes.filter(p => p.captureId === captureId), p => p.passId);
export const pass = (id: number) => fx.passes.find(p => p.passId === id);

export const run = (id: number) => fx.runs.find(r => r.runId === id);
export const runsForPass = (passId: number) => fx.runs.filter(r => r.passId === passId);
export const candidatesFor = (runId: number) => fx.candidates[runId] ?? [];
export const measuresFor = (runId: number) => fx.measures[runId] ?? [];
export const countyBallotsFor = (runId: number) => fx.countyBallots[runId] ?? [];

// Two elections can share a date and a type (TX specials), so the source's election id is part of the key.
export const electionKey = (r: Run) => [r.electionDate, r.electionType, r.sourceElectionId].map(encodeURIComponent).join("~");

export interface Election { key: string; latest: Run; runs: Run[] }

export function electionsFor(code: string): Election[] {
  const byKey = new Map<string, Run[]>();
  for (const r of newestFirst(fx.runs.filter(r => r.stateCode === code), r => r.runId)) {
    const key = electionKey(r);
    byKey.set(key, [...(byKey.get(key) ?? []), r]);
  }
  return [...byKey.entries()]
    .map(([key, runs]) => ({ key, latest: runs[0], runs }))
    .sort((a, b) => a.latest.electionDate.localeCompare(b.latest.electionDate) || a.latest.runId - b.latest.runId);
}

// Jobs are the one thing the mock lets you change: starting a run adds a row and walks it through its statuses.
let jobs = newestFirst(fx.jobs, j => j.jobId);
const listeners = new Set<() => void>();
const setJobs = (next: Job[]) => { jobs = next; listeners.forEach(l => l()); };
const patchJob = (jobId: number, patch: Partial<Job>) => setJobs(jobs.map(j => (j.jobId === jobId ? { ...j, ...patch } : j)));

export function useJobs(): Job[] {
  return useSyncExternalStore(l => { listeners.add(l); return () => listeners.delete(l); }, () => jobs);
}

export interface StartJob {
  stateCode: string; year: number; kind: JobKind; normalizeCaptureId: number | null;
  electionFilter: string | null; requestedBy: string;
}

export function startJob(request: StartJob): number {
  const jobId = Math.max(0, ...jobs.map(j => j.jobId)) + 1;
  const stamp = () => new Date(now.getTime() + performance.now()).toISOString();
  setJobs([{ ...request, jobId, status: "queued", requestedAt: stamp(), startedAt: null, finishedAt: null,
    captureId: request.normalizeCaptureId, passId: null, errorText: null }, ...jobs]);
  setTimeout(() => patchJob(jobId, { status: "running", startedAt: stamp() }), 1500);
  setTimeout(() => patchJob(jobId, { status: "succeeded", finishedAt: stamp() }), 6000);
  return jobId;
}
