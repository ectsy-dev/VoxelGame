using System;
using System.Collections.Generic;
using UnityEngine;

public static class AlphaTerrainGen
{
    public const int WORLD_HEIGHT = 128;
    public const int SEA_LEVEL = 64;

    const short ID_AIR     = 0;
    const short ID_BEDROCK = 1;
    const short ID_GRASS   = 2;
    const short ID_DIRT    = 3;
    const short ID_STONE   = 4;
    public const short ID_WATER = 5;
    const short ID_SAND    = 6;
    const short ID_GRAVEL  = 7;

    // ---------- Java LCG ----------

    const ulong LCG_MUL  = 0x5DEECE66DUL;
    const ulong LCG_ADD  = 0xBUL;
    const ulong LCG_MASK = (1UL << 48) - 1;

    static ulong RngInit(ulong seed) => (seed ^ LCG_MUL) & LCG_MASK;

    static int RngBits(ref ulong r, int bits)
    {
        r = (r * LCG_MUL + LCG_ADD) & LCG_MASK;
        return (int)(r >> (48 - bits));
    }

    static int RngInt(ref ulong r, int bound)
    {
        int m = bound - 1;
        if ((bound & m) == 0)
            return (int)((ulong)RngBits(ref r, 31) * (ulong)bound >> 31);
        int u, v;
        do { u = RngBits(ref r, 31); v = u % bound; } while (u - v + m < 0);
        return v;
    }

    static double RngDouble(ref ulong r)
    {
        long hi = RngBits(ref r, 26);
        long lo = RngBits(ref r, 27);
        return ((hi << 27) + lo) * (1.0 / (1L << 53));
    }

    // ---------- Permutation Table ----------

    struct PermTable { public double xo, yo, zo; public byte[] p; }

    static PermTable[] InitOctaves(ref ulong r, int n)
    {
        var tables = new PermTable[n];
        for (int i = 0; i < n; i++)
        {
            var t = new PermTable
            {
                xo = RngDouble(ref r) * 256.0,
                yo = RngDouble(ref r) * 256.0,
                zo = RngDouble(ref r) * 256.0,
                p  = new byte[512]
            };
            byte j = 0;
            do { t.p[j] = j; } while (j++ != 255);
            byte idx = 0;
            do
            {
                int ri = RngInt(ref r, 256 - idx) + idx;
                if (ri != idx) { t.p[idx] ^= t.p[ri]; t.p[ri] ^= t.p[idx]; t.p[idx] ^= t.p[ri]; }
                t.p[idx + 256] = t.p[idx];
            } while (idx++ != 255);
            tables[i] = t;
        }
        return tables;
    }

    // ---------- Perlin Helpers ----------

    static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    static double Lerp(double t, double a, double b) => a + t * (b - a);

    static double Grad(byte h, double x, double y, double z)
    {
        switch (h & 0xF)
        {
            case 0x0: return  x + y; case 0x1: return -x + y;
            case 0x2: return  x - y; case 0x3: return -x - y;
            case 0x4: return  x + z; case 0x5: return -x + z;
            case 0x6: return  x - z; case 0x7: return -x - z;
            case 0x8: return  y + z; case 0x9: return -y + z;
            case 0xA: return  y - z; case 0xB: return -y - z;
            case 0xC: return  y + x; case 0xD: return -y + z;
            case 0xE: return  y - x; case 0xF: return -y - z;
            default:  return 0;
        }
    }

    static double Grad2D(byte h, double x, double z) => Grad(h, x, 0, z);

    static double Sample3D(double x, double y, double z, PermTable pt)
    {
        byte[] p = pt.p;
        double px = x + pt.xo, py = y + pt.yo, pz = z + pt.zo;
        int xi = (int)px; if (px < xi) xi--;
        int yi = (int)py; if (py < yi) yi--;
        int zi = (int)pz; if (pz < zi) zi--;
        byte xb = (byte)((uint)xi & 0xFF);
        byte yb = (byte)((uint)yi & 0xFF);
        byte zb = (byte)((uint)zi & 0xFF);
        double xc = px - xi, yc = py - yi, zc = pz - zi;
        double fdX = Fade(xc), fdY = Fade(yc), fdZ = Fade(zc);
        int k2 = p[p[xb    ] + yb    ] + zb;
        int l2 = p[p[xb    ] + yb + 1] + zb;
        int k3 = p[p[xb + 1] + yb    ] + zb;
        int l3 = p[p[xb + 1] + yb + 1] + zb;
        double x1  = Lerp(fdX, Grad(p[k2    ], xc,   yc,   zc  ), Grad(p[k3    ], xc-1, yc,   zc  ));
        double x2  = Lerp(fdX, Grad(p[l2    ], xc,   yc-1, zc  ), Grad(p[l3    ], xc-1, yc-1, zc  ));
        double xx1 = Lerp(fdX, Grad(p[k2 + 1], xc,   yc,   zc-1), Grad(p[k3 + 1], xc-1, yc,   zc-1));
        double xx2 = Lerp(fdX, Grad(p[l2 + 1], xc,   yc-1, zc-1), Grad(p[l3 + 1], xc-1, yc-1, zc-1));
        return Lerp(fdZ, Lerp(fdY, x1, x2), Lerp(fdY, xx1, xx2));
    }

    // 3D Perlin accumulator — buffer layout: X-outer, Z-mid, Y-inner → [X*sz*sy + Z*sy + Y]
    static void Accum3D(double[] buf, double bx, double by, double bz,
        int sx, int sy, int sz, double fx, double fy, double fz,
        double octFac, PermTable pt)
    {
        byte[] p = pt.p;
        double inv = 1.0 / octFac;
        int ci = 0, prevYb = -1;
        double x1 = 0, x2 = 0, xx1 = 0, xx2 = 0;

        for (int X = 0; X < sx; X++)
        {
            double xc = (bx + X) * fx + pt.xo;
            int xi = (int)xc; if (xc < xi) xi--;
            byte xb = (byte)((uint)xi & 0xFF);
            xc -= xi;
            double fdX = Fade(xc);

            for (int Z = 0; Z < sz; Z++)
            {
                double zc = (bz + Z) * fz + pt.zo;
                int zi = (int)zc; if (zc < zi) zi--;
                byte zb = (byte)((uint)zi & 0xFF);
                zc -= zi;
                double fdZ = Fade(zc);

                for (int Y = 0; Y < sy; Y++)
                {
                    double yc = (by + Y) * fy + pt.yo;
                    int yi = (int)yc; if (yc < yi) yi--;
                    byte yb = (byte)((uint)yi & 0xFF);
                    yc -= yi;
                    double fdY = Fade(yc);

                    if (Y == 0 || yb != prevYb)
                    {
                        prevYb = yb;
                        int k2 = p[p[xb]     + yb    ] + zb;
                        int l2 = p[p[xb]     + yb + 1] + zb;
                        int k3 = p[p[xb + 1] + yb    ] + zb;
                        int l3 = p[p[xb + 1] + yb + 1] + zb;
                        x1  = Lerp(fdX, Grad(p[k2],     xc,     yc,     zc), Grad(p[k3],     xc-1, yc,     zc));
                        x2  = Lerp(fdX, Grad(p[l2],     xc,     yc - 1, zc), Grad(p[l3],     xc-1, yc - 1, zc));
                        xx1 = Lerp(fdX, Grad(p[k2 + 1], xc,     yc,     zc-1), Grad(p[k3 + 1], xc-1, yc,     zc-1));
                        xx2 = Lerp(fdX, Grad(p[l2 + 1], xc,     yc - 1, zc-1), Grad(p[l3 + 1], xc-1, yc - 1, zc-1));
                    }

                    double y1v = Lerp(fdY, x1, x2);
                    double y2v = Lerp(fdY, xx1, xx2);
                    buf[ci] += Lerp(fdZ, y1v, y2v) * inv;
                    ci++;
                }
            }
        }
    }

    // 2D Perlin accumulator — buffer layout: X-outer, Z-inner → [X*sz + Z]
    static void Accum2D(double[] buf, double bx, double bz, int sx, int sz,
        double fx, double fz, double octFac, PermTable pt)
    {
        byte[] p = pt.p;
        double inv = 1.0 / octFac;
        int ci = 0;

        for (int X = 0; X < sx; X++)
        {
            double xc = (bx + X) * fx + pt.xo;
            int xi = (int)xc; if (xc < xi) xi--;
            byte xb = (byte)((uint)xi & 0xFF);
            xc -= xi;
            double fdX = Fade(xc);

            for (int Z = 0; Z < sz; Z++)
            {
                double zc = (bz + Z) * fz + pt.zo;
                int zi = (int)zc; if (zc < zi) zi--;
                byte zb = (byte)((uint)zi & 0xFF);
                zc -= zi;
                double fdZ = Fade(zc);

                int hxz  = p[p[xb]    ] + zb;
                int hxz1 = p[p[xb + 1]] + zb;
                double a = Lerp(fdX, Grad2D(p[hxz],     xc,     zc), Grad2D(p[hxz1],     xc-1, zc));
                double b = Lerp(fdX, Grad2D(p[hxz + 1], xc, zc - 1), Grad2D(p[hxz1 + 1], xc-1, zc-1));
                buf[ci] += Lerp(fdZ, a, b) * inv;
                ci++;
            }
        }
    }

    static void GenNoise3D(double[] buf, double bx, double by, double bz,
        int sx, int sy, int sz, double fx, double fy, double fz, PermTable[] octs)
    {
        Array.Clear(buf, 0, sx * sy * sz);
        double f = 1.0;
        foreach (var oct in octs) { Accum3D(buf, bx, by, bz, sx, sy, sz, fx*f, fy*f, fz*f, f, oct); f /= 2.0; }
    }

    static void GenNoise2D(double[] buf, double bx, double bz, int sx, int sz,
        double fx, double fz, PermTable[] octs)
    {
        Array.Clear(buf, 0, sx * sz);
        double f = 1.0;
        foreach (var oct in octs) { Accum2D(buf, bx, bz, sx, sz, fx*f, fz*f, f, oct); f /= 2.0; }
    }

    // ---------- Simplex 2D (biomes) ----------

    static readonly int[,] _g2 = {
        {1,1},{-1,1},{1,-1},{-1,-1},{1,0},{-1,0},{1,0},{-1,0},{0,1},{0,-1},{0,1},{0,-1}
    };
    const double F2 = 0.3660254037844386;
    const double G2 = 0.21132486540518713;

    static void AccumSimplex(double[] buf, double bx, double bz, int sx, int sz,
        double fx, double fz, double ampFac, PermTable pt)
    {
        byte[] p = pt.p;
        int k = 0;
        for (int X = 0; X < sx; X++)
        {
            double xc = (bx + X) * fx + pt.xo;
            for (int Z = 0; Z < sz; Z++)
            {
                double zc = (bz + Z) * fz + pt.yo;
                double s  = (xc + zc) * F2;
                int ix = (int)(xc + s); if (xc + s < ix) ix--;
                int iz = (int)(zc + s); if (zc + s < iz) iz--;
                double td = (ix + iz) * G2;
                double x0 = xc - (ix - td), z0 = zc - (iz - td);
                int i1 = x0 > z0 ? 1 : 0, j1 = x0 > z0 ? 0 : 1;
                double x1 = x0 - i1 + G2, z1 = z0 - j1 + G2;
                double x2 = x0 - 1.0 + 2*G2, z2 = z0 - 1.0 + 2*G2;
                byte gi0 = (byte)(p[((uint)ix       & 0xFF) + p[(uint)iz       & 0xFF]] % 12);
                byte gi1 = (byte)(p[((uint)(ix + i1) & 0xFF) + p[(uint)(iz + j1) & 0xFF]] % 12);
                byte gi2 = (byte)(p[((uint)(ix + 1) & 0xFF) + p[(uint)(iz + 1) & 0xFF]] % 12);
                double t0 = 0.5 - x0*x0 - z0*z0;
                double n0 = t0 < 0 ? 0 : t0*t0*t0*t0 * (_g2[gi0,0]*x0 + _g2[gi0,1]*z0);
                double t1 = 0.5 - x1*x1 - z1*z1;
                double n1 = t1 < 0 ? 0 : t1*t1*t1*t1 * (_g2[gi1,0]*x1 + _g2[gi1,1]*z1);
                double t2 = 0.5 - x2*x2 - z2*z2;
                double n2 = t2 < 0 ? 0 : t2*t2*t2*t2 * (_g2[gi2,0]*x2 + _g2[gi2,1]*z2);
                buf[k] += 70.0 * (n0 + n1 + n2) * ampFac;
                k++;
            }
        }
    }

    static void GenSimplex(double[] buf, double bx, double bz, int sx, int sz,
        double fx, double fz, double ampFactor, PermTable[] octs)
    {
        Array.Clear(buf, 0, sx * sz);
        fx /= 1.5; fz /= 1.5;
        double octAmp = 1.0, octDim = 1.0;
        foreach (var oct in octs)
        {
            AccumSimplex(buf, bx, bz, sx, sz, fx * octAmp, fz * octAmp, 0.55 / octDim, oct);
            octAmp *= ampFactor;
            octDim *= 0.5;
        }
    }

    // ---------- Noise Table Set ----------

    struct Noises
    {
        public PermTable[] minLimit, maxLimit, mainLimit;
        public PermTable[] shoreComp, surfElev, scale, depth;
        public PermTable[] temperature, humidity, precipitation;
        public PermTable[] cave;
    }

    static Noises _noises;
    static int _noiseSeed = int.MinValue;

    // Per-thread scratch buffers — allocated once per thread on first use, reused every call.
    // All terrain gen calls on a single thread are strictly sequential so sharing is safe.
    [ThreadStatic] static double[] _tsTemp;
    [ThreadStatic] static double[] _tsHumi;
    [ThreadStatic] static double[] _tsPreci;
    [ThreadStatic] static double[] _tsSandN;
    [ThreadStatic] static double[] _tsGravN;
    [ThreadStatic] static double[] _tsElevN;
    [ThreadStatic] static double[] _tsJitterN;
    [ThreadStatic] static bool[]   _tsNearWater;
    [ThreadStatic] static double[] _tsScaleN;
    [ThreadStatic] static double[] _tsDepthN;
    [ThreadStatic] static double[] _tsMainN;
    [ThreadStatic] static double[] _tsMinN;
    [ThreadStatic] static double[] _tsMaxN;
    [ThreadStatic] static double[] _tsGrid;

    static double[] TsTemp      => _tsTemp      ?? (_tsTemp      = new double[256]);
    static double[] TsHumi      => _tsHumi      ?? (_tsHumi      = new double[256]);
    static double[] TsPreci     => _tsPreci     ?? (_tsPreci     = new double[256]);
    static double[] TsSandN     => _tsSandN     ?? (_tsSandN     = new double[256]);
    static double[] TsGravN     => _tsGravN     ?? (_tsGravN     = new double[256]);
    static double[] TsElevN     => _tsElevN     ?? (_tsElevN     = new double[256]);
    static double[] TsJitterN   => _tsJitterN   ?? (_tsJitterN   = new double[256]);
    static bool[]   TsNearWater => _tsNearWater ?? (_tsNearWater = new bool[256]);
    static double[] TsScaleN    => _tsScaleN    ?? (_tsScaleN    = new double[25]);
    static double[] TsDepthN    => _tsDepthN    ?? (_tsDepthN    = new double[25]);
    static double[] TsMainN     => _tsMainN     ?? (_tsMainN     = new double[425]);
    static double[] TsMinN      => _tsMinN      ?? (_tsMinN      = new double[425]);
    static double[] TsMaxN      => _tsMaxN      ?? (_tsMaxN      = new double[425]);
    static double[] TsGrid      => _tsGrid      ?? (_tsGrid      = new double[425]);

    static void EnsureNoises(int seed)
    {
        if (seed == _noiseSeed) return;
        ulong r = RngInit((ulong)(uint)seed);
        _noises.minLimit   = InitOctaves(ref r, 16);
        _noises.maxLimit   = InitOctaves(ref r, 16);
        _noises.mainLimit  = InitOctaves(ref r, 8);
        _noises.shoreComp  = InitOctaves(ref r, 4);
        _noises.surfElev   = InitOctaves(ref r, 4);
        _noises.scale      = InitOctaves(ref r, 10);
        _noises.depth      = InitOctaves(ref r, 16);
        InitOctaves(ref r, 8); // forest — advance RNG state, not used for terrain

        ulong rt = RngInit((ulong)(uint)seed * 9871UL);
        _noises.temperature = InitOctaves(ref rt, 4);
        ulong rh = RngInit((ulong)(uint)seed * 39811UL);
        _noises.humidity = InitOctaves(ref rh, 4);
        ulong rp = RngInit((ulong)(uint)seed * 0x84a59UL);
        _noises.precipitation = InitOctaves(ref rp, 2);
        ulong rc = RngInit((ulong)(uint)seed * 0x5F3759DFUL);
        _noises.cave = InitOctaves(ref rc, 1);
        _noiseSeed = seed;
    }

    // ---------- Biome Maps ----------

    static void GetBiomeMaps(int blockX, int blockZ, out double[] temp, out double[] humi)
    {
        temp  = TsTemp;
        humi  = TsHumi;
        var preci = TsPreci;
        GenSimplex(temp,  blockX, blockZ, 16, 16, 0.025, 0.025, 0.25,          _noises.temperature);
        GenSimplex(humi,  blockX, blockZ, 16, 16, 0.050, 0.050, 1.0 / 3.0,    _noises.humidity);
        GenSimplex(preci, blockX, blockZ, 16, 16, 0.25,  0.25,  0.58823529411, _noises.precipitation);
        for (int i = 0; i < 256; i++)
        {
            double p  = preci[i] * 1.1 + 0.5;
            double tv = (temp[i] * 0.15 + 0.7) * 0.99 + p * 0.01;
            tv = 1.0 - (1.0 - tv) * (1.0 - tv);
            temp[i] = Math.Max(0.0, Math.Min(1.0, tv));
            double hv = (humi[i] * 0.15 + 0.5) * 0.998 + p * 0.002;
            humi[i] = Math.Max(0.0, Math.Min(1.0, hv));
        }
    }

    // ---------- Density Grid ----------

    // 5x17x5 grid, indexed [cx*85 + cz*17 + cy]
    static void FillDensityGrid(double[] grid, int chunkX, int chunkZ,
        double[] temperatures, double[] humidities)
    {
        const double D = 684.412;

        var scaleN = TsScaleN;
        var depthN = TsDepthN;
        GenNoise2D(scaleN, chunkX, chunkZ, 5, 5, 1.121, 1.121, _noises.scale);
        GenNoise2D(depthN, chunkX, chunkZ, 5, 5, 200.0, 200.0, _noises.depth);

        var mainN = TsMainN;
        var minN  = TsMinN;
        var maxN  = TsMaxN;
        GenNoise3D(mainN, chunkX, 0, chunkZ, 5, 17, 5, D/80, D/160, D/80, _noises.mainLimit);
        GenNoise3D(minN,  chunkX, 0, chunkZ, 5, 17, 5, D,    D,     D,    _noises.minLimit);
        GenNoise3D(maxN,  chunkX, 0, chunkZ, 5, 17, 5, D,    D,     D,    _noises.maxLimit);

        for (int cx = 0; cx < 5; cx++)
        {
            for (int cz = 0; cz < 5; cz++)
            {
                int cell = cx * 5 + cz;
                int sX = Math.Min(cx * 3 + 1, 15);
                int sZ = Math.Min(cz * 3 + 1, 15);
                double t = temperatures[sX * 16 + sZ];
                double h = humidities[sX * 16 + sZ];

                double ard = 1.0 - h * t;
                ard = 1.0 - ard * ard * ard * ard;

                double surface = (scaleN[cell] / 512.0 + 256.0 / 512.0) * ard;
                if (surface > 1.0) surface = 1.0;

                double depth = depthN[cell] / 8000.0;
                if (depth < 0.0) depth = -depth * 0.3;
                depth = depth * 3.0 - 1.5;

                if (depth < 0.0)
                {
                    depth = Math.Max(depth / 2.0, -2.0) / 1.4 / 2.0;
                    surface = 0.0;
                }
                else
                {
                    depth = Math.Min(depth, 1.0) / 8.0;
                }

                surface = Math.Max(surface, 0.0) + 0.5;
                double depthCol = 8.5 + depth * (17.0 / 16.0) * 4.0;

                for (int cy = 0; cy < 17; cy++)
                {
                    int idx = cx * 85 + cz * 17 + cy;
                    double colPS = ((cy - depthCol) * 12.0) / surface;
                    if (colPS < 0.0) colPS *= 4.0;

                    double minV  = minN[idx] / 512.0;
                    double maxV  = maxN[idx] / 512.0;
                    double mainV = (mainN[idx] / 10.0 + 1.0) / 2.0;

                    double limit = mainV < 0.0 ? minV : mainV > 1.0 ? maxV : Lerp(mainV, minV, maxV);
                    grid[idx] = limit - colPS;
                }
            }
        }
    }

    // ---------- Terrain (trilinear interpolation) ----------

    static void BuildTerrain(short[,,] raw, double[] grid)
    {
        for (int cx = 0; cx < 4; cx++)
        for (int cz = 0; cz < 4; cz++)
        for (int cy = 0; cy < 16; cy++)
        {
            double d000 = grid[ cx      * 85 +  cz      * 17 + cy    ];
            double d001 = grid[ cx      * 85 + (cz + 1) * 17 + cy    ];
            double d100 = grid[(cx + 1) * 85 +  cz      * 17 + cy    ];
            double d101 = grid[(cx + 1) * 85 + (cz + 1) * 17 + cy    ];
            double d010 = grid[ cx      * 85 +  cz      * 17 + cy + 1];
            double d011 = grid[ cx      * 85 + (cz + 1) * 17 + cy + 1];
            double d110 = grid[(cx + 1) * 85 +  cz      * 17 + cy + 1];
            double d111 = grid[(cx + 1) * 85 + (cz + 1) * 17 + cy + 1];

            double sY00 = (d010 - d000) * 0.125, sY01 = (d011 - d001) * 0.125;
            double sY10 = (d110 - d100) * 0.125, sY11 = (d111 - d101) * 0.125;
            double c00 = d000, c01 = d001, c10 = d100, c11 = d101;

            for (int dy = 0; dy < 8; dy++)
            {
                int wy = cy * 8 + dy;
                double sX0 = (c10 - c00) * 0.25, sX1 = (c11 - c01) * 0.25;
                double v0 = c00, v1 = c01;
                for (int dx = 0; dx < 4; dx++)
                {
                    int wx = cx * 4 + dx;
                    double sZ = (v1 - v0) * 0.25, v = v0;
                    for (int dz = 0; dz < 4; dz++)
                    {
                        raw[wx, wy, cz * 4 + dz] = v > 0.0 ? ID_STONE : ID_AIR;
                        v += sZ;
                    }
                    v0 += sX0; v1 += sX1;
                }
                c00 += sY00; c01 += sY01; c10 += sY10; c11 += sY11;
            }
        }
    }

    // ---------- Surface Pass ----------

    static void ReplaceSurface(short[,,] raw, int chunkX, int chunkZ,
        double[] temperatures, double[] humidities,
        bool[,] westMap, bool[,] eastMap, bool[,] southMap, bool[,] northMap)
    {
        const double nf = 0.03125;
        ulong rng = RngInit(unchecked((ulong)((long)chunkX * 0x4f9939f508L + (long)chunkZ * 0x1ef1565bd5L)));

        var sandN = TsSandN;
        var gravN = TsGravN;
        var elevN = TsElevN;
        GenNoise3D(sandN, chunkX * 16, 0, chunkZ * 16, 16, 1, 16, nf,    nf,    1.0,  _noises.shoreComp);
        GenNoise2D(gravN, chunkZ * 16, chunkX * 16,    16, 16, nf, nf,          _noises.shoreComp);
        GenNoise3D(elevN, chunkX * 16, 0, chunkZ * 16, 16, 1, 16, nf*2,  nf*2,  nf*2, _noises.surfElev);

        // Precompute which columns are within beachRadius blocks of a water column
        // (raw y=64 == AIR means terrain is below sea level). Read before the loop
        // modifies raw so the proximity map is based on unmodified terrain.
        const int beachRadius = 4;
        var nearWater = TsNearWater;
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            bool found = false;
            for (int ox = -beachRadius; ox <= beachRadius && !found; ox++)
            for (int oz = -beachRadius; oz <= beachRadius && !found; oz++)
            {
                if (ox*ox + oz*oz > beachRadius*beachRadius) continue;
                int nx = x + ox, nz = z + oz;
                bool inX = (uint)nx < 16, inZ = (uint)nz < 16;
                if (inX && inZ)
                {
                    if (raw[nx, SEA_LEVEL, nz] == ID_AIR) found = true;
                }
                else if (!inX && inZ)
                {
                    var m = nx < 0 ? westMap : eastMap;
                    if (m != null && m[nx < 0 ? nx + 16 : nx - 16, nz]) found = true;
                }
                else if (inX && !inZ)
                {
                    var m = nz < 0 ? southMap : northMap;
                    if (m != null && m[nx, nz < 0 ? nz + 16 : nz - 16]) found = true;
                }
                // both out of bounds (chunk corner) — skip
            }
            nearWater[x * 16 + z] = found;
        }

        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            int i = x * 16 + z;
            double sandRoll = RngDouble(ref rng);
            bool gravelly   = nearWater[i] && gravN[i] + RngDouble(ref rng) * 0.2 > 3.0;
            int  elev       = (int)(elevN[i] / 3.0 + 3.0 + RngDouble(ref rng) * 0.25);
            bool sandy      = nearWater[i] && sandN[i] + sandRoll * 0.2 > 0.0;

            int   state = -1;
            short above = ID_GRASS, below = ID_DIRT;

            for (int y = WORLD_HEIGHT - 1; y >= SEA_LEVEL; y--)
            {
                short b = raw[x, y, z];
                if (b == ID_AIR)   { state = -1; continue; }
                if (b != ID_STONE) continue;

                if (state == -1)
                {
                    if (y <= SEA_LEVEL + 1)
                    {
                        above = ID_GRASS;
                        below = ID_DIRT;
                        if (gravelly) { above = ID_AIR;  below = ID_GRAVEL; }
                        if (sandy)    { above = ID_SAND; below = ID_SAND;   }
                        state = elev < 1 ? 1 : elev;
                    }
                    else if (elev <= 0)
                    {
                        above = ID_AIR; below = ID_STONE;
                        state = elev;
                    }
                    else
                    {
                        above = ID_GRASS; below = ID_DIRT;
                        state = elev;
                    }
                    raw[x, y, z] = above;
                }
                else if (state > 0)
                {
                    state--;
                    raw[x, y, z] = below;
                }
            }
        }
    }

    // ---------- Beach Slope ----------

    // Pass 1: fill the gravelly air pit at y=64 (no y=65 cascade).
    // Pass 2 (x2): spread y=64 sand/gravel 2 blocks inland over flat terrain.
    // Pass 3: raise y=65 at the inland beach edge only — columns not adjacent to water
    //         but adjacent to solid land — so the raised rim sits 1-2 blocks back from
    //         the waterline. Skipped when the beach is only 1 block wide.
    static void BeachSlope(short[,,] raw)
    {
        // Pass 1 — fill gravelly pit at y=64 only
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            if (raw[x, SEA_LEVEL, z] == ID_AIR)
            {
                short b = raw[x, SEA_LEVEL - 1, z];
                if (b == ID_GRAVEL || b == ID_SAND)
                    raw[x, SEA_LEVEL, z] = b;
            }
        }

        // Pass 2 — spread y=64 beach surface outward 2 blocks
        int[] dx = { -1, 1,  0, 0 };
        int[] dz = {  0, 0, -1, 1 };
        for (int iter = 0; iter < 2; iter++)
        {
            var snap = new short[16, 16];
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                snap[x, z] = raw[x, SEA_LEVEL, z];

            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
            {
                short s = snap[x, z];
                if (s != ID_GRAVEL && s != ID_SAND) continue;

                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], nz = z + dz[d];
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    short ng = snap[nx, nz];
                    if (ng == ID_SAND || ng == ID_GRAVEL || ng == ID_AIR || ng == ID_WATER) continue;
                    if (raw[nx, SEA_LEVEL + 1, nz] != ID_AIR) continue;
                    raw[nx, SEA_LEVEL, nz] = s;
                }
            }
        }

        // Pass 3 — raise a y=65 rim at the inland edge of the beach only.
        // Capped at 2 iterations so the rim covers at most 2 rows from the land
        // edge; more iterations would flood the entire flat beach and create a
        // checkerboard height artifact when the erosion pass carves it back.
        int[] dx8 = { -1, 1,  0, 0, -1, -1, 1, 1 };
        int[] dz8 = {  0, 0, -1, 1, -1,  1,-1, 1 };
        for (int iter = 0; iter < 2; iter++)
        {
            bool changed = false;
            var snap65 = new short[16, 16];
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                snap65[x, z] = raw[x, SEA_LEVEL + 1, z];

            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
            {
                if (snap65[x, z] != ID_AIR) continue;
                short s = raw[x, SEA_LEVEL, z];
                if (s != ID_SAND && s != ID_GRAVEL) continue;

                bool atWater = false;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + dx8[d], nz = z + dz8[d];
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    if (raw[nx, SEA_LEVEL, nz] == ID_AIR) { atWater = true; break; }
                }
                if (atWater) continue;

                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], nz = z + dz[d];
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    short nb65 = snap65[nx, nz];
                    if (nb65 != ID_AIR && nb65 != ID_WATER)
                    {
                        raw[x, SEA_LEVEL + 1, z] = s;
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed) break;
        }

        // Erosion — two conditions, repeated until stable:
        //   1. fewer than 2 solid y=65 cardinal neighbours (isolated / thin spur)
        //   2. a y=65 cardinal neighbour is AIR and the cell 2 steps further in
        //      that same direction is solid non-beach land (grass/dirt/stone) —
        //      i.e. a 1-block air gap sits between this block and the land surface.
        bool eroding = true;
        while (eroding)
        {
            eroding = false;
            var snapE = new short[16, 16];
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                snapE[x, z] = raw[x, SEA_LEVEL + 1, z];

            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
            {
                short s = snapE[x, z];
                if (s != ID_SAND && s != ID_GRAVEL) continue;

                bool remove = false;

                int solid = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], nz = z + dz[d];
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    if (snapE[nx, nz] != ID_AIR) solid++;
                }
                if (solid < 2) remove = true;

                if (!remove)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int nx1 = x + dx[d],     nz1 = z + dz[d];
                        int nx2 = x + dx[d] * 2, nz2 = z + dz[d] * 2;
                        if (nx1 < 0 || nx1 >= 16 || nz1 < 0 || nz1 >= 16) continue;
                        if (snapE[nx1, nz1] != ID_AIR) continue;
                        if (nx2 < 0 || nx2 >= 16 || nz2 < 0 || nz2 >= 16) continue;
                        short nb2 = snapE[nx2, nz2];
                        if (nb2 != ID_AIR && nb2 != ID_WATER && nb2 != ID_SAND && nb2 != ID_GRAVEL)
                        {
                            remove = true;
                            break;
                        }
                    }
                }

                if (remove) { raw[x, SEA_LEVEL + 1, z] = ID_AIR; eroding = true; }
            }
        }
    }

    // ---------- Cave Generation ----------
    // Cavern-centric source-chunk approach: each source chunk within RANGE has a chance
    // to spawn a full cave system. Every worm radiates from the carved room, so all
    // tunnels are connected by construction — no two independently-generated systems
    // can hope to overlap.

    // Noise-warped ellipsoid room anchored at (cx, cy, cz).
    // Uses _noises.cave[0] to deform the boundary organically while remaining
    // cross-chunk consistent (world-space coords feed into a seeded noise table).
    static void CarveOrganicRoom(short[,,] raw, int chunkX, int chunkZ,
        double cx, double cy, double cz, float radius)
    {
        const double noiseScale  = 0.09;  // ~11-block feature period
        const double noiseWeight = 0.45;  // ±0.45 normalized boundary warp
        const double vScale      = 0.55;  // vertical squash

        double xr = radius, yr = radius * vScale, zr = radius;
        int bx0 = Math.Max((int)(cx - xr - 2) - chunkX * 16, 0);
        int bx1 = Math.Min((int)(cx + xr + 2) - chunkX * 16 + 1, 16);
        int bz0 = Math.Max((int)(cz - zr - 2) - chunkZ * 16, 0);
        int bz1 = Math.Min((int)(cz + zr + 2) - chunkZ * 16 + 1, 16);
        int by0  = Math.Max((int)(cy - yr - 1), 6);
        int by1  = Math.Min((int)(cy + yr + 1), WORLD_HEIGHT - 2);

        if (bx0 >= bx1 || bz0 >= bz1) return;

        for (int bx = bx0; bx < bx1; bx++)
        for (int bz = bz0; bz < bz1; bz++)
        {
            bool colWater = false;
            for (int by = by0 - 1; by <= by1 + 1 && !colWater; by++)
            {
                if (by < 0 || by >= WORLD_HEIGHT) continue;
                if (raw[bx, by, bz] == ID_WATER) colWater = true;
            }
            if (colWater) continue;

            double wx = bx + chunkX * 16 + 0.5;
            double wz = bz + chunkZ * 16 + 0.5;

            for (int by = by0; by <= by1; by++)
            {
                short blk = raw[bx, by, bz];
                if (blk != ID_STONE && blk != ID_DIRT && blk != ID_GRASS &&
                    blk != ID_GRAVEL && blk != ID_SAND) continue;

                double wy  = by + 0.5;
                double ndx = (wx - cx) / xr;
                double ndy = (wy - cy) / yr;
                double ndz = (wz - cz) / zr;
                double dist = Math.Sqrt(ndx * ndx + ndy * ndy + ndz * ndz);

                if (dist > 1.6) continue;

                double n = Sample3D(wx * noiseScale, wy * noiseScale, wz * noiseScale,
                    _noises.cave[0]);
                if (dist - n * noiseWeight < 1.0)
                    raw[bx, by, bz] = ID_AIR;
            }
        }
    }

    // ---------- Cave Generation ----------
    //
    //   Pipeline per winning source chunk:
    //     1. Room      — large squashed sphere at y=28–55 (the cavern)
    //     2. Entrance  — one upward worm, slow pitch decay → punches to surface
    //     3. Branches  — 2–4 near-horizontal worms crossing chunk boundaries
    //     4. Connector — one steep downward worm linking to deeper zone
    //
    // Cross-chunk consistency: RNG seeded from (scx, scz, seed), so every chunk
    // that examines source chunk (scx, scz) produces identical worms from it.
    // CaveWorm's internal water guard stops entrance worms from breaching ocean floor.

    static void GenerateCaves(short[,,] raw, int chunkX, int chunkZ, int seed)
    {
        const int RANGE = 4; // examine 9×9 = 81 source chunks

        for (int scx = chunkX - RANGE; scx <= chunkX + RANGE; scx++)
        for (int scz = chunkZ - RANGE; scz <= chunkZ + RANGE; scz++)
        {
            ulong r = RngInit(unchecked(
                (ulong)((long)scx * 341873128712L + (long)scz * 132897987541L) ^ (ulong)(uint)seed));

            if (RngInt(ref r, 8) != 0) continue; // ~12.5% chance per source chunk

            double x = scx * 16 + RngInt(ref r, 16);
            double y = RngInt(ref r, 27) + 28; // y=28–55, above bedrock, well below surface
            double z = scz * 16 + RngInt(ref r, 16);

            // 1. Cavern room — noise-warped organic ellipsoid
            float roomSize = 3.0f + (float)(RngDouble(ref r) * 3.5); // radius 3.0–6.5
            CarveOrganicRoom(raw, chunkX, chunkZ, x, y, z, roomSize);

            // 2. Entrance worm — upward, pitchDecay=0.98 maintains angle throughout.
            //    CaveWorm's water guard stops it before breaching ocean floor.
            float eyaw   = (float)(RngDouble(ref r) * Math.PI * 2.0);
            float epitch = (float)(RngDouble(ref r) * 0.25 + 0.55); // 0.55–0.80 upward
            float esz    = (float)(RngDouble(ref r) * 0.15 + 0.8);  // 0.8–0.95, never branches
            CaveWorm(raw, chunkX, chunkZ, ref r, x, y, z,
                esz, eyaw, epitch, 0, 0, 1.0, WORLD_HEIGHT - 2, 0.98f);

            // 3. Branch tunnels — near-horizontal, cross chunk boundaries, stitch
            //    neighboring cavern systems together. size>1 enables forking.
            //    baseHW=2.0 widens the worm tip so it bridges the last 1-3 block gap.
            int branches = RngInt(ref r, 3) + 2; // 2–4
            for (int b = 0; b < branches; b++)
            {
                float byaw   = (float)(RngDouble(ref r) * Math.PI * 2.0);
                float bpitch = (float)((RngDouble(ref r) - 0.5) * 0.25); // ±0.125, near-horizontal
                float bsz    = 1.0f + (float)(RngDouble(ref r) * 1.5);   // 1.0–2.5, allows branching
                CaveWorm(raw, chunkX, chunkZ, ref r, x, y, z,
                    bsz, byaw, bpitch, 0, 0, 1.0, (int)y + 15, -1f, 2.0f);
            }

            // 4. Deep connector — steep downward worm toward lower cave zone.
            float dyaw   = (float)(RngDouble(ref r) * Math.PI * 2.0);
            float dpitch = -(float)(RngDouble(ref r) * 0.3 + 0.3); // −0.30 to −0.60 downward
            float dsz    = 1.0f + (float)(RngDouble(ref r) * 0.8);  // 1.0–1.8, allows branching
            CaveWorm(raw, chunkX, chunkZ, ref r, x, y, z,
                dsz, dyaw, dpitch, 0, 0, 1.0, (int)y + 5);
        }
    }

    // pitchDecay: 0–1 overrides the random 0.7/0.92 choice. Pass -1 for default behavior.
    // baseHW: minimum half-width at worm tips (default 1.5). Set higher for branch worms
    //         so the tip ellipsoid is wide enough to bridge 1-3 block connection gaps.
    static void CaveWorm(short[,,] raw, int chunkX, int chunkZ, ref ulong r,
        double x, double y, double z,
        float size, float yaw, float pitch,
        int step, int length, double vertScale, int yCeiling, float pitchDecay = -1f, float baseHW = 1.5f)
    {
        double cx = chunkX * 16 + 8.0;
        double cz = chunkZ * 16 + 8.0;
        float dYaw = 0f, dPitch = 0f;
        bool roomMode = false;

        if (length <= 0)
        {
            const int maxLen = 8 * 16 - 16; // RANGE * 16 - 16 = 112
            length = maxLen - RngInt(ref r, maxLen / 4);
        }
        if (step == -1) { step = length / 2; roomMode = true; }

        int branchAt     = RngInt(ref r, length / 2) + length / 4;
        bool gentlePitch = pitchDecay >= 0f ? true : RngInt(ref r, 6) == 0;
        float actualDecay = pitchDecay >= 0f ? pitchDecay : (gentlePitch ? 0.92f : 0.7f);

        for (; step < length; step++)
        {
            double hw = baseHW + Math.Sin(step * Math.PI / length) * size;
            double hh = hw * vertScale;

            float cosP = (float)Math.Cos(pitch);
            x += Math.Cos(yaw) * cosP;
            y += Math.Sin(pitch);
            z += Math.Sin(yaw) * cosP;

            pitch *= actualDecay;
            pitch += dPitch * 0.1f;
            yaw   += dYaw   * 0.1f;
            dPitch *= 0.9f;
            dYaw   *= 0.75f;
            dPitch += (float)((RngDouble(ref r) - RngDouble(ref r)) * RngDouble(ref r) * 2.0);
            dYaw   += (float)((RngDouble(ref r) - RngDouble(ref r)) * RngDouble(ref r) * 4.0);

            // Fork into two branches at a specific step, then stop this worm
            if (!roomMode && step == branchAt && size > 1.0f)
            {
                CaveWorm(raw, chunkX, chunkZ, ref r, x, y, z,
                    (float)RngDouble(ref r) * 0.5f + 0.5f,
                    yaw - (float)(Math.PI * 0.5), pitch / 3f, step, length, 1.0, yCeiling);
                CaveWorm(raw, chunkX, chunkZ, ref r, x, y, z,
                    (float)RngDouble(ref r) * 0.5f + 0.5f,
                    yaw + (float)(Math.PI * 0.5), pitch / 3f, step, length, 1.0, yCeiling);
                return;
            }

            if (!roomMode && RngInt(ref r, 4) == 0) continue;

            // Early exit: worm is too far from chunk to ever carve it
            double dx = x - cx, dz2 = z - cz;
            if (dx*dx + dz2*dz2 - (double)(length - step) * (length - step) > (size + 18.0) * (size + 18.0))
                return;

            if (x < cx - 16 - hw*2 || z < cz - 16 - hw*2 ||
                x > cx + 16 + hw*2 || z > cz + 16 + hw*2) continue;

            int x0 = Math.Max((int)(x - hw) - chunkX * 16 - 1, 0);
            int x1 = Math.Min((int)(x + hw) - chunkX * 16 + 2, 16);
            int y0 = Math.Max((int)(y - hh) - 1, 6);
            int y1 = Math.Min((int)(y + hh) + 2, yCeiling);
            int z0 = Math.Max((int)(z - hw) - chunkZ * 16 - 1, 0);
            int z1 = Math.Min((int)(z + hw) - chunkZ * 16 + 2, 16);

            // Skip step if water is present in/adjacent to carve bounds
            bool hasWater = false;
            for (int bx = x0; !hasWater && bx < x1; bx++)
            for (int bz = z0; !hasWater && bz < z1; bz++)
            for (int by = y1 + 1; !hasWater && by >= y0 - 1; by--)
            {
                if (by < 0 || by >= WORLD_HEIGHT) continue;
                if (raw[bx, by, bz] == ID_WATER) { hasWater = true; break; }
            }
            if (hasWater) continue;

            // Carve ellipsoid; dy > -0.7 cuts the bottom for a flat floor
            for (int bx = x0; bx < x1; bx++)
            {
                double ndx = (bx + chunkX * 16 + 0.5 - x) / hw;
                for (int bz = z0; bz < z1; bz++)
                {
                    double ndz = (bz + chunkZ * 16 + 0.5 - z) / hw;
                    for (int by = y1 - 1; by >= y0; by--)
                    {
                        double ndy = (by + 0.5 - y) / hh;
                        if (ndy > -0.7 && ndx*ndx + ndy*ndy + ndz*ndz < 1.0)
                        {
                            short b = raw[bx, by, bz];
                            if (b == ID_STONE || b == ID_DIRT || b == ID_GRASS ||
                                b == ID_GRAVEL || b == ID_SAND)
                                raw[bx, by, bz] = ID_AIR;
                        }
                    }
                }
            }

            if (roomMode) break;
        }
    }

    // ---------- Water Fill ----------

    // Fills ocean water per column, stopping at the first solid block (seafloor).
    // The old blanket fill (all air at y < SEA_LEVEL) flooded density-grid air
    // pockets that sit below the seafloor, creating the visible water layer under
    // the ocean floor. Scanning downward and breaking at solid ensures sub-seafloor
    // pockets stay as air rather than getting flooded.
    static void FillWater(short[,,] raw)
    {
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            if (raw[x, SEA_LEVEL, z] != ID_AIR) continue;
            for (int y = SEA_LEVEL - 1; y >= 0; y--)
            {
                if (raw[x, y, z] == ID_AIR) raw[x, y, z] = ID_WATER;
                else break;
            }
        }
    }

    // ---------- Surface Cleanup ----------

    static void FixExposedDirt(short[,,] raw)
    {
        for (int x = 0; x < 16; x++)
        for (int y = 1; y < WORLD_HEIGHT - 1; y++)
        for (int z = 0; z < 16; z++)
        {
            if (raw[x, y, z] == ID_DIRT && raw[x, y + 1, z] == ID_AIR)
                raw[x, y, z] = ID_GRASS;
        }
    }

    // ---------- Waterline / Seafloor ----------

    // Replaces exposed stone at y=64 and y=63 that the state machine left untouched.
    // Must run before FillWater so y=63 sand is not overwritten by water.
    static void FixWaterlineStone(short[,,] raw)
    {
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            if (raw[x, SEA_LEVEL,     z] == ID_STONE && raw[x, SEA_LEVEL + 1, z] == ID_AIR)
                raw[x, SEA_LEVEL,     z] = ID_SAND;
            if (raw[x, SEA_LEVEL - 1, z] == ID_STONE && raw[x, SEA_LEVEL,     z] == ID_AIR)
                raw[x, SEA_LEVEL - 1, z] = ID_SAND;
        }
    }

    // Replaces the top seafloor block per column with a material based on depth:
    //   shallow (≤~5 below sea level) → sand
    //   medium  (≤~15 below sea level) → gravel
    //   deep    (>~15 below sea level) → stone (unchanged)
    // Low-frequency Perlin noise shifts each threshold smoothly across space.
    // A per-column hash dithers the material within a ±2 block transition zone
    // around each boundary so the bands blend rather than cut cleanly.
    // Must run after FillWater so water presence can be detected.
    static void PlaceSeafloor(short[,,] raw, int chunkX, int chunkZ, int seed)
    {
        var jitterN = TsJitterN;
        GenNoise2D(jitterN, chunkX * 16, chunkZ * 16, 16, 16, 0.015, 0.015, _noises.shoreComp);

        const double transWidth = 1.0;

        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            int yTop = -1;
            for (int y = SEA_LEVEL - 1; y >= 1; y--)
            {
                if (raw[x, y, z] == ID_STONE && raw[x, y + 1, z] == ID_WATER)
                    { yTop = y; break; }
            }
            if (yTop < 0) continue;

            int depth = (SEA_LEVEL - 1) - yTop;
            double jitter = jitterN[x * 16 + z] * 1.0;

            double sandThresh   = 1.5 + jitter;
            double gravelThresh = 4.0 + jitter;

            int wx = chunkX * 16 + x, wz = chunkZ * 16 + z;
            int h = ((wx * 73856093) ^ (wz * 19349663) ^ (seed * 83492791)) & 0x7FFFFFFF;
            double dither = (h % 1000) / 1000.0;

            short block;
            if (depth <= sandThresh - transWidth)
            {
                block = ID_SAND;
            }
            else if (depth <= sandThresh + transWidth)
            {
                double t = (depth - (sandThresh - transWidth)) / (transWidth * 2.0);
                block = dither < (1.0 - t) ? ID_SAND : ID_GRAVEL;
            }
            else if (depth <= gravelThresh - transWidth)
            {
                block = ID_GRAVEL;
            }
            else if (depth <= gravelThresh + transWidth)
            {
                double t = (depth - (gravelThresh - transWidth)) / (transWidth * 2.0);
                block = dither < (1.0 - t) ? ID_GRAVEL : ID_STONE;
            }
            else
            {
                block = ID_STONE;
            }

            if (block != ID_STONE) raw[x, yTop, z] = block;
        }
    }

    // ---------- Bedrock ----------

    static void PlaceBedrock(short[,,] raw, int chunkX, int chunkZ, int seed)
    {
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            int wx = chunkX * 16 + x, wz = chunkZ * 16 + z;
            int h = ((wx * 73856093) ^ (wz * 19349663) ^ (seed * 83492791)) & 0x7FFFFFFF;
            h = h % 4;
            for (int y = 0; y <= h; y++) raw[x, y, z] = ID_BEDROCK;
        }
    }

    // ---------- Ore Gen ----------

    static void PlaceOres(short[,,] raw, int chunkX, int chunkZ, int seed, OreNode[] nodes)
    {
        if (nodes == null || nodes.Length == 0) return;
        for (int x = 0; x < 16; x++)
        for (int y = 1; y < WORLD_HEIGHT; y++)
        for (int z = 0; z < 16; z++)
        {
            if (raw[x, y, z] != ID_STONE) continue;
            int wx = chunkX * 16 + x, wz = chunkZ * 16 + z;
            foreach (var node in nodes)
                if (y > node.minHeight && y < node.maxHeight)
                    if (Noise.Get3DNoise(new Vector3(wx, y, wz), node.noiseThreshold, node.noiseScale, node.noiseOffset, seed))
                        raw[x, y, z] = node.blockID;
        }
    }

    // ---------- Public API ----------

    public static short[,,] GenerateChunk(int chunkX, int chunkZ, int seed, OreNode[] oreNodes,
        bool[,] westMap = null, bool[,] eastMap = null,
        bool[,] southMap = null, bool[,] northMap = null)
    {
        EnsureNoises(seed);

        var raw = new short[(int)VoxelData.chunkWidth, (int)VoxelData.chunkHeight, (int)VoxelData.chunkWidth];

        GetBiomeMaps(chunkX * 16, chunkZ * 16, out double[] temp, out double[] humi);

        var grid = TsGrid;
        FillDensityGrid(grid, chunkX * 4, chunkZ * 4, temp, humi);
        BuildTerrain(raw, grid);

        ReplaceSurface(raw, chunkX, chunkZ, temp, humi, westMap, eastMap, southMap, northMap);
        BeachSlope(raw);
        FixWaterlineStone(raw);
        FillWater(raw);
        GenerateCaves(raw, chunkX, chunkZ, seed);
        FixExposedDirt(raw);
        PlaceSeafloor(raw, chunkX, chunkZ, seed);
        PlaceBedrock(raw, chunkX, chunkZ, seed);
        PlaceOres(raw, chunkX, chunkZ, seed, oreNodes);

        return raw;
    }

    // Runs only FillDensityGrid + BuildTerrain (no surface passes) and returns a
    // 16×16 bool map where true = y=SEA_LEVEL is air (terrain below sea level).
    // Used by World.cs to provide cross-chunk water context before generating a neighbor.
    public static bool[,] BuildWaterMap(int chunkX, int chunkZ, int seed)
    {
        EnsureNoises(seed);
        GetBiomeMaps(chunkX * 16, chunkZ * 16, out double[] temp, out double[] humi);
        var grid = TsGrid;
        FillDensityGrid(grid, chunkX * 4, chunkZ * 4, temp, humi);
        var raw = new short[16, WORLD_HEIGHT, 16];
        BuildTerrain(raw, grid);
        var map = new bool[16, 16];
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            map[x, z] = raw[x, SEA_LEVEL, z] == ID_AIR;
        return map;
    }
}
