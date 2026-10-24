import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const htmlPath = path.join(__dirname, "..", "index.html");
const html = fs.readFileSync(htmlPath, "utf8");

const match = html.match(/<script id="game-logic">([\s\S]*?)<\/script>/);
if (!match) {
  console.error("FAIL: cannot find <script id=\"game-logic\"> in index.html");
  process.exit(1);
}

const sandbox = { window: {}, globalThis: {} };
sandbox.window = sandbox;
vm.createContext(sandbox);
vm.runInContext(match[1], sandbox);

const G = sandbox.Game2048;
if (!G) {
  console.error("FAIL: Game2048 API not exposed");
  process.exit(1);
}

let passed = 0;
let failed = 0;

function assert(cond, msg) {
  if (cond) {
    passed++;
    console.log("  ok  " + msg);
  } else {
    failed++;
    console.error("  FAIL " + msg);
  }
}

function gridsEqual(a, b) {
  if (a.length !== b.length) return false;
  for (let r = 0; r < a.length; r++) {
    if (a[r].length !== b[r].length) return false;
    for (let c = 0; c < a[r].length; c++) {
      if (a[r][c] !== b[r][c]) return false;
    }
  }
  return true;
}

console.log("createBoard dimensions");
for (const n of [4, 5, 8, 36]) {
  const board = G.createBoard(n);
  assert(board.length === n, `createBoard(${n}) rows=${board.length}`);
  assert(board.every((row) => row.length === n && row.every((v) => v === 0)), `createBoard(${n}) all zeros n×n`);
}

console.log("clampSize");
assert(G.clampSize(3) === 4, "clamp 3 -> 4");
assert(G.clampSize(4) === 4, "clamp 4 -> 4");
assert(G.clampSize(36) === 36, "clamp 36 -> 36");
assert(G.clampSize(37) === 36, "clamp 37 -> 36");
assert(G.clampSize("8") === 8, "clamp string 8 -> 8");

console.log("slide/merge left");
{
  const grid = [[2, 2, 0, 0]];
  const res = G.move(grid, "left");
  assert(res.moved === true, "moved true");
  assert(res.score === 4, "score 4");
  assert(gridsEqual(res.grid, [[4, 0, 0, 0]]), "merge to [4,0,0,0]");
  assert(gridsEqual(grid, [[2, 2, 0, 0]]), "input not mutated");
}

console.log("no double merge");
{
  const res = G.move([[2, 2, 2, 0]], "left");
  assert(res.score === 4, "score 4 once");
  assert(gridsEqual(res.grid, [[4, 2, 0, 0]]), "[2,2,2,0] -> [4,2,0,0]");
}

console.log("chain merge order");
{
  const res = G.move([[2, 2, 4, 4]], "left");
  assert(res.score === 12, "score 4+8=12");
  assert(gridsEqual(res.grid, [[4, 8, 0, 0]]), "[2,2,4,4] -> [4,8,0,0]");
}

console.log("right direction");
{
  const res = G.move([[2, 2, 0, 0]], "right");
  assert(gridsEqual(res.grid, [[0, 0, 0, 4]]), "right merge");
  assert(res.score === 4, "right score");
}

console.log("up / down");
{
  const up = G.move(
    [
      [2, 0],
      [2, 0],
    ],
    "up"
  );
  assert(gridsEqual(up.grid, [[4, 0], [0, 0]]), "up merge");
  assert(up.score === 4, "up score");

  const down = G.move(
    [
      [2, 0],
      [2, 0],
    ],
    "down"
  );
  assert(gridsEqual(down.grid, [[0, 0], [4, 0]]), "down merge");
}

console.log("invalid move");
{
  const grid = [
    [2, 4, 8, 16],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
  ];
  const res = G.move(grid, "up");
  assert(res.moved === false, "up already packed moved=false");
  assert(res.score === 0, "no score");
  assert(gridsEqual(res.grid, grid), "grid unchanged");
}

console.log("canMove");
{
  assert(G.canMove([[0, 2], [4, 8]]) === true, "empty cell can move");
  assert(
    G.canMove([
      [2, 2],
      [4, 8],
    ]) === true,
    "adjacent merge exists"
  );
  assert(
    G.canMove([
      [2, 4],
      [8, 16],
    ]) === false,
    "blocked no empty no merge"
  );
  assert(
    G.canMove([
      [2, 2],
      [8, 16],
    ]) === true,
    "top row merge"
  );
}

console.log("addRandomTile");
{
  const full = [
    [2, 4],
    [8, 16],
  ];
  assert(G.addRandomTile(full) === false, "full board returns false");

  let i = 0;
  const seq = [0.05, 0.05];
  const rng = () => seq[i++];
  const board = G.createBoard(4);
  assert(G.addRandomTile(board, rng) === true, "empty board can add");
  const filled = board.flat().filter((v) => v !== 0);
  assert(filled.length === 1, "exactly one tile");
  assert(filled[0] === 2, "rng 0.05 -> tile 2");
}

console.log("addRandomTiles spawn count");
{
  assert(G.SPAWN_MIN === 1, "SPAWN_MIN 1");
  assert(G.SPAWN_MAX === 12, "SPAWN_MAX 12");
  assert(G.clampSpawnCount(0) === 1, "clamp spawn 0 -> 1");
  assert(G.clampSpawnCount(99) === 12, "clamp spawn 99 -> 12");
  assert(G.clampSpawnCount("3") === 3, "clamp spawn string 3");

  const board = G.createBoard(8);
  let calls = 0;
  const rng = () => {
    calls++;
    return 0.01;
  };
  const added = G.addRandomTiles(board, 3, rng);
  assert(added === 3, "added 3 tiles");
  const twos = board.flat().filter((v) => v === 2);
  assert(twos.length === 3, "all spawned tiles are 2");
  assert(G.emptyCells(board).length === 64 - 3, "3 cells filled");

  const almostFull = G.createBoard(2);
  almostFull[0][0] = 4;
  almostFull[0][1] = 4;
  almostFull[1][0] = 4;
  const added2 = G.addRandomTiles(almostFull, 5, () => 0.5);
  assert(added2 === 1, "stops when board full");
  assert(almostFull[1][1] === 2, "last empty became 2");
}

console.log("hasReached2048");
{
  assert(G.hasReached2048([[2048, 0], [0, 0]]) === true, "2048 present");
  assert(G.hasReached2048([[1024, 1024], [0, 0]]) === false, "1024 only");
  assert(G.hasReached2048([[4096, 0], [0, 0]]) === true, "4096 counts");
}

console.log("large board smoke 36");
{
  const board = G.createBoard(36);
  board[0][0] = 2;
  board[0][1] = 2;
  const res = G.move(board, "left");
  assert(res.grid[0][0] === 4, "36x36 left merge");
  assert(res.moved === true, "36x36 moved");
  assert(G.canMove(res.grid) === true, "36x36 still playable");
}

console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) process.exit(1);
