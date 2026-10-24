// Generic N x N 2048 engine: expectimax search + heuristic evaluation.
//
// Score-first design (2026-10-24): searches the real game rules (slide/merge +
// random spawn), so the same code plays 4x4, 5x5, 6x6...
//
// Build:
//   clang++ -O2 -std=c++17 engine.cpp -o engine.exe
// Run:
//   engine.exe --size 4 --games 10 --budget 100
//   engine.exe --size 5 --games 5 --budget 150
//
// Board cells are exponents: 0 empty, 1=2, 2=4, ... (int8_t, max 15 = 32768).
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <random>
#include <string>
#include <unordered_map>
#include <vector>

using Clock = std::chrono::steady_clock;

struct Board {
    int n;
    int8_t c[64];  // n*n, fixed size: no heap allocation in the search hot path
    explicit Board(int n_ = 4) : n(n_) { std::memset(c, 0, sizeof(c)); }
    int8_t& at(int r, int col) { return c[r * n + col]; }
    int8_t at(int r, int col) const { return c[r * n + col]; }
    bool operator==(const Board& o) const {
        return n == o.n && std::memcmp(c, o.c, (size_t)(n * n)) == 0;
    }
};

struct Config {
    int n = 4;
    int maxDepth = 6;
    double budgetMs = 100.0;
    double prob4 = 0.1;
    int chanceCap = 8;
    // heuristic weights (score-first defaults, tuned on 4x4)
    double wEmpty = 270.0;
    double wMono = 47.0;
    double wSmooth = 0.0;
    double wPower = 11.0;
    double wMerges = 700.0;
    double wCorner = 60.0;
    double wSnake = 0.0;
    double wCornerProx = 0.0;
    int evalMode = 0;   // 0 = raw weighted sum, 1 = paper-style normalized features
};

static Config g_cfg;

// ---------------------------------------------------------------- moves ----
static void merge_line(int8_t* line, int n, int& gain) {
    int8_t out[16];
    int m = 0;
    for (int i = 0; i < n; i++) if (line[i]) out[m++] = line[i];
    int w = 0, i = 0;
    while (i < m) {
        if (i + 1 < m && out[i] == out[i + 1]) {
            int v = out[i] + 1;
            line[w++] = (int8_t)v;
            gain += 1 << v;
            i += 2;
        } else {
            line[w++] = out[i++];
        }
    }
    while (w < n) line[w++] = 0;
}

// dir: 0 left, 1 right, 2 up, 3 down. Returns score gain; board unchanged if illegal.
static int do_move(Board& b, int dir) {
    Board before = b;
    int gain = 0;
    int8_t line[16];
    if (dir == 0 || dir == 1) {
        for (int r = 0; r < b.n; r++) {
            for (int i = 0; i < b.n; i++) line[i] = b.at(r, dir == 0 ? i : b.n - 1 - i);
            int g = 0;
            merge_line(line, b.n, g);
            for (int i = 0; i < b.n; i++) b.at(r, dir == 0 ? i : b.n - 1 - i) = line[i];
            gain += g;
        }
    } else {
        for (int col = 0; col < b.n; col++) {
            for (int i = 0; i < b.n; i++) line[i] = b.at(dir == 2 ? i : b.n - 1 - i, col);
            int g = 0;
            merge_line(line, b.n, g);
            for (int i = 0; i < b.n; i++) b.at(dir == 2 ? i : b.n - 1 - i, col) = line[i];
            gain += g;
        }
    }
    if (b == before) return -1;  // illegal
    return gain;
}

static bool has_move(const Board& b) {
    for (int d = 0; d < 4; d++) {
        Board t = b;
        if (do_move(t, d) >= 0) return true;
    }
    return false;
}

// -------------------------------------------------------------- heuristics --
// Snake path: a boustrophedon covering the board; value drops faster along it.
static int snake_path_index(int n, int r, int col) {
    return (r % 2 == 0) ? r * n + col : r * n + (n - 1 - col);
}

static double evaluate(const Board& b) {
    const int n = b.n;
    int empties = 0;
    double sumPower = 0.0;
    int merges = 0;
    int8_t mx = 0;
    int mxIdx = 0;
    for (int i = 0; i < n * n; i++) {
        int v = b.c[i];
        if (!v) { empties++; continue; }
        sumPower += v;
        if (v > mx) { mx = v; mxIdx = i; }
        int r = i / n, col = i % n;
        if (col + 1 < n && b.c[i + 1] == v) merges++;
        if (r + 1 < n && b.c[i + n] == v) merges++;
    }
    if (empties == 0) {
        bool any = false;
        for (int r = 0; r < n && !any; r++)
            for (int col = 0; col + 1 < n && !any; col++)
                if (b.at(r, col) && b.at(r, col) == b.at(r, col + 1)) any = true;
        for (int col = 0; col < n && !any; col++)
            for (int r = 0; r + 1 < n && !any; r++)
                if (b.at(r, col) && b.at(r, col) == b.at(r + 1, col)) any = true;
        if (!any) return -200000.0;
    }
    // monotonicity penalty: per line, the weaker direction's total rise
    double monoPen = 0.0;
    for (int r = 0; r < n; r++) {
        double inc = 0, dec = 0;
        for (int col = 0; col + 1 < n; col++) {
            double d = (double)b.at(r, col + 1) - b.at(r, col);
            if (d > 0) inc += d; else dec -= d;
        }
        monoPen += std::min(inc, dec);
    }
    for (int col = 0; col < n; col++) {
        double inc = 0, dec = 0;
        for (int r = 0; r + 1 < n; r++) {
            double d = (double)b.at(r + 1, col) - b.at(r, col);
            if (d > 0) inc += d; else dec -= d;
        }
        monoPen += std::min(inc, dec);
    }
    // smoothness: adjacent log2 gaps (like the paper's 1/(1+diff) ratio)
    double smooth = 0.0;
    if (g_cfg.wSmooth != 0.0 || g_cfg.evalMode == 1) {
        for (int r = 0; r < n; r++)
            for (int col = 0; col < n; col++) {
                int v = b.at(r, col);
                if (!v) continue;
                if (col + 1 < n && b.at(r, col + 1)) smooth += 1.0 / (1.0 + std::abs(v - b.at(r, col + 1)));
                if (r + 1 < n && b.at(r + 1, col)) smooth += 1.0 / (1.0 + std::abs(v - b.at(r + 1, col)));
            }
    }
    // corner proximity: max tile as close as possible to any corner
    int mr = mxIdx / n, mc = mxIdx % n;
    double bestProx = 0.0;
    int corners[4][2] = {{0, 0}, {0, n - 1}, {n - 1, 0}, {n - 1, n - 1}};
    for (int k = 0; k < 4; k++) {
        double dist = std::abs(mr - corners[k][0]) + std::abs(mc - corners[k][1]);
        double prox = 1.0 - dist / (2.0 * (n - 1));
        bestProx = std::max(bestProx, prox);
    }
    // snake: count how many consecutive snake-path pairs are non-increasing
    double snake = 0.0;
    if (g_cfg.wSnake != 0.0 || g_cfg.evalMode == 1) {
        int prev = -1;
        for (int r = 0; r < n; r++)
            for (int col = 0; col < n; col++) {
                int i = snake_path_index(n, r, col);
                int v = b.c[i];
                if (v && prev >= 0 && prev >= v) snake += 1.0;
                if (v) prev = v;
            }
    }
    double snakeRatio = snake / (double)(n * n - 1);
    if (g_cfg.evalMode == 1) {
        // paper-style normalized evaluation: every feature lands in [0,1],
        // weights sum to 1 (Merge and Conquer, arXiv:2510.20205, cycle-11 weights)
        double emptyRatio = empties / (double)(n * n);
        double maxRatio = mx / (double)(n * n);
        double mergeRatio = merges / (double)(n * n);
        double smoothRatio = smooth / (2.0 * n * (n - 1));
        return 10000.0 * (0.30 * emptyRatio
                        + 0.20 * maxRatio
                        + 0.15 * bestProx
                        + 0.10 * mergeRatio
                        + 0.10 * smoothRatio
                        + 0.15 * snakeRatio);
    }
    double smoothTerm = (g_cfg.wSmooth != 0.0) ? smooth : 0.0;
    double snakeTerm = (g_cfg.wSnake != 0.0) ? snakeRatio : 0.0;
    double cornerHit = 0.0;
    if (g_cfg.wCorner != 0.0) {
        int corners[4] = {0, n - 1, n * (n - 1), n * n - 1};
        for (int k = 0; k < 4; k++) if (b.c[corners[k]] == mx && mx) cornerHit = mx;
    }
    return g_cfg.wEmpty * empties
         + g_cfg.wMono * (-monoPen)
         + g_cfg.wSmooth * smoothTerm
         + g_cfg.wPower * sumPower
         + g_cfg.wMerges * merges
         + g_cfg.wCorner * cornerHit
         + g_cfg.wSnake * snakeTerm
         + g_cfg.wCornerProx * bestProx * mx;
}

// ---------------------------------------------------------------- search ----
struct Deadline {
    Clock::time_point end;
    bool hit = false;
    long nodes = 0;
    bool expired() {
        if ((++nodes & 511) == 0 && Clock::now() > end) hit = true;
        return hit;
    }
};

static std::unordered_map<uint64_t, float> g_tt;

static inline uint64_t hash_board(const Board& b, int depth) {
    uint64_t h = 1469598103934665603ULL;
    for (int i = 0; i < b.n * b.n; i++) {
        h ^= (uint8_t)b.c[i];
        h *= 1099511628211ULL;
    }
    h ^= (uint64_t)(depth * 2) * 0x9E3779B97F4A7C15ULL;
    return h;
}

static double searchMax(const Board& b, int depth, Deadline& dl);
static double searchChance(const Board& b, int depth, Deadline& dl);

static double searchMax(const Board& b, int depth, Deadline& dl) {
    if (dl.expired()) return evaluate(b);
    if (depth <= 0) return evaluate(b);
    const uint64_t key = hash_board(b, depth);
    auto it = g_tt.find(key);
    if (it != g_tt.end()) return it->second;
    double best = -1e18;
    for (int d = 0; d < 4; d++) {
        Board nb = b;
        int gain = do_move(nb, d);
        if (gain < 0) continue;
        double v = gain + searchChance(nb, depth - 1, dl);
        if (v > best) best = v;
        if (dl.hit) break;
    }
    if (!dl.hit) {
        if (g_tt.size() > 3000000) g_tt.clear();
        g_tt[key] = (float)best;
    }
    return best;
}

static double searchChance(const Board& b, int depth, Deadline& dl) {
    int n = b.n;
    int empties[64];
    int ne = 0;
    for (int i = 0; i < n * n; i++) if (!b.c[i]) empties[ne++] = i;
    if (ne == 0) return searchMax(b, depth, dl);
    // approximate with an evenly spaced subset on wide boards; in the tight
    // endgame (<=4 empties) expand every spawn exactly — a wrong guess there
    // kills the whole run.
    int cells[64];
    int nc = 0;
    int cap = g_cfg.chanceCap;
    if (ne <= 4) cap = ne;
    if (ne > cap) {
        double step = (double)ne / cap;
        for (int i = 0; i < cap; i++) cells[nc++] = empties[(int)(i * step)];
    } else {
        for (int i = 0; i < ne; i++) cells[nc++] = empties[i];
    }
    double total = 0.0;
    for (int k = 0; k < nc; k++) {
        int idx = cells[k];
        Board b2 = b;
        b2.c[idx] = 1;
        total += (1.0 - g_cfg.prob4) * searchMax(b2, depth, dl);
        if (g_cfg.prob4 > 0) {
            b2.c[idx] = 2;
            total += g_cfg.prob4 * searchMax(b2, depth, dl);
        }
        if (dl.hit) break;
    }
    return total / (double)nc;
}

struct Choice { int dir; double value; int depth; };

static Choice choose_move(const Board& b) {
    Choice best{0, -1e18, 0};
    Deadline dl;
    dl.end = Clock::now() + std::chrono::milliseconds((long long)g_cfg.budgetMs);
    for (int depth = 1; depth <= g_cfg.maxDepth; depth++) {
        Deadline d2 = dl;
        Choice local{best.dir, -1e18, depth};
        for (int d = 0; d < 4; d++) {
            Board nb = b;
            int gain = do_move(nb, d);
            if (gain < 0) continue;
            double v = gain + searchChance(nb, depth - 1, d2);
            if (v > local.value) { local.value = v; local.dir = d; }
            if (d2.hit) break;
        }
        if (d2.hit) break;
        best = local;
        if (Clock::now() > dl.end) break;
    }
    return best;
}

// ------------------------------------------------------------------ play ----
static std::mt19937_64 g_rng;

static void spawn_tile(Board& b) {
    int e[64];
    int ne = 0;
    for (int i = 0; i < b.n * b.n; i++) if (!b.c[i]) e[ne++] = i;
    if (ne == 0) return;
    std::uniform_int_distribution<int> pick(0, ne - 1);
    std::uniform_real_distribution<double> u(0.0, 1.0);
    b.c[e[pick(g_rng)]] = (u(g_rng) < g_cfg.prob4) ? 2 : 1;
}

static void print_board_json(FILE* out, const Board& b) {
    fprintf(out, "[");
    for (int r = 0; r < b.n; r++) {
        if (r) fprintf(out, ",");
        fprintf(out, "[");
        for (int col = 0; col < b.n; col++) {
            if (col) fprintf(out, ",");
            fprintf(out, "%d", b.at(r, col) ? (1 << b.at(r, col)) : 0);
        }
        fprintf(out, "]");
    }
    fprintf(out, "]");
}

int main(int argc, char** argv) {
    int games = 1;
    unsigned long long seed = 1;
    const char* savePath = nullptr;
    for (int i = 1; i < argc; i++) {
        std::string a = argv[i];
        auto next = [&]() { return std::string(argv[++i]); };
        if (a == "--size") g_cfg.n = std::stoi(next());
        else if (a == "--games") games = std::stoi(next());
        else if (a == "--budget") g_cfg.budgetMs = std::stod(next());
        else if (a == "--max-depth") g_cfg.maxDepth = std::stoi(next());
        else if (a == "--chance-cap") g_cfg.chanceCap = std::stoi(next());
        else if (a == "--seed") seed = std::stoull(next());
        else if (a == "--save") savePath = strdup(next().c_str());
        else if (a == "--w-empty") g_cfg.wEmpty = std::stod(next());
        else if (a == "--w-mono") g_cfg.wMono = std::stod(next());
        else if (a == "--w-smooth") g_cfg.wSmooth = std::stod(next());
        else if (a == "--w-power") g_cfg.wPower = std::stod(next());
        else if (a == "--w-merges") g_cfg.wMerges = std::stod(next());
        else if (a == "--w-corner") g_cfg.wCorner = std::stod(next());
        else if (a == "--w-snake") g_cfg.wSnake = std::stod(next());
        else if (a == "--w-cprox") g_cfg.wCornerProx = std::stod(next());
        else if (a == "--eval-norm") g_cfg.evalMode = 1;
    }
    double sumScore = 0, sumSec = 0;
    int bestScore = 0, minScore = 1 << 30;
    for (int g = 0; g < games; g++) {
        g_rng.seed(seed + g);
        Board b(g_cfg.n);
        spawn_tile(b); spawn_tile(b);
        int score = 0, moves = 0, mx = 0;
        double depthSum = 0;
        auto t0 = Clock::now();
        FILE* out = nullptr;
        if (savePath && g == 0) {
            out = fopen(savePath, "w");
            if (out) {
                fprintf(out, "{\"meta\":{\"model\":\"expectimax-heuristic\",\"size\":%d,\"seed\":%llu,\"budget_ms\":%.0f},"
                        "\"steps\":[", g_cfg.n, seed, g_cfg.budgetMs);
            }
        }
        while (has_move(b)) {
            Choice ch = choose_move(b);
            Board before = b;
            int gain = do_move(b, ch.dir);
            if (gain < 0) break;  // should not happen
            score += gain;
            depthSum += ch.depth;
            spawn_tile(b);
            moves++;
            if (out) {
                if (moves > 1) fprintf(out, ",");
                fprintf(out, "{\"i\":%d,\"move\":\"%s\",\"gained\":%d,\"score\":%d,\"depth\":%d,\"grid_before\":",
                        moves, ch.dir == 0 ? "left" : ch.dir == 1 ? "right" : ch.dir == 2 ? "up" : "down",
                        gain, score, ch.depth);
                print_board_json(out, before);
                fprintf(out, ",\"grid_after\":");
                print_board_json(out, b);
                fprintf(out, "}");
            }
            for (int8_t v : b.c) mx = std::max(mx, (int)v);
            if (moves > 200000) break;
        }
        double secs = std::chrono::duration<double>(Clock::now() - t0).count();
        if (out) { fprintf(out, "]}"); fclose(out); printf("\n"); }
        sumScore += score; sumSec += secs;
        bestScore = std::max(bestScore, score);
        minScore = std::min(minScore, score);
        printf("{\"game\":%d,\"seed\":%llu,\"size\":%d,\"score\":%d,\"moves\":%d,\"max_tile\":%d,"
               "\"seconds\":%.1f,\"avg_depth\":%.2f}\n",
               g + 1, seed + g, g_cfg.n, score, moves, mx ? (1 << mx) : 0, secs,
               moves ? depthSum / moves : 0.0);
        fflush(stdout);
    }
    if (games > 1) {
        printf("{\"size\":%d,\"games\":%d,\"mean\":%.1f,\"min\":%d,\"max\":%d,\"avg_seconds\":%.1f}\n",
               g_cfg.n, games, sumScore / games, minScore, bestScore, sumSec / games);
    }
    return 0;
}
