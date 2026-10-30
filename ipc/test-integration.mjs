import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";
import WebSocket from "ws";
import { createIpc } from "adofai-ipc";
import { OrbitRenderIpcContract } from "./generated/typescript/index.js";

const dotnet = process.env.DOTNET || "dotnet";
const serverDll = fileURLToPath(new URL("../Tests/IpcSdkIntegration/bin/Debug/net10.0/AdofaiIpc.Protocol.Tests.dll", import.meta.url));
const server = spawn(dotnet, [serverDll], { stdio: ["pipe", "pipe", "inherit"] });
const lines = createInterface({ input: server.stdout });
const { port, levelPath } = await new Promise((resolve, reject) => {
  lines.once("line", line => resolve(JSON.parse(line)));
  server.once("exit", code => reject(new Error(`SDK test server exited: ${code}`)));
});
const command = text => new Promise(resolve => {
  lines.once("line", resolve);
  server.stdin.write(`${text}\n`);
});

const originalFetch = globalThis.fetch;
let origin = "https://orbitrender.imnya.ng";
globalThis.WebSocket = class extends WebSocket {
  constructor(url, protocols) { super(url, protocols, { origin }); }
};
globalThis.fetch = (url, options = {}) => originalFetch(url, {
  ...options, headers: { ...options.headers, Origin: origin }
});

try {
  const app = createIpc({ startPort: port, endPort: port, requestTimeoutMs: 1000 }).registerApp({ displayName: "OrbitRender test" });
  const channel = app.use(OrbitRenderIpcContract, {});
  await channel.ready({ timeoutMs: 5000 });
  const health = await channel.health();
  assert.equal(health.ok, true);
  assert.equal(health.renderer, "idle");
  const accepted = await channel.createRender({ options: { levelPath, videoFps: 60, bgaMode: false } });
  assert.ok(accepted.id);
  const status = await channel.renderStatus({ id: accepted.id });
  assert.equal(status.state, "queued");
  assert.equal(status.settings.videoFps, 60);
  assert.equal(status.settings.width, null);
  assert.equal(status.settings.bgaMode, false);
  assert.equal((await channel.listJobs()).length, 1);
  assert.equal((await channel.cancelRender({ id: accepted.id })).state, "cancelling");
  await assert.rejects(channel.downloadRender({ id: accepted.id }));
  await assert.rejects(channel.createRender({ options: { levelPath, videoFps: 999 } }));
  assert.equal(await command("complete"), "OK");
  assert.equal((await channel.renderStatus({ id: accepted.id })).state, "completed");
  const file = await channel.downloadRender({ id: accepted.id });
  assert.equal(await file.text(), "video-content");
  await assert.rejects(channel.renderStatus({ id: "unknown" }));
  const unavailable = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("Disabled mod stayed ready")), 5000);
    let unsubscribe = () => {};
    unsubscribe = channel.onStatus(status => {
      if (status !== "ready") { clearTimeout(timer); unsubscribe(); resolve(); }
    });
  });
  assert.equal(await command("disable"), "OK");
  await unavailable;
  await assert.rejects(channel.ready({ timeoutMs: 300 }), { code: "TIMEOUT" });
  assert.equal(await command("enable"), "OK");
  await channel.ready({ timeoutMs: 5000 });
  assert.equal((await channel.health()).ok, true);
  app.dispose();

  origin = "https://untrusted.example";
  const rejectedApp = createIpc({ startPort: port, endPort: port }).registerApp({ displayName: "Rejected test" });
  const rejectedChannel = rejectedApp.use(OrbitRenderIpcContract, {});
  await assert.rejects(rejectedChannel.ready({ timeoutMs: 3000 }), { code: "origin_not_allowed" });
  rejectedApp.dispose();
  console.log("SDK integration: calls, options, download, lifecycle and origin restriction passed");
} finally {
  server.stdin.end("stop\n");
  lines.close();
  await new Promise(resolve => server.once("exit", resolve));
}
