import { keepPreviousData, useQuery } from "@tanstack/react-query";

export type Status = "queued" | "running" | "succeeded" | "failed";
export type RunKind = "capture" | "normalize" | "both";

export interface Capture {
  captureId: number; stateCode: string; year: number; status: Status; source: string; requestedBy: string;
  startedAt: string; finishedAt: string | null; wayback: string | null; rawDir: string | null;
  fetchCount: number; rawBytes: number; gitSha: string | null; logText: string | null; errorText: string | null;
  filesPresent: boolean;
}

export interface Fetch {
  seq: number; role: string | null; keys: Record<string, unknown> | null; method: string; url: string;
  status: number | null; contentType: string | null; bytes: number | null; durationMs: number; error: string | null;
}

export interface Source { group: string; url: string; format: string; hashed: boolean }
export interface NextRun { after: string | null; reason: string | null }

export interface Pass {
  passId: number; captureId: number | null; stateCode: string; year: number; status: Status; source: string;
  requestedBy: string; requestedAt: string; finishedAt: string | null; electionFilter: string | null; gitSha: string | null;
  runCount: number; candidateCount: number; measureCount: number; countyBallotCount: number;
  countyDirectoryCount: number; proposedMeasureCount: number; unassignedRowCount: number;
  gaps: string[]; nextRun: NextRun | null; sources: Source[];
  summary: string | null; logText: string | null; errorText: string | null;
}

export interface Run {
  runId: number; passId: number; stateCode: string; electionDate: string; electionType: string; electionName: string;
  sourceElectionId: string; isPending: boolean; candidateCount: number; measureCount: number; countyBallotCount: number;
  createdAt: string; runCount: number;
}

export interface Candidate {
  office: string; district: string | null; county: string | null; name: string; party: string | null;
  status: string | null; ocdDivisionId: string | null; sourceOfficeType: string | null;
}

export interface Measure { measureId: string; title: string | null; jurisdiction: string; county: string | null }
export interface CountyBallot { county: string; candidateCount: number; measureCount: number }
export interface Page<T> { total: number; rows: T[] }

export interface StateSummary {
  code: string; name: string; implemented: boolean; lastCapture: Capture | null; lastPass: Pass | null;
  gapCount: number | null; nextRun: NextRun | null; elections: Run[];
}

export interface StateDetail { code: string; name: string; implemented: boolean; captures: Capture[]; passes: Pass[]; runs: Run[] }
export interface CaptureDetail { capture: Capture; fetches: Fetch[]; passes: Pass[]; runs: Run[] }
export interface RunDetail { run: Run; pass: Pass }
export interface ActivityData { captures: Capture[]; passes: Pass[]; runs: Run[] }

export interface Job {
  jobId: number; stateCode: string; year: number; kind: RunKind; normalizeCaptureId: number | null;
  electionFilter: string | null; status: Status; requestedBy: string; requestedAt: string;
  startedAt: string | null; finishedAt: string | null; captureId: number | null; passId: number | null;
  errorText: string | null;
}

export interface JobsData { jobs: Job[]; runs: Run[] }

export interface StartJob {
  stateCode: string; year: number; kind: RunKind; normalizeCaptureId: number | null;
  electionFilter: string | null; requestedBy: string;
}

/** Queues a run. Rejects with the API's reason when the request is refused. */
export async function startJob(request: StartJob): Promise<Job> {
  const response = await fetch("/api/jobs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.error ?? `The API answered ${response.status}.`);
  return body;
}

export const isActive = (job: Job) => job.status === "queued" || job.status === "running";

async function get<T>(path: string): Promise<T> {
  const response = await fetch(`/api${path}`);
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error ?? `The API answered ${response.status} for ${path}. Is it running? dotnet run --project src/StateBallot.Api`);
  }
  return response.json();
}

// Polling is how a run started elsewhere (the CLI today) shows up without a reload.
export function useApi<T>(path: string, refetch?: number | ((data: T | undefined) => number | false)) {
  return useQuery<T, Error>({
    queryKey: [path],
    queryFn: () => get<T>(path),
    placeholderData: keepPreviousData,
    refetchInterval: typeof refetch === "function" ? query => refetch(query.state.data) : refetch,
  });
}

// Two elections can share a date and a type (TX specials), so the source's election id is part of the key.
export const electionKey = (r: Run) => [r.electionDate, r.electionType, r.sourceElectionId].map(encodeURIComponent).join("~");

export interface Election { key: string; latest: Run; runs: Run[] }

export function electionsFrom(runs: Run[]): Election[] {
  const byKey = new Map<string, Run[]>();
  for (const r of [...runs].sort((a, b) => b.runId - a.runId)) {
    const key = electionKey(r);
    byKey.set(key, [...(byKey.get(key) ?? []), r]);
  }
  return [...byKey.entries()]
    .map(([key, list]) => ({ key, latest: list[0], runs: list }))
    .sort((a, b) => a.latest.electionDate.localeCompare(b.latest.electionDate) || a.runs[a.runs.length - 1].runId - b.runs[b.runs.length - 1].runId);
}
