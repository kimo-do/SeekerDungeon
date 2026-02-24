import Fastify from "fastify";
import { timingSafeEqual } from "node:crypto";
import { loadConfig } from "./config.js";
import { FeedService, parseFeedPublishPayload } from "./feed.js";
import { SeekerIdService } from "./resolver.js";

const main = async (): Promise<void> => {
  const config = loadConfig();
  const app = Fastify({
    logger: true
  });

  const service = new SeekerIdService(config, (message) => {
    if (config.logDebug) {
      app.log.info(message);
    }
  });
  const feedService = new FeedService(config.feedMaxEvents);

  const parsePositiveInt = (value: string | undefined, fallback: number): number => {
    if (!value) {
      return fallback;
    }

    const parsed = Number.parseInt(value, 10);
    if (!Number.isFinite(parsed) || parsed < 0) {
      return fallback;
    }

    return parsed;
  };

  const isFeedTokenValid = (headerValue: string | string[] | undefined): boolean => {
    if (!config.feedPublishToken) {
      return true;
    }

    const providedToken = Array.isArray(headerValue) ? headerValue[0] ?? "" : headerValue ?? "";
    const expectedBytes = Buffer.from(config.feedPublishToken, "utf8");
    const providedBytes = Buffer.from(providedToken.trim(), "utf8");
    if (expectedBytes.length !== providedBytes.length) {
      return false;
    }

    return timingSafeEqual(expectedBytes, providedBytes);
  };

  app.get("/healthz", async () => {
    return { ok: true, service: "seeker-id-api" };
  });

  app.get<{ Querystring: { wallet?: string } }>("/seeker-id/resolve", async (request, reply) => {
    const wallet = (request.query.wallet ?? "").trim();
    if (!wallet) {
      reply.code(400);
      return {
        error: "Missing wallet query parameter."
      };
    }

    try {
      const result = await service.resolve(wallet);
      return result;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      reply.code(400);
      return {
        error: message
      };
    }
  });

  app.post("/feed/events", async (request, reply) => {
    try {
      if (!isFeedTokenValid(request.headers["x-feed-token"])) {
        reply.code(401);
        return {
          error: "Unauthorized feed publish."
        };
      }

      const payload = parseFeedPublishPayload(request.body);
      const event = feedService.publish(payload);
      return {
        ok: true,
        event
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      reply.code(400);
      return {
        error: message
      };
    }
  });

  app.get<{ Querystring: { afterId?: string; limit?: string } }>("/feed/events", async (request) => {
    const afterId = parsePositiveInt(request.query.afterId, 0);
    const requestedLimit = parsePositiveInt(request.query.limit, config.feedPollLimitDefault);
    const limit = Math.min(Math.max(1, requestedLimit), config.feedPollLimitMax);
    const events = feedService.listAfter(afterId, limit);
    return {
      events
    };
  });

  app.get("/feed/stream", async (request, reply) => {
    reply.hijack();

    const rawReply = reply.raw;
    rawReply.writeHead(200, {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no"
    });
    rawReply.write(": connected\n\n");

    const writeEvent = (event: { id: number; message: string; type: string; createdAtUnix: number }): void => {
      rawReply.write(`event: feed\n`);
      rawReply.write(`id: ${event.id}\n`);
      rawReply.write(`data: ${JSON.stringify(event)}\n\n`);
    };

    const unsubscribe = feedService.subscribe(writeEvent);
    const heartbeatHandle = setInterval(() => {
      rawReply.write(`: heartbeat ${Date.now()}\n\n`);
    }, config.feedHeartbeatSeconds * 1000);

    const cleanup = (): void => {
      clearInterval(heartbeatHandle);
      unsubscribe();
    };

    request.raw.on("close", cleanup);
    request.raw.on("aborted", cleanup);
  });

  await app.listen({
    port: config.port,
    host: "0.0.0.0"
  });
};

main().catch((error) => {
  // Process-level failure log for startup/runtime boot issues.
  // Keep message plain ASCII for windows console compatibility.
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  // eslint-disable-next-line no-console
  console.error(message);
  process.exit(1);
});
