import assert from "node:assert/strict";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const packageRoot = path.dirname(fileURLToPath(import.meta.url));

const transport = new StdioClientTransport({
  command: "npx",
  args: ["tsx", "src/index.ts"],
  env: {
    ...process.env,
  },
  cwd: packageRoot,
});

const client = new Client({ name: "test", version: "0.1.0" });
try {
  await client.connect(transport);
  const { tools } = await client.listTools();
  const names = tools.map((tool) => tool.name).sort();
  assert.deepEqual(names, [
    "workslip_create_chart",
    "workslip_create_dashboard",
    "workslip_create_map",
    "workslip_get_economics",
    "workslip_get_engagement",
    "workslip_get_leader_analysis",
  ]);
} finally {
  await client.close();
}
