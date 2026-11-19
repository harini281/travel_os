import { createServer } from "node:http";

const port = Number(process.env.E2E_AI_MOCK_PORT ?? 8011);

const recommendations = [
  {
    title: "Botanical Garden Walk",
    category: "new_activity",
    reason: "A calm outdoor visit that adds variety to the day's existing museum stop.",
    suggestedDurationMinutes: 60,
    estimatedCost: 500,
    currencyCode: "LKR",
    bestFor: "Nature lovers and relaxed travellers",
    verificationNote: "Playwright fixture only. Verify the real venue, opening hours and price before visiting.",
  },
  {
    title: "Riverside Art Walk",
    category: "group_interest",
    reason: "A longer self-guided walk with public art and river views.",
    suggestedDurationMinutes: 120,
    estimatedCost: 0,
    currencyCode: "LKR",
    bestFor: "Groups and photography enthusiasts",
    verificationNote: "Playwright fixture only. Verify the route, access and local conditions before visiting.",
  },
];

const server = createServer(async (request, response) => {
  if (request.method === "GET" && request.url === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ status: "ok", mode: "playwright-mock" }));
    return;
  }

  if (request.method !== "POST" || request.url !== "/api/v1/planner/recommendations") {
    response.writeHead(404, { "content-type": "application/json" });
    response.end(JSON.stringify({ detail: "Not found" }));
    return;
  }

  let body = "";
  for await (const chunk of request) body += chunk;
  let payload;
  try {
    payload = JSON.parse(body);
  } catch {
    response.writeHead(400, { "content-type": "application/json" });
    response.end(JSON.stringify({ detail: "Invalid JSON request" }));
    return;
  }

  // This controlled failure path lets browser tests verify the real Platform API
  // error handling without replacing authentication, persistence or scheduling.
  if (String(payload.question ?? "").includes("[e2e:fail]")) {
    response.writeHead(503, { "content-type": "application/json" });
    response.end(JSON.stringify({ detail: "Deterministic Playwright AI-provider failure." }));
    return;
  }

  response.writeHead(200, { "content-type": "application/json" });
  response.end(JSON.stringify({
    status: "generated",
    provider: "playwright-mock",
    model: "deterministic-fixture-v1",
    summary: "Two deterministic ideas for your selected itinerary day.",
    recommendations,
    warnings: ["These are test fixtures, not verified live venue listings."],
  }));
});

server.listen(port, "127.0.0.1", () => {
  process.stdout.write(`TravelOS Playwright AI mock listening on http://127.0.0.1:${port}\n`);
});

server.on("error", error => {
  process.stderr.write(`Playwright AI mock failed: ${error.message}\n`);
  process.exitCode = 1;
});
