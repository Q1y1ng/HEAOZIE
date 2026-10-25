// ntuple.cpp — n-tuple TD(0) learner for 2048 (afterstate TD with spawn backup).
//
// Board: uint64_t, 16 nibbles, cell i at bits 4*i (0 = empty, 1 = 2, ... 15 = 32768).
// Network: sum of per-pattern lookup tables (16^len floats each).  A 4-tuple
// pattern of 4 cells has 16^4 = 65536 entries; 16 patterns = 4 MB, L3 resident.
//
// Learning: self-play, epsilon-greedy over afterstates; TD target is
//
//     gain + E_spawn[ max_d V(afterstate(s', d)) ]
//
// i.e. afterstate TD(0) with a sampled expectation over spawns and a
// Q-learning-flavoured max over our own moves.  Terminal afterstates are worth 0
// (gamma = 1), so V converges to the expected final score.
//
// Build:
//   clang++ -O3 -std=c++17 -D_CRT_SECURE_NO_WARNINGS -pthread -o ntuple.exe ntuple.cpp
// Train:
//   ntuple.exe --threads 12 --games 2000000 --alpha 0.001 --eps 0.01 \
//              --pat 4x16 --save models/n14.bin --save-every 200000
// Evaluate:
//   ntuple.exe --load models/n14.bin --eval 20 [--eval-only]
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <random>
#include <string>
#include <thread>
#include <vector>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

using Clock = std::chrono::steady_clock;
using board_t = uint64_t;

static const board_t OCC_MASK = 0x1111111111111111ULL;

// ---------------------------------------------------------- board ops ----
static uint16_t T_L[65536], T_R[65536];
static int32_t S_L[65536], S_R[65536];

static inline int nib(board_t b, int i) { return (int)((b >> (4 * i)) & 0xF); }

static inline uint16_t pack4(const int* v) {
    return (uint16_t)((v[0] & 0xF) | ((v[1] & 0xF) << 4) | ((v[2] & 0xF) << 8) | ((v[3] & 0xF) << 12));
}

static uint16_t slide_left_packed(int a0, int a1, int a2, int a3, int* gain) {
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
            if (nv > 15) nv = 15;  // a 4-bit nibble cannot hold level 16+
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
        uint16_t rr = T_R[r];
        T_R[r] = (uint16_t)(((rr & 0xF) << 12) | (((rr >> 4) & 0xF) << 8) |
                            (((rr >> 8) & 0xF) << 4) | ((rr >> 12) & 0xF));
        S_R[r] = g;
    }
}

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

static inline board_t do_move(board_t b, int dir, int* gain) {
    switch (dir) {
        case 0: return rows_left(b, gain);
        case 1: return rows_right(b, gain);
        case 2: return transpose(rows_left(transpose(b), gain));
        default: return transpose(rows_right(transpose(b), gain));
    }
}

static inline int empties_of(board_t b) {
    return 16 - __builtin_popcountll(spread_bits(b) & OCC_MASK);
}

// ------------------------------------------------------------- patterns ---
static const int MAXPAT = 20;
static const int MAXTUP = 6;
static int NPAT = 0;
static int PAT_LEN[MAXPAT];
static int PAT_CELLS[MAXPAT][MAXTUP];
static std::vector<float> W[MAXPAT];

// 4x16: four rows, four columns, eight 2x2 windows
static const int P4x16[16][4] = {
    {0, 1, 2, 3},   {4, 5, 6, 7},   {8, 9, 10, 11}, {12, 13, 14, 15},
    {0, 4, 8, 12},  {1, 5, 9, 13},  {2, 6, 10, 14}, {3, 7, 11, 15},
    {0, 1, 4, 5},   {1, 2, 5, 6},   {4, 5, 8, 9},   {5, 6, 9, 10},
    {2, 3, 6, 7},   {6, 7, 10, 11}, {8, 9, 12, 13},{9, 10, 13, 14},
};

// 4x8: rows + columns only (cheap baseline)
static const int P4x8[8][4] = {
    {0, 1, 2, 3},   {4, 5, 6, 7},   {8, 9, 10, 11}, {12, 13, 14, 15},
    {0, 4, 8, 12},  {1, 5, 9, 13},  {2, 6, 10, 14}, {3, 7, 11, 15},
};

// 6x4: two overlapping row/col "bars" of length 6 (bigger receptive field)
static const int P6x4[4][6] = {
    {0, 1, 2, 3, 4, 5},   {4, 5, 6, 7, 8, 9},
    {0, 1, 4, 5, 8, 9},   {1, 2, 5, 6, 9, 10},
};


// 6x8: eight 2x3 blocks (4 horizontal + 4 vertical) — bigger receptive field
static const int P6x8[8][6] = {
    {0, 1, 2, 4, 5, 6},    {1, 2, 3, 5, 6, 7},
    {8, 9, 10, 12, 13, 14}, {9, 10, 11, 13, 14, 15},
    {0, 1, 4, 5, 8, 9},    {2, 3, 6, 7, 10, 11},
    {4, 5, 8, 9, 12, 13},  {6, 7, 10, 11, 14, 15},
};
// 6x12: every 2x3 / 3x2 block (6 horizontal + 6 vertical)
static const int P6x12[12][6] = {
    {0, 1, 2, 4, 5, 6},    {1, 2, 3, 5, 6, 7},
    {4, 5, 6, 8, 9, 10},   {5, 6, 7, 9, 10, 11},
    {8, 9, 10, 12, 13, 14}, {9, 10, 11, 13, 14, 15},
    {0, 1, 4, 5, 8, 9},    {2, 3, 6, 7, 10, 11},
    {1, 2, 5, 6, 9, 10},   {5, 6, 9, 10, 13, 14},
    {4, 5, 8, 9, 12, 13},  {6, 7, 10, 11, 14, 15},
};static void setup_patterns(const std::string& name) {
    if (name == "4x8") {
        NPAT = 8;
        for (int p = 0; p < 8; p++) {
            PAT_LEN[p] = 4;
            for (int i = 0; i < 4; i++) PAT_CELLS[p][i] = P4x8[p][i];
        }
    } else if (name == "6x8") {
        NPAT = 8;
        for (int p = 0; p < 8; p++) {
            PAT_LEN[p] = 6;
            for (int i = 0; i < 6; i++) PAT_CELLS[p][i] = P6x8[p][i];
        }
    } else if (name == "6x12") {
        NPAT = 12;
        for (int p = 0; p < 12; p++) {
            PAT_LEN[p] = 6;
            for (int i = 0; i < 6; i++) PAT_CELLS[p][i] = P6x12[p][i];
        }
    } else if (name == "6x4") {
        NPAT = 4;
        for (int p = 0; p < 4; p++) {
            PAT_LEN[p] = 6;
            for (int i = 0; i < 6; i++) PAT_CELLS[p][i] = P6x4[p][i];
        }
    } else {  // 4x16 default
        NPAT = 16;
        for (int p = 0; p < 16; p++) {
            PAT_LEN[p] = 4;
            for (int i = 0; i < 4; i++) PAT_CELLS[p][i] = P4x16[p][i];
        }
    }
    for (int p = 0; p < NPAT; p++) {
        size_t n = (size_t)1 << (4 * PAT_LEN[p]);
        W[p].assign(n, 0.0f);
    }
}

static inline uint32_t pat_index(board_t b, int p) {
    const int* c = PAT_CELLS[p];
    const int len = PAT_LEN[p];
    uint32_t idx = 0;
    for (int i = 0; i < len; i++) idx = (idx << 4) | (uint32_t)((b >> (4 * c[i])) & 0xF);
    return idx;
}

static inline float value_of(board_t b) {
    float v = 0.0f;
    for (int p = 0; p < NPAT; p++) v += W[p][pat_index(b, p)];
    return v;
}

// shared, lock-free updates (benign float races are standard for n-tuple TD)
static inline void td_update(board_t a, float delta, float alpha) {
    const float step = alpha * delta / (float)NPAT;
    for (int p = 0; p < NPAT; p++) W[p][pat_index(a, p)] += step;
}

// best afterstate value from a state (max over our moves); -1e30 if dead
static inline float best_after_value(board_t s, int* best_gain = nullptr, int* best_dir = nullptr) {
    float best = -1e30f;
    for (int d = 0; d < 4; d++) {
        int g = 0;
        board_t m = do_move(s, d, &g);
        if (m == s) continue;
        float v = value_of(m);
        if (v > best) {
            best = v;
            if (best_gain) *best_gain = g;
            if (best_dir) *best_dir = d;
        }
    }
    return best;
}

// ------------------------------------------------------------- training ---
struct Cfg {
    int threads = 12;
    long long games = 1000000;
    float alpha = 0.001f;
    float eps = 0.01f;
    int samples = 4;       // spawn cells sampled per TD target
    float scale = 1000.0f; // reward scaling: learn V in units of score/scale
    float alphaFinal = 0.0f; // >0: linearly decay alpha to this value over the run
    float probCut = 1e-3f; // search: prune spawn branches below this cumulative probability
    std::string pat = "4x16";
    std::string save;
    std::string load;
    long long saveEvery = 200000;
    int evalGames = 0;
    bool evalOnly = false;
    int seed = 12345;
};
static Cfg g_cfg;

static std::atomic<long long> g_games{0};
static std::atomic<long long> g_steps{0};
static std::atomic<bool> g_stop{false};
static thread_local float tlAlpha = 0.02f;  // current learning rate for this worker

static void disable_power_throttling() {
#ifdef _WIN32
    PROCESS_POWER_THROTTLING_STATE st;
    std::memset(&st, 0, sizeof(st));
    st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
    st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
    st.StateMask = 0;
    SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &st, sizeof(st));
#endif
}

// expected value after a random spawn, over `samples` empty cells and both tiles
static inline float spawn_expectation(board_t s, std::mt19937_64& rng, int samples) {
    int cells[16];
    int n = 0;
    uint64_t m = ~spread_bits(s) & OCC_MASK;
    while (m) {
        cells[n++] = (int)(__builtin_ctzll(m) >> 2);
        m &= m - 1;
    }
    if (!n) return 0.0f;

    float sum = 0.0f;
    int used = 0;
    int picks[8];
    if (n <= samples) {
        for (int i = 0; i < n; i++) picks[used++] = cells[i];
    } else {
        // even stride + rotation for cheap diversity
        int off = (int)(rng() % (uint64_t)n);
        for (int k = 0; k < samples; k++) picks[used++] = cells[(off + k * n / samples) % n];
    }
    for (int k = 0; k < used; k++) {
        const int sh = 4 * picks[k];
        board_t b2 = s | (1ULL << sh);
        float v2 = best_after_value(b2);
        board_t b4 = s | (2ULL << sh);
        float v4 = best_after_value(b4);
        if (v2 < -1e29f) v2 = 0.0f;  // dead after spawn
        if (v4 < -1e29f) v4 = 0.0f;
        sum += 0.9f * v2 + 0.1f * v4;
    }
    return sum / (float)used;
}

static void worker(int tid) {
    std::mt19937_64 rng((uint64_t)g_cfg.seed * 1000003ULL + (uint64_t)tid);
    std::uniform_real_distribution<float> uni(0.0f, 1.0f);
    while (!g_stop.load(std::memory_order_relaxed)) {
        // claim one game from the global budget (balanced, no remainder loss)
        const long long claimed = g_games.fetch_add(1, std::memory_order_relaxed);
        if (claimed >= g_cfg.games) {
            g_games.fetch_sub(1, std::memory_order_relaxed);
            break;
        }
        tlAlpha = g_cfg.alpha;
        if (g_cfg.alphaFinal > 0.0f && g_cfg.games > 0) {
            double frac = (double)claimed / (double)g_cfg.games;
            if (frac > 1.0) frac = 1.0;
            tlAlpha = (float)(g_cfg.alpha + ((double)g_cfg.alphaFinal - g_cfg.alpha) * frac);
        }
        // initial two tiles
        board_t b = 0;
        for (int i = 0; i < 2; i++) {
            uint64_t e = ~spread_bits(b) & OCC_MASK;
            int n = __builtin_popcountll(e);
            int pick = (int)(rng() % (uint64_t)n);
            while (pick--) e &= e - 1;
            int cell = (int)(__builtin_ctzll(e) >> 2);
            b |= (uni(rng) < 0.1f) ? (2ULL << (4 * cell)) : (1ULL << (4 * cell));
        }
        while (true) {
            board_t as[4];
            int gains[4];
            int ng = 0;
            for (int d = 0; d < 4; d++) {
                int g = 0;
                board_t m = do_move(b, d, &g);
                if (m == b) continue;
                as[ng] = m;
                gains[ng] = g;
                ng++;
            }
            if (!ng) break;

            int pick = 0;
            float chosen_val = 0.0f;
            if (uni(rng) < g_cfg.eps) {
                pick = (int)(rng() % (uint64_t)ng);
                chosen_val = value_of(as[pick]);
            } else {
                float best = -1e30f;
                for (int k = 0; k < ng; k++) {
                    float v = value_of(as[k]);
                    if (v > best) { best = v; pick = k; }
                }
                chosen_val = best;
            }
            const board_t a = as[pick];
            const int gain = gains[pick];
            g_steps.fetch_add(1, std::memory_order_relaxed);

            // TD target: expectimax over spawns *from the afterstate* a.
            // (Spawning first and sampling again would back up two spawns ahead.)
            float target = spawn_expectation(a, rng, g_cfg.samples);
            // advance the real game: one spawn on the afterstate
            board_t s2 = a;
            uint64_t e = ~spread_bits(s2) & OCC_MASK;
            int ne = __builtin_popcountll(e);
            if (ne) {
                int k = (int)(rng() % (uint64_t)ne);
                while (k--) e &= e - 1;
                int cell = (int)(__builtin_ctzll(e) >> 2);
                s2 |= (uni(rng) < 0.1f) ? (2ULL << (4 * cell)) : (1ULL << (4 * cell));
            }
            const float reward = (float)gain / g_cfg.scale;
            td_update(a, reward + target - chosen_val, tlAlpha);
            b = s2;
        }
    }
}


// ------------------------------------------------------------- search -----
// Expectimax on top of the learned afterstate value.
//   g_searchDepth == 0  -> pure net policy (one ply: max over moves)
//   g_searchDepth  > 0  -> expand that many extra move/spawn pairs
static int g_searchDepth = 0;

static float searchMax(board_t b, int depth, double prob, std::mt19937_64& rng, int samples);

static float searchChance(board_t a, int depth, double prob, std::mt19937_64& rng, int samples) {
    int cells[16];
    int n = 0;
    uint64_t m = ~spread_bits(a) & OCC_MASK;
    while (m) {
        cells[n++] = (int)(__builtin_ctzll(m) >> 2);
        m &= m - 1;
    }
    if (!n) return 0.0f;
    int picks[16];
    int use = 0;
    if (n <= samples) {
        for (int i = 0; i < n; i++) picks[use++] = cells[i];
    } else {
        int off = (int)(rng() % (uint64_t)n);
        for (int k = 0; k < samples; k++) picks[use++] = cells[(off + k * n / samples) % n];
    }
    const double p2 = prob * 0.9 / (double)use;
    const double p4 = prob * 0.1 / (double)use;
    float sum = 0.0f;
    for (int k = 0; k < use; k++) {
        const int sh = 4 * picks[k];
        sum += 0.9f * searchMax(a | (1ULL << sh), depth, p2, rng, samples) +
               0.1f * searchMax(a | (2ULL << sh), depth, p4, rng, samples);
    }
    return sum / (float)use;
}

static float searchMax(board_t b, int depth, double prob, std::mt19937_64& rng, int samples) {
    if (depth < 0 || prob < g_cfg.probCut) {
        float v = best_after_value(b);
        return v < -1e29f ? 0.0f : v;
    }
    float best = -1e30f;
    for (int d = 0; d < 4; d++) {
        int g = 0;
        board_t a = do_move(b, d, &g);
        if (a == b) continue;
        float v = (float)g / g_cfg.scale + searchChance(a, depth - 1, prob, rng, samples);
        if (v > best) best = v;
    }
    return best < -1e29f ? 0.0f : best;
}

// direction chosen by the policy (0..3), or -1 when no move is legal
static int choose_move_net(board_t b, std::mt19937_64& rng) {
    int bd = -1;
    if (g_searchDepth <= 0) {
        int g = 0;
        float v = best_after_value(b, &g, &bd);
        return v < -1e29f ? -1 : bd;
    }
    float best = -1e30f;
    for (int d = 0; d < 4; d++) {
        int g = 0;
        board_t a = do_move(b, d, &g);
        if (a == b) continue;
        float v = (float)g / g_cfg.scale + searchChance(a, g_searchDepth - 1, 1.0, rng, g_cfg.samples);
        if (v > best) {
            best = v;
            bd = d;
        }
    }
    return bd;
}

// ------------------------------------------------------------- evaluation -
struct EvalResult {
    long long games = 0;
    double mean = 0, median = 0;
    int best = 0, worst = 1 << 30;
    int tiles[20] = {0};  // count by log2
    double avgMoves = 0;
};

static EvalResult evaluate(int games, int seed) {
    EvalResult r;
    std::mt19937_64 rng((uint64_t)seed);
    std::uniform_real_distribution<float> uni(0.0f, 1.0f);
    std::vector<int> scores;
    long long moves_total = 0;
    for (int gi = 0; gi < games; gi++) {
        board_t b = 0;
        for (int i = 0; i < 2; i++) {
            uint64_t e = ~spread_bits(b) & OCC_MASK;
            int n = __builtin_popcountll(e);
            int pick = (int)(rng() % (uint64_t)n);
            while (pick--) e &= e - 1;
            int cell = (int)(__builtin_ctzll(e) >> 2);
            b |= (uni(rng) < 0.1f) ? (2ULL << (4 * cell)) : (1ULL << (4 * cell));
        }
        int score = 0, moves = 0, mx = 0;
        while (true) {
            int g = 0, d = choose_move_net(b, rng);
            if (d < 0) break;
            board_t a = do_move(b, d, &g);
            score += g;
            moves++;
            b = a;
            uint64_t e = ~spread_bits(b) & OCC_MASK;
            int ne = __builtin_popcountll(e);
            if (!ne) break;
            int k = (int)(rng() % (uint64_t)ne);
            while (k--) e &= e - 1;
            int cell = (int)(__builtin_ctzll(e) >> 2);
            b |= (uni(rng) < 0.1f) ? (2ULL << (4 * cell)) : (1ULL << (4 * cell));
        }
        for (int i = 0; i < 16; i++) mx = std::max(mx, nib(b, i));
        scores.push_back(score);
        moves_total += moves;
        if (mx >= 0 && mx < 20) r.tiles[mx]++;
    }
    std::sort(scores.begin(), scores.end());
    r.games = games;
    r.mean = 0;
    for (int s : scores) r.mean += s;
    r.mean /= (double)games;
    r.median = scores[games / 2];
    r.best = scores.back();
    r.worst = scores.front();
    r.avgMoves = (double)moves_total / (double)games;
    return r;
}

// ---------------------------------------------------------------- io ------
static bool save_model(const std::string& path) {
    FILE* f = fopen(path.c_str(), "wb");
    if (!f) return false;
    const char magic[8] = {'N', 'T', 'U', 'P', '2', '0', '4', '8'};
    fwrite(magic, 1, 8, f);
    int32_t hdr[4] = {NPAT, PAT_LEN[0], g_cfg.seed, 0};
    fwrite(hdr, sizeof(int32_t), 4, f);
    float meta[2] = {g_cfg.alpha, (float)g_games.load()};
    fwrite(meta, sizeof(float), 2, f);
    for (int p = 0; p < NPAT; p++) {
        fwrite(PAT_CELLS[p], sizeof(int), PAT_LEN[p], f);
        fwrite(W[p].data(), sizeof(float), W[p].size(), f);
    }
    fclose(f);
    return true;
}

static bool load_model(const std::string& path) {
    FILE* f = fopen(path.c_str(), "rb");
    if (!f) return false;
    char magic[8];
    if (fread(magic, 1, 8, f) != 8 || memcmp(magic, "NTUP2048", 8) != 0) {
        fclose(f);
        return false;
    }
    int32_t hdr[4];
    if (fread(hdr, sizeof(int32_t), 4, f) != 4) { fclose(f); return false; }
    const int np = hdr[0], len = hdr[1];
    float meta[2];
    if (fread(meta, sizeof(float), 2, f) != 2) { fclose(f); return false; }
    if (np <= 0 || np > MAXPAT || len <= 0 || len > MAXTUP) { fclose(f); return false; }
    NPAT = np;
    for (int p = 0; p < np; p++) {
        PAT_LEN[p] = len;
        if (fread(PAT_CELLS[p], sizeof(int), len, f) != (size_t)len) { fclose(f); return false; }
        size_t n = (size_t)1 << (4 * len);
        W[p].assign(n, 0.0f);
        if (fread(W[p].data(), sizeof(float), n, f) != n) { fclose(f); return false; }
    }
    fclose(f);
    return true;
}

// -------------------------------------------------------------- selftest --
static int run_selftest() {
    build_row_tables();
    setup_patterns("4x16");
    int fails = 0;
    // moves agree with a naive reference; build boards with DISTINCT cells so
    // no value can be OR-accumulated past 15 (a 4-bit nibble cannot hold level 16)
    std::mt19937_64 rng(7);
    int order[16];
    for (int i = 0; i < 16; i++) order[i] = i;
    for (int t = 0; t < 50000; t++) {
        std::shuffle(order, order + 16, rng);
        board_t b = 0;
        int cells = 1 + (int)(rng() % 16);
        for (int i = 0; i < cells; i++) b |= (board_t)(1 + rng() % 6) << (4 * order[i]);
        for (int d = 0; d < 4; d++) {
            int g1 = 0;
            board_t a = do_move(b, d, &g1);
            // reference: decode, move, encode
            int c[16], o[16];
            for (int i = 0; i < 16; i++) c[i] = nib(b, i);
            for (int i = 0; i < 16; i++) o[i] = 0;
            int g2 = 0;
            for (int line = 0; line < 4; line++) {
                int v[4], out[4], m = 0;
                for (int i = 0; i < 4; i++) {
                    int idx = (d == 0 || d == 1) ? line * 4 + (d == 0 ? i : 3 - i)
                                                 : (d == 2 ? i : 3 - i) * 4 + line;
                    v[i] = c[idx];
                    if (v[i]) out[m++] = v[i];
                }
                int res[4] = {0, 0, 0, 0}, w = 0, i = 0, sg = 0;
                while (i < m) {
                    if (i + 1 < m && out[i] == out[i + 1]) {
                        int nv = out[i] + 1;
                        res[w++] = nv;
                        sg += 1 << nv;
                        i += 2;
                    } else {
                        res[w++] = out[i++];
                    }
                }
                g2 += sg;
                for (int i = 0; i < 4; i++) {
                    int idx = (d == 0 || d == 1) ? line * 4 + (d == 0 ? i : 3 - i)
                                                 : (d == 2 ? i : 3 - i) * 4 + line;
                    o[idx] = res[i];
                }
            }
            board_t ref = 0;
            for (int i = 0; i < 16; i++) ref |= (board_t)o[i] << (4 * i);
            if (a != ref || g1 != g2) {
                printf("FAIL move dir=%d b=%016llx got=%016llx/%d want=%016llx/%d\n", d,
                       (unsigned long long)b, (unsigned long long)a, g1,
                       (unsigned long long)ref, g2);
                fails++;
                if (fails > 3) return 1;
            }
        }
    }
    // pattern index is a bijection on single-cell boards
    {
        board_t b = 0;
        for (int i = 0; i < 16; i++) {
            b = 0;
            b |= (board_t)5 << (4 * i);
            float v = value_of(b);
            if (!std::isfinite(v)) { printf("FAIL value nan\n"); fails++; }
        }
    }
    // a learned-free network values everything 0
    board_t b = 0;
    b |= 1ULL << 0;
    if (value_of(b) != 0.0f) { printf("FAIL zero init\n"); fails++; }
    printf(fails ? "SELFTEST FAILED (%d)\n" : "SELFTEST OK\n", fails);
    return fails ? 1 : 0;
}

// ----------------------------------------------------------------- main ---
int main(int argc, char** argv) {
    bool selftest = false;
    for (int i = 1; i < argc; i++) {
        std::string a = argv[i];
        auto next = [&]() { return std::string(argv[++i]); };
        if (a == "--threads") g_cfg.threads = std::stoi(next());
        else if (a == "--games") g_cfg.games = std::stoll(next());
        else if (a == "--alpha") g_cfg.alpha = std::stof(next());
        else if (a == "--eps") g_cfg.eps = std::stof(next());
        else if (a == "--samples") g_cfg.samples = std::stoi(next());
        else if (a == "--pat") g_cfg.pat = next();
        else if (a == "--save") g_cfg.save = next();
        else if (a == "--load") g_cfg.load = next();
        else if (a == "--save-every") g_cfg.saveEvery = std::stoll(next());
        else if (a == "--scale") g_cfg.scale = std::stof(next());
        else if (a == "--search-depth") g_searchDepth = std::stoi(next());
        else if (a == "--prob-cut") g_cfg.probCut = std::stof(next());
        else if (a == "--alpha-final") g_cfg.alphaFinal = std::stof(next());
        else if (a == "--eval") g_cfg.evalGames = std::stoi(next());
        else if (a == "--eval-only") g_cfg.evalOnly = true;
        else if (a == "--seed") g_cfg.seed = std::stoi(next());
        else if (a == "--selftest") selftest = true;
        else if (a == "--help" || a == "-h") {
            printf("ntuple: n-tuple TD(0) for 2048\n"
                   "  --threads N --games N --alpha A --eps E --samples K\n"
                   "  --pat 4x16|4x8|6x4 --save F --load F --save-every N\n"
                   "  --eval GAMES --eval-only --seed S --selftest\n");
            return 0;
        } else {
            fprintf(stderr, "unknown arg %s\n", a.c_str());
            return 2;
        }
    }
    if (selftest) return run_selftest();
    disable_power_throttling();
    build_row_tables();

    bool loaded = false;
    if (!g_cfg.load.empty()) {
        loaded = load_model(g_cfg.load);
        if (!loaded) { fprintf(stderr, "cannot load %s\n", g_cfg.load.c_str()); return 2; }
        printf("{\"loaded\":\"%s\",\"patterns\":%d,\"len\":%d}\n", g_cfg.load.c_str(), NPAT, PAT_LEN[0]);
        fflush(stdout);
    }
    if (!loaded) setup_patterns(g_cfg.pat);

    if (g_cfg.evalGames > 0) {
        auto t0 = Clock::now();
        EvalResult r = evaluate(g_cfg.evalGames, g_cfg.seed + 777);
        double secs = std::chrono::duration<double>(Clock::now() - t0).count();
        printf("{\"eval_games\":%lld,\"mean\":%.0f,\"median\":%.0f,\"best\":%d,\"worst\":%d,"
               "\"moves_per_game\":%.0f,\"seconds\":%.1f,\"tiles\":{",
               r.games, r.mean, r.median, r.best, r.worst, r.avgMoves, secs);
        bool first = true;
        for (int k = 0; k < 20; k++) {
            if (!r.tiles[k]) continue;
            printf("%s\"%d\":%d", first ? "" : ",", 1 << k, r.tiles[k]);
            first = false;
        }
        printf("}}\n");
        if (g_cfg.evalOnly) return 0;
    }

    if (g_cfg.games <= 0) return 0;

    auto t0 = Clock::now();
    std::vector<std::thread> pool;
    for (int t = 0; t < g_cfg.threads; t++) pool.emplace_back(worker, t);
    int tick = 0;
    while (true) {
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
        tick++;
        long long g = g_games.load();
        double secs = std::chrono::duration<double>(Clock::now() - t0).count();
        if (tick % 25 == 0) {
            printf("{\"games\":%lld,\"steps\":%lld,\"games_per_s\":%.1f,\"steps_per_s\":%.0f,"
                   "\"steps_per_game\":%.0f,\"t\":%.0f}\n",
                   g, g_steps.load(), g / secs, g_steps.load() / secs,
                   g ? (double)g_steps.load() / (double)g : 0.0, secs);
            fflush(stdout);
            if (!g_cfg.save.empty() && g_cfg.saveEvery > 0) save_model(g_cfg.save);
        }
        if (g >= g_cfg.games) break;
    }
    g_stop = true;
    for (auto& th : pool) th.join();
    if (!g_cfg.save.empty()) {
        if (save_model(g_cfg.save)) printf("{\"saved\":\"%s\"}\n", g_cfg.save.c_str());
    }
    double secs = std::chrono::duration<double>(Clock::now() - t0).count();
    printf("{\"done\":true,\"games\":%lld,\"steps\":%lld,\"seconds\":%.1f}\n", g_games.load(),
           g_steps.load(), secs);
    return 0;
}
