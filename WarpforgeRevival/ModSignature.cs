using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WarpforgeRevival
{
    /// <summary>
    /// Signed mod updates (0.14.0). Every build the server hands out carries the project owner's signature, added on
    /// the owner's own computer with a key that never leaves it (tools/mod-signing). The updater installs a download
    /// only when that signature checks out against a key built into this mod, so a server that was broken into, or
    /// anyone between the game and the server, cannot make players run a mod the owner did not sign. The server's
    /// checksum is still checked too.
    ///
    /// The signature is added to the end of the DLL (Windows and .NET ignore anything after the program's own data):
    ///   [the build] [signature, 64 bytes] [key id, 8 bytes] ["WFRSIG01", 8 bytes]
    /// What is signed: "Warpforge Revival mod v1\n" + file name + "\n" + version + "\n" followed by the SHA-256 of the
    /// build. The file name keeps a Windows build from passing as an Android one; the version is read from the build
    /// itself (as the server does), so an older signed build cannot be passed off as a newer one: the updater also
    /// refuses any build that is not newer than the one running.
    /// </summary>
    internal static class ModSignature
    {
        private const string Magic = "WFRSIG01";
        private const int Trailer = 64 + 8 + 8;

        /// <summary>The owner's public keys (base64): the everyday key and a spare kept offline, so losing one does
        /// not stop updates. A public key is not secret.</summary>
        private static readonly string[] Keys =
        {
            "v0GNRYRtzVn43h1UPJiNR1pLwvHlo4tpZUDI0q5NMg0=",
            "+STVSQrWCM6CEz1ouUaizh/+HK/PnlAMd4d+3593u6Q=",
        };

        private static readonly Regex VersionInBuild = new Regex(@"Warpforge Revival[\s\S](\d+(?:\.\d+){1,3})(-[a-z][a-z0-9]*)?", RegexOptions.CultureInvariant);

        /// <summary>The version a build names for itself ("0.14.0-w"), read the way the server reads it; null if none.</summary>
        internal static string VersionOf(byte[] build, int length)
        {
            var m = VersionInBuild.Match(Encoding.Latin1.GetString(build, 0, length));
            return m.Success ? m.Groups[1].Value + m.Groups[2].Value : null;
        }

        /// <summary>Checks a downloaded build. Returns null when it is signed by one of the owner's keys (and says which
        /// version it is), or why it was refused.</summary>
        internal static string Check(byte[] data, string fileName, out string version)
        {
            version = null;
            if (data == null || data.Length <= Trailer) return "the download is too small";
            int body = data.Length - Trailer;
            if (Encoding.ASCII.GetString(data, data.Length - 8, 8) != Magic) return "the download is not signed";
            var sig = new byte[64];
            Array.Copy(data, body, sig, 0, 64);
            var keyId = new byte[8];
            Array.Copy(data, body + 64, keyId, 0, 8);
            version = VersionOf(data, body);
            if (version == null) return "the download names no version";
            byte[] message = Message(fileName, version, SHA256.HashData(new ReadOnlySpan<byte>(data, 0, body)));
            foreach (var k in Keys)
            {
                byte[] key;
                try { key = Convert.FromBase64String(k); } catch { continue; }
                if (key.Length != 32) continue;
                if (!SHA256.HashData(key).AsSpan(0, 8).SequenceEqual(keyId)) continue;
                return Ed25519.Verify(key, message, sig) ? null : "the signature does not match";
            }
            return "the download is signed with a key this mod does not know";
        }

        internal static byte[] Message(string fileName, string version, byte[] sha256)
        {
            var head = Encoding.UTF8.GetBytes("Warpforge Revival mod v1\n" + fileName + "\n" + version + "\n");
            var all = new byte[head.Length + sha256.Length];
            head.CopyTo(all, 0);
            sha256.CopyTo(all, head.Length);
            return all;
        }
    }
}
