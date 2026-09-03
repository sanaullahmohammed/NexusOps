// Structured JSON stdout logger. Aspire's telemetry pipeline scrapes container/process stdout for
// Node resources rather than requiring a full OpenTelemetry JS SDK — one JSON line per event is the
// practical way to satisfy Constitution VI's "structured JSON logs" requirement for a service this
// deliberately minimal (research.md Decision 10).

export type LogLevel = "info" | "warn" | "error";

export interface LogFields {
  [key: string]: unknown;
}

export function log(level: LogLevel, event: string, fields: LogFields = {}): void {
  const line = {
    timestamp: new Date().toISOString(),
    level,
    event,
    ...fields,
  };
  const target = level === "error" ? console.error : console.log;
  target(JSON.stringify(line));
}
