import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const tokenRes = await fetch("http://127.0.0.1:5262/api/dev/token", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email: "admin@17v3ygzs.mailosaur.net" }),
});
const { token } = await tokenRes.json();
console.log("token", token.slice(0,20)+"...");

const transport = new StdioClientTransport({
  command: "npx",
  args: ["tsx", "mcp/workslip-visuals/src/index.ts"],
  env: {
    ...process.env,
    WORKSLIP_API_URL: "http://127.0.0.1:5262",
    WORKSLIP_API_TOKEN: token,
    WORKSLIP_APP_URL: "http://127.0.0.1:5270",
  },
  cwd: "/Users/rbjdonor/Development/repos/Workslip-v2.0",
});

const client = new Client({ name: "test", version: "0.1.0" });
await client.connect(transport);
console.log("connected");

const tools = await client.listTools();
console.log("tools", tools.tools.map(t=>t.name));

for (const name of ["workslip_get_leader_analysis", "workslip_get_economics", "workslip_create_chart", "workslip_create_map"]) {
  console.log("\n===", name, "===");
  try {
    let args = {};
    if (name === "workslip_create_chart") args = { chartType: "donut", dataSource: "sagsflow" };
    if (name === "workslip_create_map") args = { source: "jobs", limit: 3 };
    const res = await client.callTool({ name, arguments: args });
    console.log(JSON.stringify(res.content[0].text).slice(0,800));
  } catch(e) {
    console.error(e.message.slice(0,500));
  }
}

await client.close();
console.log("done");
