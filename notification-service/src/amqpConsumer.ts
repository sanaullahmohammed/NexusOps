import amqp from "amqplib";
import { log } from "./logger.ts";

// The exchange name is pinned by NotificationRequested's [MassTransit.EntityName("notification-requested")]
// attribute (NexusOps.Contracts/Messages/NotificationRequested.cs) rather than left to MassTransit's
// default CLR-type-derived naming — this is the one fixed, documented name a plain amqplib consumer
// binds against, with no need to reimplement MassTransit's internal naming convention in JavaScript
// (research.md Decision 9).
const EXCHANGE_NAME = "notification-requested";
const QUEUE_NAME = "notification-service.notification-requested";

const RECONNECT_DELAYS_MS = [1000, 2000, 5000, 10000, 30000];

// MassTransit's default RabbitMQ transport wraps every message body in a JSON "envelope" carrying
// delivery metadata (messageId, correlationId, messageType, etc.) alongside the actual payload under
// `message`. Only the fields this service actually reads are modeled here.
interface MassTransitEnvelope {
  message: {
    correlationId: string;
    orderId: string;
    actionType: string;
    outcome: string;
    message: string;
  };
}

let connected = false;

/** Whether the AMQP connection is currently up — this service's only job depends entirely on it (mirrors WorkflowOrchestrator's research.md Decision 7: readiness should reflect bus connectivity for a host that structurally cannot do anything without it). */
export function isConnected(): boolean {
  return connected;
}

export async function startNotificationConsumer(connectionString: string): Promise<void> {
  // Previously connected once and, on a dropped connection, only logged a warning — the consumer
  // then stayed dead for the rest of the process's life until someone noticed and restarted it
  // manually (code review finding). Reconnects with backoff instead, indefinitely.
  void connectWithRetry(connectionString, 0);
}

async function connectWithRetry(connectionString: string, attempt: number): Promise<void> {
  try {
    const connection = await amqp.connect(connectionString);
    const channel = await connection.createChannel();

    await channel.assertExchange(EXCHANGE_NAME, "fanout", { durable: true });
    await channel.assertQueue(QUEUE_NAME, { durable: true });
    await channel.bindQueue(QUEUE_NAME, EXCHANGE_NAME, "");

    connected = true;
    log("info", "amqp-consumer.started", { exchange: EXCHANGE_NAME, queue: QUEUE_NAME, attempt });

    await channel.consume(
      QUEUE_NAME,
      (msg) => {
        if (msg === null) {
          return;
        }

        try {
          const envelope = JSON.parse(msg.content.toString()) as MassTransitEnvelope;
          const { correlationId, orderId, actionType, outcome, message } = envelope.message;

          // The "simulated email" this feature's instructions call for — a structured log line
          // stands in for an actual send, per ROADMAP.md's "logs a simulated email — nothing more".
          log("info", "notification.logged", {
            correlationId,
            orderId,
            actionType,
            outcome,
            simulatedEmail: `Simulated email to ops@nexusops.example: ${message}`,
          });

          channel.ack(msg);
        } catch (error) {
          // A malformed message is not retried into an infinite redelivery loop — nack without
          // requeue, letting the broker's own dead-letter handling (if configured) take it from here.
          log("error", "notification.parse-failed", {
            error: error instanceof Error ? error.message : String(error),
          });
          channel.nack(msg, false, false);
        }
      },
      { noAck: false },
    );

    connection.on("error", (error) => {
      log("warn", "amqp-consumer.connection-error", { error: error instanceof Error ? error.message : String(error) });
    });

    connection.on("close", () => {
      connected = false;
      log("warn", "amqp-consumer.connection-closed", {});
      void connectWithRetry(connectionString, 0);
    });
  } catch (error) {
    connected = false;
    const delay = RECONNECT_DELAYS_MS[Math.min(attempt, RECONNECT_DELAYS_MS.length - 1)];
    log("warn", "amqp-consumer.connect-failed", {
      error: error instanceof Error ? error.message : String(error),
      retryInMs: delay,
      attempt,
    });
    setTimeout(() => void connectWithRetry(connectionString, attempt + 1), delay);
  }
}
