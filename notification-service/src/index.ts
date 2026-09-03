import { log } from "./logger.ts";
import { startHealthServer } from "./healthServer.ts";
import { startNotificationConsumer } from "./amqpConsumer.ts";

const connectionString = process.env.ConnectionStrings__rabbitmq;

if (!connectionString) {
  // Mirrors the named-configuration-failure guard every .NET bus-connected host in this system
  // applies for the same connection string (CLAUDE.md's ".NET project conventions" / task T068 of
  // feature 005) — name the offending key rather than failing with an opaque connection error.
  log("error", "startup.missing-configuration", { missing: "ConnectionStrings__rabbitmq" });
  process.exit(1);
}

const port = Number(process.env.PORT ?? 8080);

log("info", "startup.starting", { port });

startHealthServer(port);

// Reconnects with backoff indefinitely on its own (amqpConsumer.ts) -- this call starts that loop
// and returns immediately rather than blocking startup on the first connection attempt.
await startNotificationConsumer(connectionString);
