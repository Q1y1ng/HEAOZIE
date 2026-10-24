import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const html = fs.readFileSync(path.join(__dirname, "..", "index.html"), "utf8");

let failed = 0;
function assert(cond, msg) {
  if (cond) console.log("  ok  " + msg);
  else {
    failed++;
    console.error("  FAIL " + msg);
  }
}

console.log("required DOM ids");
for (const id of [
  "game-logic",
  "board",
  "score",
  "best",
  "btn-new",
  "btn-undo",
  "btn-apply",
  "size-range",
  "size-number",
  "size-label",
  "size-presets",
  "overlay",
  "spawn-count",
]) {
  assert(html.includes(`id="${id}"`), `has #${id}`);
}

console.log("feature markers");
assert(html.includes("SIZE_MIN = 4"), "SIZE_MIN 4");
assert(html.includes("SIZE_MAX = 36"), "SIZE_MAX 36");
assert(html.includes("SPAWN_MIN = 1"), "SPAWN_MIN 1");
assert(html.includes("SPAWN_MAX = 12"), "SPAWN_MAX 12");
assert(html.includes("addRandomTiles"), "addRandomTiles API");
assert(html.includes("2048-spawn-count"), "spawn count persistence key");
assert(html.includes("2048-best-"), "best score key prefix");
assert(html.includes("2048-state"), "state persistence key");
assert(/min="4"[^>]*max="36"|max="36"[^>]*min="4"/.test(html), "range min/max 4-36");
assert(/id="spawn-count"[^>]*min="1"[^>]*max="12"|min="1"[^>]*max="12"[^>]*id="spawn-count"/.test(html), "spawn input 1-12");
assert(html.includes('id="game-logic"'), "logic script isolation");

console.log("keyboard + swipe");
assert(html.includes("ArrowUp"), "arrow keys");
assert(html.includes("touchstart"), "touch swipe");
assert(html.includes("WASD") || html.includes('"w"'), "WASD");

console.log(`\n${failed === 0 ? "PASS" : "FAIL"} structure checks, ${failed} failed`);
if (failed) process.exit(1);
