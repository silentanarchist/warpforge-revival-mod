// Ed25519 signatures (RFC 8032), written out in plain managed code so they work the same on every platform the mod
// runs on (the phone's .NET has no Ed25519 of its own). Only BigInteger and SHA-512 are used. It is slow next to a
// native library (some milliseconds per signature), which does not matter for one check per mod update.
//
// Kept in the old C# 5 style on purpose: the owner's signing script (tools/mod-signing/sign-mod.ps1) compiles this same
// class with Windows PowerShell's built-in C# compiler, which knows nothing newer. Change both copies together.
using System;
using System.Numerics;
using System.Security.Cryptography;

namespace WarpforgeRevival
{
    public static class Ed25519
    {
        private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        private static readonly BigInteger Q = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        private static readonly BigInteger D = Mod(-121665 * Inv(121666));
        private static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
        private static readonly BigInteger[] G = MakeBase();

        private static BigInteger Mod(BigInteger x) { BigInteger r = x % P; return r.Sign < 0 ? r + P : r; }
        private static BigInteger Inv(BigInteger x) { return BigInteger.ModPow(Mod(x), P - 2, P); }

        private static BigInteger[] MakeBase()
        {
            BigInteger y = Mod(4 * Inv(5));
            BigInteger x = RecoverX(y, 0);
            return new BigInteger[] { x, y, BigInteger.One, Mod(x * y) };
        }

        // points in extended coordinates (X, Y, Z, T), x = X/Z, y = Y/Z, x*y = T/Z
        private static BigInteger[] Add(BigInteger[] p, BigInteger[] q)
        {
            BigInteger a = Mod((p[1] - p[0]) * (q[1] - q[0]));
            BigInteger b = Mod((p[1] + p[0]) * (q[1] + q[0]));
            BigInteger c = Mod(2 * p[3] * q[3] * D);
            BigInteger d = Mod(2 * p[2] * q[2]);
            BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
            return new BigInteger[] { Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h) };
        }

        private static BigInteger[] Mul(BigInteger s, BigInteger[] p)
        {
            BigInteger[] q = new BigInteger[] { BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero };
            while (s.Sign > 0)
            {
                if (!s.IsEven) q = Add(q, p);
                p = Add(p, p);
                s >>= 1;
            }
            return q;
        }

        private static bool Equal(BigInteger[] p, BigInteger[] q)
        {
            return Mod(p[0] * q[2] - q[0] * p[2]).IsZero && Mod(p[1] * q[2] - q[1] * p[2]).IsZero;
        }

        private static BigInteger RecoverX(BigInteger y, int sign)
        {
            if (y >= P) return BigInteger.MinusOne;
            BigInteger x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
            if (x2.IsZero) return sign == 0 ? BigInteger.Zero : BigInteger.MinusOne;
            BigInteger x = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(x * x - x2).IsZero) x = Mod(x * SqrtM1);
            if (!Mod(x * x - x2).IsZero) return BigInteger.MinusOne;
            if ((int)(x & 1) != sign) x = P - x;
            return x;
        }

        private static byte[] Compress(BigInteger[] p)
        {
            BigInteger zi = Inv(p[2]);
            BigInteger x = Mod(p[0] * zi), y = Mod(p[1] * zi);
            byte[] b = ToBytes(y);
            if (!x.IsEven) b[31] |= 0x80;
            return b;
        }

        private static BigInteger[] Decompress(byte[] s)
        {
            if (s == null || s.Length != 32) return null;
            byte[] c = (byte[])s.Clone();
            int sign = c[31] >> 7;
            c[31] &= 0x7F;
            BigInteger y = FromBytes(c);
            BigInteger x = RecoverX(y, sign);
            if (x.Sign < 0) return null;
            return new BigInteger[] { x, y, BigInteger.One, Mod(x * y) };
        }

        private static BigInteger FromBytes(byte[] b)
        {
            byte[] u = new byte[b.Length + 1];
            Array.Copy(b, u, b.Length);
            return new BigInteger(u);
        }

        private static byte[] ToBytes(BigInteger x)
        {
            byte[] raw = x.ToByteArray();
            byte[] o = new byte[32];
            Array.Copy(raw, o, Math.Min(32, raw.Length));
            return o;
        }

        private static byte[] Sha512(params byte[][] parts)
        {
            using (SHA512 h = SHA512.Create())
            {
                int n = 0;
                foreach (byte[] p in parts) n += p.Length;
                byte[] all = new byte[n];
                int at = 0;
                foreach (byte[] p in parts) { Array.Copy(p, 0, all, at, p.Length); at += p.Length; }
                return h.ComputeHash(all);
            }
        }

        private static BigInteger Scalar(byte[] h32)
        {
            byte[] a = new byte[32];
            Array.Copy(h32, a, 32);
            a[0] &= 248; a[31] &= 127; a[31] |= 64;
            return FromBytes(a);
        }

        /// <summary>The public key (32 bytes) for a secret key (32 random bytes).</summary>
        public static byte[] PublicKey(byte[] secret)
        {
            byte[] h = Sha512(secret);
            return Compress(Mul(Scalar(h), G));
        }

        /// <summary>The 64-byte signature of a message.</summary>
        public static byte[] Sign(byte[] secret, byte[] message)
        {
            byte[] h = Sha512(secret);
            BigInteger a = Scalar(h);
            byte[] prefix = new byte[32];
            Array.Copy(h, 32, prefix, 0, 32);
            byte[] A = Compress(Mul(a, G));
            BigInteger r = FromBytes(Sha512(prefix, message)) % Q;
            byte[] R = Compress(Mul(r, G));
            BigInteger k = FromBytes(Sha512(R, A, message)) % Q;
            BigInteger s = (r + k * a) % Q;
            byte[] sig = new byte[64];
            Array.Copy(R, sig, 32);
            Array.Copy(ToBytes(s), 0, sig, 32, 32);
            return sig;
        }

        /// <summary>True when the signature is the key's signature of the message.</summary>
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64) return false;
            BigInteger[] A = Decompress(publicKey);
            if (A == null) return false;
            byte[] Rs = new byte[32], Ss = new byte[32];
            Array.Copy(signature, Rs, 32);
            Array.Copy(signature, 32, Ss, 0, 32);
            BigInteger[] R = Decompress(Rs);
            if (R == null) return false;
            BigInteger s = FromBytes(Ss);
            if (s >= Q) return false;
            BigInteger k = FromBytes(Sha512(Rs, publicKey, message)) % Q;
            return Equal(Mul(s, G), Add(R, Mul(k, A)));
        }
    }
}
