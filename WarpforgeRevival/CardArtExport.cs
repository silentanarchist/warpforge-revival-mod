using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Il2Cpp;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings > Mod > Export card art: writes the card pictures from this copy of the game into one zip
    /// in the mod's folder (UserData/WarpforgeRevival/card-art-export.zip), for the website to show
    /// from the player's own computer instead of from the server:
    ///   card-art/&lt;card id&gt;.jpg      every card's art, 384 x 384 (what the site's Card viewer draws)
    ///   card-masks/&lt;card id&gt;.png    troops and warlords: the art's own see-through parts as a mask,
    ///                                 192 x 192 (white, alpha = where the figure is drawn over the frame)
    ///   card-frames/&lt;army&gt;_&lt;troop|stratagem&gt;_&lt;1-4&gt;.png   the faction frames by rarity, 684 x 960
    ///   export.json                   what was written, from which game and mod version
    /// Everything is read through the game's own loader (Addressables), drawn smaller on the graphics
    /// card and read back, a few cards per frame so the game keeps running. The files are written by the
    /// mod's own encoders (ImageEncoders), never Unity's.
    /// </summary>
    internal static class CardArtExport
    {
        private const int ArtSize = 384, MaskSize = 192, InFlight = 6;
        private const double FrameBudgetMs = 20;
        // where the card sits in a 1024 x 1024 frame texture (the site's frames are this part)
        private const int FrameX = 170, FrameTop = 36, FrameW = 684, FrameH = 960;

        private sealed class Art { public string Guid; public int Army; public readonly List<(string id, int type)> Cards = new List<(string, int)>(); }

        private static bool running;
        private static readonly Queue<Art> todo = new Queue<Art>();
        private static readonly List<(Art art, AsyncOperationHandle<Sprite> load, float since)> loading = new List<(Art, AsyncOperationHandle<Sprite>, float)>();
        private static AsyncOperationHandle<Sprite> anchor;   // (interop handles are objects: null = none)            // keeps the current faction's art bundle loaded
        private static int anchorArmy = int.MinValue;
        private static int total, done, written, masks, frames, sinceCleanup;
        private static readonly List<string> missing = new List<string>();
        private static ZipArchive zip;
        private static string zipPath, partPath;
        private static Texture2D readback;
        private static Stopwatch clock;
        private static Action<string> report;

        internal static bool Running => running;
        internal static string FilePath => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "card-art-export.zip");

        /// <summary>Starts an export; <paramref name="progress"/> gets short texts for the button (on the main thread).</summary>
        internal static void Start(Action<string> progress)
        {
            if (running) return;
            report = progress;
            try
            {
                var cards = Gather();
                if (cards.Count == 0) { Say("No cards loaded yet: open the Collection once, then try again."); return; }
                zipPath = FilePath;
                partPath = zipPath + ".part";
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
                if (File.Exists(partPath)) File.Delete(partPath);
                zip = new ZipArchive(new FileStream(partPath, FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Create);
                todo.Clear(); loading.Clear(); missing.Clear();
                foreach (var a in cards) todo.Enqueue(a);
                total = cards.Count; done = written = masks = frames = sinceCleanup = 0;
                anchor = null; anchorArmy = int.MinValue;
                clock = Stopwatch.StartNew();
                running = true;
                int ids = 0; foreach (var a in cards) ids += a.Cards.Count;
                RevivalMod.Log.Msg($"[art export] started: {ids} cards with art, {total} pictures, writing {zipPath}");
                Say($"Exporting card art... 0 / {total}");
            }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>Every card the game has loaded (collection, created cards, tokens), grouped by picture, faction by faction.</summary>
        private static List<Art> Gather()
        {
            var byGuid = new Dictionary<string, Art>();
            var seen = new HashSet<string>();
            void Add(RawCardScript c)
            {
                if ((object)c == null || c.Pointer == IntPtr.Zero) return;
                string id = c.uniqueId;
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
                var r = c.cardImageReference;
                string guid = (object)r == null ? null : r.AssetGUID;
                if (string.IsNullOrEmpty(guid)) return;
                if (!byGuid.TryGetValue(guid, out var art)) byGuid[guid] = art = new Art { Guid = guid, Army = (int)c.cardArmy };
                art.Cards.Add((id, (int)c.cardType));
            }
            var pd = PlayerDataManager.singletonManager;
            var list = (object)pd == null ? null : pd.allCardCollection;
            if (list != null) for (int i = 0; i < list.Count; i++) Add(list[i]);
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<RawCardScript>()))
                Add(o.TryCast<RawCardScript>());
            var arts = new List<Art>(byGuid.Values);
            arts.Sort((a, b) => a.Army != b.Army ? a.Army.CompareTo(b.Army) : string.CompareOrdinal(a.Guid, b.Guid));
            return arts;
        }

        /// <summary>Called every frame.</summary>
        internal static void Tick()
        {
            if (!running) return;
            var frameClock = Stopwatch.StartNew();
            try
            {
                while (frameClock.Elapsed.TotalMilliseconds < FrameBudgetMs)
                {
                    // keep a few pictures loading, one faction at a time (its art bundle stays loaded meanwhile)
                    while (loading.Count < InFlight && todo.Count > 0 && (loading.Count == 0 || todo.Peek().Army == loading[0].art.Army))
                    {
                        var a = todo.Dequeue();
                        if (a.Army != anchorArmy) NewFaction(a.Army);
                        loading.Add((a, new AssetReference(a.Guid).LoadAssetAsync<Sprite>(), Time.realtimeSinceStartup));
                    }
                    if (loading.Count == 0) break;
                    int ready = loading.FindIndex(l => l.load.IsDone || Time.realtimeSinceStartup - l.since > 30f);
                    if (ready < 0) return;                               // waiting for the game's loader
                    var (art, load, _) = loading[ready];
                    loading.RemoveAt(ready);
                    Picture(art, load);
                    done++;
                    if (done % 25 == 0 || done == total) Say($"Exporting card art... {done} / {total}");
                }
                if (loading.Count == 0 && todo.Count == 0) Finish();
            }
            catch (Exception e) { Fail(e); }
        }

        private static void NewFaction(int army)
        {
            ReleaseAnchor();
            anchorArmy = army;
            Resources.UnloadUnusedAssets();
        }

        private static void ReleaseAnchor()
        {
            if ((object)anchor != null) try { Addressables.Release(anchor); } catch { }
            anchor = null;
        }

        private static void Picture(Art art, AsyncOperationHandle<Sprite> load)
        {
            Sprite sprite = null;
            try { if (load.IsDone && load.Status == AsyncOperationStatus.Succeeded) sprite = load.Result; } catch { }
            if ((object)sprite == null || (object)sprite.texture == null)
            {
                foreach (var c in art.Cards) missing.Add(c.id);
                RevivalMod.Log.Warning($"[art export] picture {art.Guid} did not load ({art.Cards[0].id})");
                try { Addressables.Release(load); } catch { }
                return;
            }
            var rgba = Read(sprite, ArtSize, ArtSize, null);
            // the first picture of a faction keeps its bundle loaded; the rest are let go straight away
            if ((object)anchor == null) anchor = load; else try { Addressables.Release(load); } catch { }
            if (++sinceCleanup >= 40) { sinceCleanup = 0; Resources.UnloadUnusedAssets(); }

            var rgb = new byte[ArtSize * ArtSize * 3];
            for (int i = 0, j = 0; i < rgb.Length; i += 3, j += 4) { rgb[i] = rgba[j]; rgb[i + 1] = rgba[j + 1]; rgb[i + 2] = rgba[j + 2]; }
            byte[] jpg = ImageEncoders.Jpeg(rgb, ArtSize, ArtSize, 85);

            // troops and warlords: the art's see-through parts are drawn over the frame
            byte[] mask = null;
            if (art.Cards.Exists(c => c.type == (int)CardTypeOptions.Minion || c.type == (int)CardTypeOptions.Hero))
            {
                var la = new byte[MaskSize * MaskSize * 2];
                bool clear = false;
                for (int y = 0; y < MaskSize; y++)
                    for (int x = 0; x < MaskSize; x++)
                    {
                        int s = ((y * 2) * ArtSize + x * 2) * 4 + 3, r = ArtSize * 4;
                        int a = (rgba[s] + rgba[s + 4] + rgba[s + r] + rgba[s + r + 4] + 2) / 4;
                        if (a < 250) clear = true;
                        la[(y * MaskSize + x) * 2] = 255; la[(y * MaskSize + x) * 2 + 1] = (byte)a;
                    }
                if (clear) mask = ImageEncoders.Png(la, MaskSize, MaskSize, 2);
            }
            foreach (var c in art.Cards)
            {
                Put("card-art/" + c.id + ".jpg", jpg); written++;
                if (mask != null && (c.type == (int)CardTypeOptions.Minion || c.type == (int)CardTypeOptions.Hero)) { Put("card-masks/" + c.id + ".png", mask); masks++; }
            }
        }

        /// <summary>
        /// The sprite drawn into a width x height picture on the graphics card and read back as RGBA rows,
        /// top row first. With <paramref name="crop"/> (in the sprite's own pixels, from its top left) only
        /// that part is read, at full size.
        /// </summary>
        private static byte[] Read(Sprite sprite, int width, int height, RectInt? crop)
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            var scale = new Vector2(r.width / tex.width, r.height / tex.height);
            var offset = new Vector2(r.x / tex.width, r.y / tex.height);
            RenderTexture full = null, half = null, prev = RenderTexture.active;
            try
            {
                int sw = (int)r.width, sh = (int)r.height;
                RenderTexture target;
                int rx = 0, ry = 0;
                if (crop.HasValue)
                {
                    full = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, full, scale, offset);
                    target = full;
                    rx = crop.Value.x; ry = sh - crop.Value.y - crop.Value.height;      // the texture's rows run bottom-up
                    width = crop.Value.width; height = crop.Value.height;
                }
                else
                {
                    // halve first (an exact 2:1 step averages 2 x 2 pixels), then the last step to the size wanted
                    half = RenderTexture.GetTemporary(Math.Max(width, sw / 2), Math.Max(height, sh / 2), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, half, scale, offset);
                    full = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(half, full);
                    target = full;
                }
                if ((object)readback == null || readback.width != width || readback.height != height)
                {
                    if ((object)readback != null) UnityEngine.Object.Destroy(readback);
                    readback = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                }
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(rx, ry, width, height), 0, 0, false);
                byte[] raw = readback.GetRawTextureData().AsSpan().ToArray();      // bottom row first
                var rows = new byte[raw.Length];
                int stride = width * 4;
                for (int y = 0; y < height; y++) Buffer.BlockCopy(raw, (height - 1 - y) * stride, rows, y * stride, stride);
                return rows;
            }
            finally
            {
                RenderTexture.active = prev;
                if ((object)half != null) RenderTexture.ReleaseTemporary(half);
                if ((object)full != null) RenderTexture.ReleaseTemporary(full);
            }
        }

        /// <summary>The faction frames, from the game's own frame list (loaded with the card screens).</summary>
        private static void Frames()
        {
            CardFramesSO so = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CardFramesSO>()))
            {
                so = o.TryCast<CardFramesSO>();
                if ((object)so != null) break;
            }
            if ((object)so == null) { RevivalMod.Log.Warning("[art export] the game's frame list is not loaded: no frames written (open the Collection once and export again)"); return; }
            foreach (CardArmy army in Enum.GetValues(typeof(CardArmy)))
            {
                if (army == CardArmy.Neutral) continue;
                foreach (var (type, word) in new[] { (CardTypeOptions.Minion, "troop"), (CardTypeOptions.Tactic, "stratagem") })
                    for (int tier = 0; tier < 4; tier++)
                    {
                        try
                        {
                            var pair = so.GetClanFrame(army, (CardTier)tier, type);
                            var sprite = pair.Item1;
                            if ((object)sprite == null || (object)sprite.texture == null) continue;
                            var r = sprite.textureRect;
                            float k = r.width / 1024f;
                            var crop = new RectInt((int)Math.Round(FrameX * k), (int)Math.Round(FrameTop * k), (int)Math.Round(FrameW * k), (int)Math.Round(FrameH * k));
                            var rgba = Read(sprite, 0, 0, crop);
                            Put($"card-frames/{army}_{word}_{tier + 1}.png", ImageEncoders.Png(rgba, crop.width, crop.height, 4));
                            frames++;
                            try { so.ReleaseCardFrame(army, (CardTier)tier, type); } catch { }
                        }
                        catch (Exception e) { RevivalMod.Log.Warning($"[art export] frame {army} {word} {tier + 1}: {e.Message}"); }
                    }
            }
        }

        private static void Put(string name, byte[] data)
        {
            var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.NoCompression);       // pictures are packed already
            using var s = entry.Open();
            s.Write(data, 0, data.Length);
        }

        private static void Finish()
        {
            ReleaseAnchor();
            Say("Exporting card frames...");
            Frames();
            var info = new StringBuilder();
            info.Append("{\n  \"made\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("\",\n");
            info.Append("  \"mod\": \"").Append(RevivalMod.Version).Append("\",\n");
            info.Append("  \"game\": \"").Append(Application.version).Append("\",\n");
            info.Append("  \"platform\": \"").Append(Application.platform.ToString()).Append("\",\n");
            info.Append($"  \"art\": {written},\n  \"masks\": {masks},\n  \"frames\": {frames},\n  \"artSize\": {ArtSize},\n  \"maskSize\": {MaskSize},\n");
            info.Append("  \"missing\": [").Append(string.Join(", ", missing.ConvertAll(m => "\"" + m + "\""))).Append("]\n}\n");
            Put("export.json", Encoding.UTF8.GetBytes(info.ToString()));
            zip.Dispose(); zip = null;
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(partPath, zipPath);
            running = false;
            Resources.UnloadUnusedAssets();
            long size = new FileInfo(zipPath).Length;
            RevivalMod.Log.Msg($"[art export] done in {clock.Elapsed.TotalSeconds:0} s: {written} card pictures, {masks} masks, {frames} frames, " +
                               $"{missing.Count} missing, {size / 1048576.0:0.0} MB -> {zipPath}");
            Say($"Card art saved ({size / 1048576.0:0} MB)");
#if !ANDROID_PORT
            try { Application.OpenURL("file:///" + Path.GetDirectoryName(zipPath).Replace('\\', '/')); } catch { }
#endif
        }

        private static void Fail(Exception e)
        {
            RevivalMod.Log.Warning("[art export] stopped: " + e);
            running = false;
            try { ReleaseAnchor(); foreach (var l in loading) try { Addressables.Release(l.load); } catch { } } catch { }
            loading.Clear(); todo.Clear();
            try { zip?.Dispose(); } catch { }
            zip = null;
            try { if (partPath != null && File.Exists(partPath)) File.Delete(partPath); } catch { }
            Say("Card art export failed (see the log)");
        }

        private static void Say(string text)
        {
            try { report?.Invoke(text); } catch { }
        }
    }
}
