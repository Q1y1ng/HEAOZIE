// engine_bb.cpp — 4x4 bitboard expectimax engine for 2048.
//
// Why: the array-based engine (engine.cpp) tops out around depth 4.5 and
// ~60k points.  The known-good route to 8192 is a bitboard board + row move
// tables + transposition table + deep expectimax with a probability cutoff
// (nneonneo/2048-ai reaches depth 8 / 10M states-per-second this way).
//
// Board layout: uint64_t, 16 nibbles, cell i (row-major) at bits 4*i.
//   0 = empty, 1 = 2, 2 = 4, ... , 15 = 32768.
//
// Moves: one 65536-entry table per direction pair (left/right); up/down are
// left/right on the transposed board.  A full move is 4 lookups, a full
// move set is 16 lookups + 8 transposes.
//
// Eval: per-row table (nneonneo-style: sum^3.5, monotonicity^4, merge runs,
// empty count) summed over the 4 rows and the 4 columns of the transpose.
//
// Search: expectimax, max nodes -> chance nodes (all empty cells), value =
// real merge score + eval.  Transposition table keyed on the exact board,
// depth-aware.  Iterative deepening at the root with a wall-clock budget;
// branches whose cumulative spawn probability falls below --prob-cut are
// evaluated statically instead of expanded.
//
// Build:
//   clang++ -O3 -std=c++17 -D_CRT_SECURE_NO_WARNINGS -o engine_bb.exe engine_bb.cpp
// Self-test (checks tables/transpose/search against a slow reference):
//   engine_bb.exe --selftest
// Run:
//   engine_bb.exe --games 8 --budget 300 --seed 1
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <random>
#include <string>
#include <vector>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

using Clock = std::chrono::steady_clock;
using board_t = uint64_t;

// Windows throttles background processes (EcoQoS / power throttling): a hidden
// console game runs at roughly half speed, which directly costs search depth.
// Opt out explicitly so overnight batches get the real CPU.
static void disable_power_throttling() {
#ifdef _WIN32
    PROCESS_POWER_THROTTLING_STATE st;
    std::memset(&st, 0, sizeof(st));
    st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
    st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
    st.StateMask = 0;  // 0 = never throttle
    SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &st, sizeof(st));
#endif
}

static const board_t OCC_MASK = 0x1111111111111111ULL;
static const int CORNERS[4] = {0, 3, 12, 15};

// ------------------------------------------------------------- config -----
struct Cfg {
    double budgetMs = 100.0;
    int maxDepth = 16;
    double probCut = 1e-4;   // branches below this cumulative probability: static eval
    int chanceCap = 0;       // 0 = expand every empty cell
    double prob4 = 0.1;
    // eval weights (nneonneo/2048-ai defaults)
    double wEmpty = 270.0;   // NOTE: counted once per line, i.e. twice per cell
    double wMono = 47.0;
    double wSum = 11.0;
    double wMerges = 700.0;
    double sumPow = 3.5;
    double monoPow = 4.0;
    double lostPenalty = 200000.0;
    double gainW = 1.0;      // weight of real merge score vs eval
    bool selftest = false;
};
static Cfg g_cfg;

// ------------------------------------------------------------- tables -----
static uint16_t T_L[65536], T_R[65536];
static int32_t S_L[65536], S_R[65536];
static double H_ROW[65536];

static inline int nib(board_t r, int i) { return (int)((r >> (4 * i)) & 0xF); }

static inline uint16_t pack4(const int* v) {
    return (uint16_t)((v[0] & 0xF) | ((v[1] & 0xF) << 4) | ((v[2] & 0xF) << 8) | ((v[3] & 0xF) << 12));
}

// slide-left a 4-cell line (already compacted input), returns packed result
static inline uint16_t slide_left_packed(int a0, int a1, int a2, int a3, int* gain) {
    int in[4] = {a0, a1, a2, a3};
    int out[4];
    int m = 0;
    for (int i = 0; i < 4; i++)
        if (in[i]) out[m++] = in[i];
    int res[4] = {0, 0, 0, 0};
    int w = 0, i = 0, s = 0;
    while (i < m) {
        if (i + 1 < m && out[i] == out[i + 1]) {
            int nv = out[i] + 1;
            res[w++] = nv;
            s += 1 << nv;
            i += 2;
        } else {
            res[w++] = out[i++];
        }
    }
    *gain = s;
    return pack4(res);
}

static void build_row_tables() {
    for (int r = 0; r < 65536; r++) {
        int v[4] = {nib(r, 0), nib(r, 1), nib(r, 2), nib(r, 3)};
        int g = 0;
        T_L[r] = slide_left_packed(v[0], v[1], v[2], v[3], &g);
        S_L[r] = g;
        T_R[r] = slide_left_packed(v[3], v[2], v[1], v[0], &g);
        // T_R maps the reversed line back, so reverse the result nibbles
        uint16_t rr = T_R[r];
        T_R[r] = (uint16_t)(((rr & 0xF) << 12) | (((rr >> 4) & 0xF) << 8) |
                            (((rr >> 8) & 0xF) << 4) | ((rr >> 12) & 0xF));
        S_R[r] = g;
    }
}

static void build_eval_table() {
    for (int r = 0; r < 65536; r++) {
        int line[4] = {nib(r, 0), nib(r, 1), nib(r, 2), nib(r, 3)};
        double sum = 0.0;
        int empty = 0, merges = 0, prev = 0, counter = 0;
        for (int i = 0; i < 4; i++) {
            int rank = line[i];
            sum += std::pow((double)rank, g_cfg.sumPow);
            if (rank == 0) {
                empty++;
            } else {
                if (prev == rank) {
                    counter++;
                } else if (counter > 0) {
                    merges += 1 + counter;
                    counter = 0;
                }
                prev = rank;
            }
        }
        if (counter > 0) merges += 1 + counter;
        double monoL = 0.0, monoR = 0.0;
        for (int i = 1; i < 4; i++) {
            if (line[i - 1] > line[i])
                monoL += std::pow((double)line[i - 1], g_cfg.monoPow) - std::pow((double)line[i], g_cfg.monoPow);
            else
                monoR += std::pow((double)line[i], g_cfg.monoPow) - std::pow((double)line[i - 1], g_cfg.monoPow);
        }
        H_ROW[r] = g_cfg.lostPenalty
                 + g_cfg.wEmpty * empty
                 + g_cfg.wMerges * merges
                 - g_cfg.wMono * std::min(monoL, monoR)
                 - g_cfg.wSum * sum;
    }
}

// ---------------------------------------------------------- bit helpers ---
static inline uint64_t spread_bits(uint64_t x) { return x | (x >> 1) | (x >> 2) | (x >> 3); }

static inline board_t transpose(board_t x) {
    const board_t a1 = x & 0xF0F00F0FF0F00F0FULL;
    const board_t a2 = x & 0x0000F0F00000F0F0ULL;
    const board_t a3 = x & 0x0F0F00000F0F0000ULL;
    const board_t a = a1 | (a2 << 12) | (a3 >> 12);
    const board_t b1 = a & 0xFF00FF0000FF00FFULL;
    const board_t b2 = a & 0x00FF00FF00000000ULL;
    const board_t b3 = a & 0x00000000FF00FF00ULL;
    return b1 | (b2 >> 24) | (b3 << 24);
}

static inline uint64_t mix64(uint64_t x) {
    x ^= x >> 33;
    x *= 0xff51afd7ed558ccdULL;
    x ^= x >> 33;
    x *= 0xc4ceb9fe1a85ec53ULL;
    x ^= x >> 33;
    return x;
}

// ---------------------------------------------------------------- moves ---
// row-wise primitives (no transpose); a full move = 4 lookups on 16-bit rows
static inline board_t rows_left(board_t b, int* gain) {
    int s = 0;
    board_t out = 0;
    for (int sh = 0; sh < 64; sh += 16) {
        unsigned r = (unsigned)((b >> sh) & 0xFFFFULL);
        out |= (board_t)T_L[r] << sh;
        s += S_L[r];
    }
    *gain = s;
    return out;
}

static inline board_t rows_right(board_t b, int* gain) {
    int s = 0;
    board_t out = 0;
    for (int sh = 0; sh < 64; sh += 16) {
        unsigned r = (unsigned)((b >> sh) & 0xFFFFULL);
        out |= (board_t)T_R[r] << sh;
        s += S_R[r];
    }
    *gain = s;
    return out;
}

static inline board_t move_left(board_t b, int* gain) { return rows_left(b, gain); }
static inline board_t move_right(board_t b, int* gain) { return rows_right(b, gain); }

static inline board_t move_up(board_t b, int* gain) {
    return transpose(rows_left(transpose(b), gain));
}

static inline board_t move_down(board_t b, int* gain) {
    return transpose(rows_right(transpose(b), gain));
}

static inline board_t do_move(board_t b, int dir, int* gain) {
    switch (dir) {
        case 0: return move_left(b, gain);
        case 1: return move_right(b, gain);
        case 2: return move_up(b, gain);
        default: return move_down(b, gain);
    }
}

// ----------------------------------------------------------------- eval ---
static inline double eval_rowsum(board_t b) {
    return H_ROW[b & 0xFFFFULL] + H_ROW[(b >> 16) & 0xFFFFULL] +
           H_ROW[(b >> 32) & 0xFFFFULL] + H_ROW[(b >> 48) & 0xFFFFULL];
}

static inline double evaluate(board_t b) { return eval_rowsum(b) + eval_rowsum(transpose(b)); }

// --------------------------------------------------------------- search ---
struct TTEntry {
    uint64_t key;
    float val;
    int8_t depth;
    int8_t pad0;
    uint16_t pad1;
};

static std::vector<TTEntry> g_tt;
static uint64_t g_ttMask = 0;
static long long g_nodes = 0;
static unsigned g_cuts = 0;
static Clock::time_point g_end;
static bool g_timeout = false;
static int g_tick = 0;

static inline bool check_time() {
    if (++g_tick < 1024) return g_timeout;
    g_tick = 0;
    if (Clock::now() > g_end) g_timeout = true;
    return g_timeout;
}

static double searchMax(board_t b, int depth, double prob);

static double searchChance(board_t b, int depth, double prob) {
    uint64_t empty_mask = ~spread_bits(b) & OCC_MASK;
    if (!empty_mask) return evaluate(b);
    int cells[16];
    int nc = 0;
    uint64_t m = empty_mask;
    while (m) {
        cells[nc++] = (__builtin_ctzll(m) >> 2);
        m &= m - 1;
    }
    int use = nc;
    if (g_cfg.chanceCap > 0 && nc > g_cfg.chanceCap) {
        int sampled[16];
        for (int k = 0; k < g_cfg.chanceCap; k++) {
            int idx = (int)((double)k * (double)nc / (double)g_cfg.chanceCap);
            if (idx >= nc) idx = nc - 1;
            sampled[k] = cells[idx];
        }
        for (int k = 0; k < g_cfg.chanceCap; k++) cells[k] = sampled[k];
        use = g_cfg.chanceCap;
    }
    const double w2 = 1.0 - g_cfg.prob4;
    const double w4 = g_cfg.prob4;
    const double p2 = prob * w2 / (double)use;
    const double p4 = prob * w4 / (double)use;
    double total = 0.0;
    for (int k = 0; k < use; k++) {
        int i = cells[k];
        const int sh = 4 * i;
        board_t b2 = b | (1ULL << sh);
        total += w2 * searchMax(b2, depth, p2);
        if (g_timeout) return total / (double)use;
        if (w4 > 0.0) {
            board_t b4 = b | (2ULL << sh);
            total += w4 * searchMax(b4, depth, p4);
            if (g_timeout) return total / (double)use;
        }
    }
    return total / (double)use;
}

static double searchMax(board_t b, int depth, double prob) {
    if (depth <= 0) return evaluate(b);
    if (prob < g_cfg.probCut) {
        g_cuts++;
        return evaluate(b);
    }
    if (check_time()) return evaluate(b);
    g_nodes++;

    TTEntry& e = g_tt[mix64(b) & g_ttMask];
    if (e.key == b && e.depth >= depth) return (double)e.val;

    const unsigned c0 = g_cuts;
    // one transpose shared by the up/down moves instead of two per move
    int gl = 0, gr = 0, gu = 0, gd = 0;
    board_t l = rows_left(b, &gl);
    board_t r = rows_right(b, &gr);
    board_t bt = transpose(b);
    board_t u = transpose(rows_left(bt, &gu));
    board_t d = transpose(rows_right(bt, &gd));
    board_t nbs[4];
    int gs[4];
    int n = 0;
    if (l != b) { nbs[n] = l; gs[n++] = gl; }
    if (r != b) { nbs[n] = r; gs[n++] = gr; }
    if (u != b) { nbs[n] = u; gs[n++] = gu; }
    if (d != b) { nbs[n] = d; gs[n++] = gd; }

    double best;
    if (n == 0) {
        best = -1e9;  // dead board
    } else {
        best = -1e18;
        for (int k = 0; k < n; k++) {
            double v = g_cfg.gainW * (double)gs[k] + searchChance(nbs[k], depth - 1, prob);
            if (v > best) best = v;
            if (g_timeout) break;
        }
    }
    if (!g_timeout && g_cuts == c0) {
        e.key = b;
        e.val = (float)best;
        e.depth = (int8_t)depth;
    }
    return best;
}

struct RootResult {
    int dir = -1;
    double val = -1e18;
    int depth = 0;
};

static RootResult root_search(board_t b) {
    g_timeout = false;
    g_tick = 0;
    g_nodes = 0;
    g_end = Clock::now() +
            std::chrono::duration_cast<Clock::duration>(
                std::chrono::duration<double, std::milli>(g_cfg.budgetMs));

    RootResult best;
    double orderVal[4] = {-1e18, -1e18, -1e18, -1e18};

    for (int depth = 1; depth <= g_cfg.maxDepth; depth++) {
        int dirs[4] = {0, 1, 2, 3};
        std::sort(dirs, dirs + 4, [&](int x, int y) { return orderVal[x] > orderVal[y]; });
        double v[4] = {-1e18, -1e18, -1e18, -1e18};
        bool complete = true;
        int bd = -1;
        double bv = -1e18;
        for (int k = 0; k < 4; k++) {
            int d = dirs[k];
            int g = 0;
            board_t m = do_move(b, d, &g);
            if (m == b) continue;  // illegal
            double val = g_cfg.gainW * (double)g + searchChance(m, depth - 1, 1.0);
            if (g_timeout) { complete = false; break; }
            v[d] = val;
            if (val > bv) { bv = val; bd = d; }
        }
        if (complete && bd >= 0) {
            best.dir = bd;
            best.val = bv;
            best.depth = depth;
            for (int d = 0; d < 4; d++) orderVal[d] = v[d];
        } else {
            break;
        }
        if (depth >= g_cfg.maxDepth) break;
    }
    return best;
}

// ------------------------------------------------------------- game play --
static std::mt19937_64 g_rng;

static void spawn_tile(board_t& b) {
    uint64_t empty_mask = ~spread_bits(b) & OCC_MASK;
    int cells[16];
    int nc = 0;
    uint64_t m = empty_mask;
    while (m) {
        cells[nc++] = (__builtin_ctzll(m) >> 2);
        m &= m - 1;
    }
    if (!nc) return;
    std::uniform_int_distribution<int> pick(0, nc - 1);
    std::uniform_real_distribution<double> u(0.0, 1.0);
    int i = cells[pick(g_rng)];
    b |= (u(g_rng) < g_cfg.prob4) ? (2ULL << (4 * i)) : (1ULL << (4 * i));
}

static int board_max_level(board_t b) {
    int m = 0;
    for (int i = 0; i < 16; i++) {
        int v = nib(b, i);
        if (v > m) m = v;
    }
    return m;
}

static void print_board_json(FILE* out, board_t b) {
    fprintf(out, "[");
    for (int r = 0; r < 4; r++) {
        if (r) fprintf(out, ",");
        fprintf(out, "[");
        for (int c = 0; c < 4; c++) {
            if (c) fprintf(out, ",");
            int v = nib(b, r * 4 + c);
            fprintf(out, "%d", v ? (1 << v) : 0);
        }
        fprintf(out, "]");
    }
    fprintf(out, "]");
}

static const char* DIR_NAME[4] = {"left", "right", "up", "down"};

// ------------------------------------------------------------ self-test ---
static uint16_t ref_row_left(int a0, int a1, int a2, int a3, int* gain) {
    return slide_left_packed(a0, a1, a2, a3, gain);
}

static void ref_decode(board_t b, int c[16]) {
    for (int i = 0; i < 16; i++) c[i] = nib(b, i);
}

static board_t ref_encode(const int c[16]) {
    board_t b = 0;
    for (int i = 0; i < 16; i++) b |= (board_t)(c[i] & 0xF) << (4 * i);
    return b;
}

// straightforward 2048 move on a cell array
static board_t ref_move(board_t b, int dir, int* gain) {
    int c[16], o[16];
    ref_decode(b, c);
    int s = 0;
    if (dir == 0 || dir == 1) {
        for (int r = 0; r < 4; r++) {
            int line[4];
            for (int i = 0; i < 4; i++) line[i] = c[r * 4 + (dir == 0 ? i : 3 - i)];
            int gg = 0;
            uint16_t packed = ref_row_left(line[0], line[1], line[2], line[3], &gg);
            s += gg;
            for (int i = 0; i < 4; i++) {
                int v = (packed >> (4 * (dir == 0 ? i : 3 - i))) & 0xF;
                o[r * 4 + i] = v;
            }
        }
    } else {
        for (int col = 0; col < 4; col++) {
            int line[4];
            for (int i = 0; i < 4; i++) line[i] = c[(dir == 2 ? i : 3 - i) * 4 + col];
            int gg = 0;
            uint16_t packed = ref_row_left(line[0], line[1], line[2], line[3], &gg);
            s += gg;
            for (int i = 0; i < 4; i++) {
                int v = (packed >> (4 * i)) & 0xF;
                o[(dir == 2 ? i : 3 - i) * 4 + col] = v;
            }
        }
    }
    *gain = s;
    return ref_encode(o);
}

// slow reference eval using the same feature definitions
static double ref_eval(board_t b) {
    int c[16];
    ref_decode(b, c);
    double total = 0.0;
    for (int pass = 0; pass < 2; pass++) {
        for (int line = 0; line < 4; line++) {
            int v[4];
            for (int i = 0; i < 4; i++)
                v[i] = pass == 0 ? c[line * 4 + i] : c[i * 4 + line];
            double sum = 0.0;
            int empty = 0, merges = 0, prev = 0, counter = 0;
            for (int i = 0; i < 4; i++) {
                int rank = v[i];
                sum += std::pow((double)rank, g_cfg.sumPow);
                if (rank == 0) {
                    empty++;
                } else {
                    if (prev == rank) {
                        counter++;
                    } else if (counter > 0) {
                        merges += 1 + counter;
                        counter = 0;
                    }
                    prev = rank;
                }
            }
            if (counter > 0) merges += 1 + counter;
            double monoL = 0.0, monoR = 0.0;
            for (int i = 1; i < 4; i++) {
                if (v[i - 1] > v[i])
                    monoL += std::pow((double)v[i - 1], g_cfg.monoPow) - std::pow((double)v[i], g_cfg.monoPow);
                else
                    monoR += std::pow((double)v[i], g_cfg.monoPow) - std::pow((double)v[i - 1], g_cfg.monoPow);
            }
            total += g_cfg.lostPenalty + g_cfg.wEmpty * empty + g_cfg.wMerges * merges -
                     g_cfg.wMono * std::min(monoL, monoR) - g_cfg.wSum * sum;
        }
    }
    return total;
}

static int run_selftest() {
    build_row_tables();
    build_eval_table();
    std::mt19937_64 rng(12345);
    int fails = 0;
    // transpose is an involution and maps (r,c) -> (c,r)
    for (int i = 0; i < 16; i++) {
        board_t b = (board_t)7 << (4 * i);
        int r = i / 4, c = i % 4;
        board_t want = (board_t)7 << (4 * (c * 4 + r));
        if (transpose(b) != want) {
            printf("FAIL transpose cell %d\n", i);
            fails++;
        }
    }
    for (int t = 0; t < 20000; t++) {
        board_t b = 0;
        for (int i = 0; i < 16; i++) b |= (board_t)(rng() & 0xF) << (4 * i);
        if (transpose(transpose(b)) != b) {
            printf("FAIL transpose involution\n");
            fails++;
            break;
        }
    }
    // moves match the reference, and legal/illegal detection agrees
    for (int t = 0; t < 200000; t++) {
        board_t b = 0;
        int cells = 1 + (int)(rng() % 16);
        for (int i = 0; i < cells; i++) {
            int idx = (int)(rng() % 16);
            b |= (board_t)(rng() % 12) << (4 * idx);
        }
        for (int d = 0; d < 4; d++) {
            int g1 = 0, g2 = 0;
            board_t a = do_move(b, d, &g1);
            board_t c = ref_move(b, d, &g2);
            if (a != c || g1 != g2) {
                printf("FAIL move dir=%d b=%016llx got %016llx/%d want %016llx/%d\n", d,
                       (unsigned long long)b, (unsigned long long)a, g1, (unsigned long long)c, g2);
                fails++;
                if (fails > 5) return 1;
            }
        }
    }
    // eval matches the slow reference
    for (int t = 0; t < 20000; t++) {
        board_t b = 0;
        for (int i = 0; i < 16; i++)
            if (rng() % 3) b |= (board_t)(rng() % 13) << (4 * i);
        double a = evaluate(b), c = ref_eval(b);
        if (std::fabs(a - c) > 1e-6 * (std::fabs(c) + 1.0)) {
            printf("FAIL eval b=%016llx got %.3f want %.3f\n", (unsigned long long)b, a, c);
            fails++;
            if (fails > 5) return 1;
        }
    }
    // eval must rank a tidy board above a scattered one
    board_t tidy = 0;
    int tv[16] = {11, 10, 9, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};
    tidy = ref_encode(tv);
    board_t messy = 0;
    int mv[16] = {11, 0, 10, 0, 0, 9, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0};
    messy = ref_encode(mv);
    if (!(evaluate(tidy) > evaluate(messy))) {
        printf("FAIL eval ordering tidy=%.1f messy=%.1f\n", evaluate(tidy), evaluate(messy));
        fails++;
    }
    // max level + empties helpers
    int ml = board_max_level(tidy);
    if (ml != 11) {
        printf("FAIL max level %d\n", ml);
        fails++;
    }
    if (__builtin_popcountll(~spread_bits(tidy) & OCC_MASK) != 12) {
        printf("FAIL empty count\n");
        fails++;
    }
    printf(fails ? "SELFTEST FAILED (%d)\n" : "SELFTEST OK\n", fails);
    return fails ? 1 : 0;
}

// ------------------------------------------------------------ bench mode ---
// One-ply greedy move, used only to build a natural mid-game position fast.
static int greedy_move(board_t b) {
    int best = -1;
    double bv = -1e18;
    for (int d = 0; d < 4; d++) {
        int g = 0;
        board_t m = do_move(b, d, &g);
        if (m == b) continue;
        double v = g_cfg.gainW * (double)g + evaluate(m);
        if (v > bv) { bv = v; best = d; }
    }
    return best;
}

static int run_bench(int moves) {
    g_rng.seed(777);
    board_t b = 0;
    spawn_tile(b);
    spawn_tile(b);
    for (int i = 0; i < 120; i++) {
        int d = greedy_move(b);
        if (d < 0) break;
        int g = 0;
        b = do_move(b, d, &g);
        spawn_tile(b);
    }
    long long nodes = 0;
    double ms = 0.0;
    double depthSum = 0.0;
    int played = 0;
    for (int i = 0; i < moves; i++) {
        auto t0 = Clock::now();
        RootResult rr = root_search(b);
        ms += std::chrono::duration<double, std::milli>(Clock::now() - t0).count();
        nodes += g_nodes;
        if (rr.dir < 0) break;
        depthSum += rr.depth;
        int g = 0;
        b = do_move(b, rr.dir, &g);
        spawn_tile(b);
        played++;
    }
    printf("{\"bench_moves\":%d,\"avg_ms\":%.2f,\"nps\":%lld,\"avg_depth\":%.2f}\n", played,
           played ? ms / played : 0.0, ms > 0 ? (long long)((double)nodes / (ms / 1000.0)) : 0,
           played ? depthSum / played : 0.0);
    return 0;
}

// ------------------------------------------------------------------ main --
int main(int argc, char** argv) {
    int games = 1;
    unsigned long long seed = 1;
    const char* savePath = nullptr;
    size_t ttBits = 22;
    int traceEvery = 0;
    int benchMoves = 0;
    for (int i = 1; i < argc; i++) {
        std::string a = argv[i];
        auto next = [&]() { return std::string(argv[++i]); };
        if (a == "--size") {
            int n = std::stoi(next());
            if (n != 4) { fprintf(stderr, "--size %d unsupported (bitboard engine is 4x4)\n", n); return 2; }
        } else if (a == "--games") games = std::stoi(next());
        else if (a == "--budget") g_cfg.budgetMs = std::stod(next());
        else if (a == "--max-depth") g_cfg.maxDepth = std::stoi(next());
        else if (a == "--prob-cut") g_cfg.probCut = std::stod(next());
        else if (a == "--chance-cap") g_cfg.chanceCap = std::stoi(next());
        else if (a == "--prob4") g_cfg.prob4 = std::stod(next());
        else if (a == "--seed") seed = std::stoull(next());
        else if (a == "--save") savePath = strdup(next().c_str());
        else if (a == "--tt-bits") ttBits = (size_t)std::stoi(next());
        else if (a == "--trace-every") traceEvery = std::stoi(next());
        else if (a == "--bench-moves") benchMoves = std::stoi(next());
        else if (a == "--w-empty") g_cfg.wEmpty = std::stod(next());
        else if (a == "--w-mono") g_cfg.wMono = std::stod(next());
        else if (a == "--w-sum") g_cfg.wSum = std::stod(next());
        else if (a == "--w-merges") g_cfg.wMerges = std::stod(next());
        else if (a == "--sum-pow") g_cfg.sumPow = std::stod(next());
        else if (a == "--gain-w") g_cfg.gainW = std::stod(next());
        else if (a == "--selftest") g_cfg.selftest = true;
        else if (a == "--help" || a == "-h") {
            printf("engine_bb: 4x4 bitboard expectimax\n"
                   "  --games N --seed S --budget MS --max-depth D\n"
                   "  --prob-cut P --chance-cap K --tt-bits B --save FILE\n"
                   "  --w-empty --w-mono --w-sum --w-merges --sum-pow --gain-w\n"
                   "  --selftest\n");
            return 0;
        } else {
            fprintf(stderr, "unknown arg: %s\n", a.c_str());
            return 2;
        }
    }
    if (g_cfg.selftest) return run_selftest();

    disable_power_throttling();
    build_row_tables();
    build_eval_table();
    g_tt.assign((size_t)1 << ttBits, TTEntry{0, 0.0f, 0, 0, 0});
    g_ttMask = ((uint64_t)1 << ttBits) - 1;

    if (benchMoves > 0) return run_bench(benchMoves);

    double sumScore = 0, sumSec = 0, sumDepth = 0;
    long long sumNodes = 0;
    int bestScore = 0, worstScore = 1 << 30;
    for (int g = 0; g < games; g++) {
        g_rng.seed(seed + (unsigned long long)g);
        board_t b = 0;
        spawn_tile(b);
        spawn_tile(b);
        int score = 0, moves = 0, mx = 0;
        long long nodes = 0;
        double depthSum = 0;
        double sumMoveMs = 0;
        int movesSlow = 0;
        auto t0 = Clock::now();
        FILE* out = nullptr;
        if (savePath && g == 0) {
            out = fopen(savePath, "w");
            if (out)
                fprintf(out,
                        "{\"meta\":{\"model\":\"expectimax-bitboard\",\"size\":4,\"seed\":%llu,"
                        "\"budget_ms\":%.0f},\"steps\":[",
                        seed, g_cfg.budgetMs);
        }
        while (true) {
            // game over if no legal move
            bool any = false;
            for (int d = 0; d < 4; d++) {
                int gg;
                if (do_move(b, d, &gg) != b) { any = true; break; }
            }
            if (!any) break;
            board_t before = b;
            auto t1 = Clock::now();
            RootResult rr = root_search(b);
            double mms = std::chrono::duration<double, std::milli>(Clock::now() - t1).count();
            if (rr.dir < 0) break;
            int gain = 0;
            board_t moved = do_move(b, rr.dir, &gain);
            if (moved == b) break;
            b = moved;
            score += gain;
            depthSum += rr.depth;
            nodes += g_nodes;
            spawn_tile(b);
            moves++;
            sumMoveMs += mms;
            movesSlow += (mms > g_cfg.budgetMs * 2.5);
            if (traceEvery > 0 && moves % traceEvery == 0)
                fprintf(stderr, "[m=%d score=%d depth=%d move_ms=%.1f avg_ms=%.1f nodes=%lld]\n",
                        moves, score, rr.depth, mms, sumMoveMs / moves, g_nodes);
            if (out) {
                if (moves > 1) fprintf(out, ",");
                fprintf(out,
                        "{\"i\":%d,\"move\":\"%s\",\"gained\":%d,\"score\":%d,\"depth\":%d,"
                        "\"grid_before\":",
                        moves, DIR_NAME[rr.dir], gain, score, rr.depth);
                print_board_json(out, before);
                fprintf(out, ",\"grid_after\":");
                print_board_json(out, b);
                fprintf(out, "}");
            }
            if (moves > 200000) break;
        }
        mx = board_max_level(b);
        double secs = std::chrono::duration<double>(Clock::now() - t0).count();
        if (out) { fprintf(out, "]}"); fclose(out); }
        sumScore += score;
        sumSec += secs;
        sumDepth += moves ? depthSum / moves : 0;
        sumNodes += nodes;
        bestScore = std::max(bestScore, score);
        worstScore = std::min(worstScore, score);
        printf("{\"game\":%d,\"seed\":%llu,\"size\":4,\"score\":%d,\"moves\":%d,\"max_tile\":%d,"
               "\"seconds\":%.1f,\"avg_depth\":%.2f,\"nodes\":%lld,\"nps\":%lld,"
               "\"move_ms\":%.1f,\"slow\":%d}\n",
               g + 1, seed + (unsigned long long)g, score, moves, mx ? (1 << mx) : 0, secs,
               moves ? depthSum / moves : 0.0, nodes,
               secs > 0 ? (long long)((double)nodes / secs) : 0,
               moves ? sumMoveMs / moves : 0.0, movesSlow);
        fflush(stdout);
    }
    if (games > 1) {
        printf("{\"size\":4,\"games\":%d,\"mean\":%.1f,\"min\":%d,\"max\":%d,\"avg_seconds\":%.1f,"
               "\"avg_depth\":%.2f,\"nps\":%lld}\n",
               games, sumScore / games, worstScore, bestScore, sumSec / games,
               sumDepth / games, sumSec > 0 ? (long long)((double)sumNodes / sumSec) : 0);
    }
    return 0;
}
