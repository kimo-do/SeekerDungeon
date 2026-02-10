import Fastify from "fastify";
import { loadConfig } from "./config.js";
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
