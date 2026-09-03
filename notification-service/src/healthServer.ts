import { createServer } from "node:http";
import { log } from "./logger.ts";
import { isConnected } from "./amqpConsumer.ts";

// Bare node:http — no framework — matching ROADMAP.md's "nothing more" scope for this service.
// Mirrors NexusOps.ServiceDefaults' WriteHealthResponse response shape exactly, so every service in
// the system (Node or .NET) reports health the same way to the Aspire dashboard. Reflects AMQP
// connectivity rather than reporting unconditionally healthy: this service structurally cannot do
// its one job (consume and log notifications) without the bus, mirroring WorkflowOrchestrator's own
// readiness precedent (research.md Decision 7) rather than AgentHost/the domain services' precedent
// of staying healthy through a dependency outage they can meaningfully operate without.
export function startHealthServer(port: number): void {
  const server = createServer((req, res) => {
    if (req.method === "GET" && req.url === "/health") {
      const healthy = isConnected();
      res.writeHead(healthy ? 200 : 503, { "Content-Type": "application/json; charset=utf-8" });
      res.end(JSON.stringify({ status: healthy ? "healthy" : "unhealthy" }));
      return;
    }
    res.writeHead(404, { "Content-Type": "application/json; charset=utf-8" });
    res.end(JSON.stringify({ error: "not found" }));
  });

  server.listen(port, () => {
    log("info", "health-server.started", { port });
  });
}
