using System.Net.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProperVersion;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {
    public static class ModAPI {
        public static readonly HttpClient httpClient = new();

        public static readonly string ImageCacheDir = Path.Combine(GamePaths.Cache, "images");
        public static readonly string AssetsCacheDir = Path.Combine(GamePaths.Cache, "assets");

        public static volatile bool modsQueryFinished = false;
        public static Dictionary<string, APIModSummary> modListSummary = new();
        public static List<CustomModCellEntry> modCells = new();

        public static APIStatusModList modListCache = null;
        public static Dictionary<string, APIStatusModInfo> modInfoCache = new();

        public static Dictionary<string, SemVer> latestVersionCache = new();
        public static Dictionary<string, APIModRelease> latestReleaseCache = new();

        public static readonly bool hasInternet = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

        public static HttpResponseMessage MakeRequest(string url, int tries = 3) {
            if (!hasInternet) throw new WebException("No internet");

            int trial = tries;
            while (trial-- > 0) {
                try {
                    HttpResponseMessage response = null;
                    Task.Run(async () => {
                        response = await httpClient.GetAsync(url);
                    }).Wait();

                    if (trial > 0 && response?.IsSuccessStatusCode != true && response?.StatusCode != HttpStatusCode.NotFound) {
                        throw new Exception("Web request to " + url + " failed with code " + response.StatusCode);
                    }
                    return response;
                } catch (Exception) when (trial > 0) {
                    // De-race condition?
                    Thread.Sleep(100 + EMTK.rng.Next(0, 50));
                }
            }
            
            throw new NotImplementedException("MakeRequest() did not throw an error on failure");
        }

        public static List<string> GetUpdates(List<ModContainer> mods) {
            try {
                List<string> queryMods = new();
                foreach (ModContainer mod in mods) {
                    string modid = mod?.Info?.ModID?.ToLower();
                    if (modid == null || modid == "game" || modid == "creative" || modid == "survival") continue;
                    string ver = mod?.Info?.Version;

                    if (latestReleaseCache.ContainsKey(modid)) continue;
                    if (latestVersionCache.ContainsKey(modid) && EMTK.ParseVersion(modid, ver) >= latestVersionCache[modid]) continue;

                    queryMods.Add(modid + "@" + ver);
                }

                // Query the api for all updates
                string joined = String.Join(",", queryMods);
                while (joined.Length > 1900) {
                    int s = joined.LastIndexOf(',', 1900);
                    MakeUpdatesQuery(joined.Substring(0, s));
                    joined = joined.Substring(s+1);
                }
                if (joined.Length > 0) MakeUpdatesQuery(joined);

                List<string> modUpdates = new();

                // Any mods that did not come back are up to date. Cache this information too
                foreach (ModContainer mod in mods) {
                    string modid = mod?.Info?.ModID?.ToLower();
                    if (modid == null) continue;
                    if (latestVersionCache.ContainsKey(modid)) {
                        if (latestVersionCache[modid] > EMTK.ParseVersion(modid, mod.Info.Version)) modUpdates.Add(modid);
                    } else {
                        latestVersionCache[modid] = EMTK.ParseVersion(modid, mod.Info.Version);
                    }
                }

                // Return the out of date mods
                return modUpdates;
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("EMTK: API request to get mod updates failed with error: {0}", ex);
                return null;
            }
        }

        private static void MakeUpdatesQuery(string mods, int tries = 3) {
            string query = "https://mods.vintagestory.at/api/updates?mods=" + mods;
            Console.WriteLine(query);

            HttpResponseMessage response = MakeRequest(query, tries);
            using (var reader = new StreamReader(response.Content.ReadAsStream())) {
                APIStatusUpdates updates = JsonConvert.DeserializeObject<APIStatusUpdates>(reader.ReadToEnd());
                if (!response.IsSuccessStatusCode) {
                    ScreenManager.Platform.Logger.Warning("EMTK: API request to get mod updates failed with code {0}", modListCache.statuscode);
                }
                if (updates.Updates != null) {
                    foreach (KeyValuePair<string, APIModRelease> mr in updates.Updates) {
                        latestVersionCache[mr.Key.ToLower()] = EMTK.ParseVersion(mr.Key.ToLower(), mr.Value?.modversion);
                        latestReleaseCache[mr.Key.ToLower()] = mr.Value;
                    }
                }
            }
        }

        public static bool fontSet = false;
        public static void SetModEntryFont() {
            if (fontSet) return;

            var medFont = CairoFont.WhiteSmallishText();
            var smallFont = CairoFont.WhiteSmallText();

            foreach (CustomModCellEntry entry in modCells) {
                entry.TitleFont = medFont;
                entry.DetailTextFont = smallFont;
            }

            fontSet = true;
        }

        public static APIStatusModList GetMods(int tries = 3) {
            if (modListCache != null) {
                string code = modListCache.statuscode;
                if (code == "200") return modListCache;
            }

            try {
                HttpResponseMessage response = MakeRequest("https://mods.vintagestory.at/api/mods", tries);
                using (var reader = new StreamReader(response.Content.ReadAsStream())) {
                    modListCache = JsonConvert.DeserializeObject<APIStatusModList>(reader.ReadToEnd());
                    if (modListCache.statuscode == "200") {
                        foreach (APIModSummary summary in modListCache.mods) {
                            if (summary == null || summary?.modidstrs?.Length < 1 || summary?.type != "mod") continue;

                            string modid = summary.modidstrs[0].ToLower();
                            try {
                                modCells.Add(new CustomModCellEntry() {
                                    ModID = modid,
                                    Keywords = String.Join(" ", modid, summary?.name ?? "Unknown", summary?.author ?? "Unknown", summary?.tags ?? Array.Empty<string>()).ToLower(),
                                    Summary = summary,
                                    Title = summary?.name ?? "Unknown",
                                    DetailText = String.Format("{0}\n{1} - {2}",
                                        summary?.author ?? "Unknown",
                                        summary?.side == null ? "" : char.ToUpper(summary.side[0]) + summary.side.Substring(1),
                                        String.Join(", ", summary?.tags ?? Array.Empty<string>())
                                    ),
                                    RightTopText = String.Format("{0} Downloads,\n{1} Follows, {2} Comments", summary?.downloads ?? 0, summary?.follows ?? 0, summary?.comments ?? 0),
                                    // TitleFont = medFont,
                                    // DetailTextFont = smallFont,
                                });
                            } catch (Exception) {
                                return new APIStatusModList {statuscode = "412"};
                            }
                            modListSummary[modid] = summary;
                        }
                    } else {
                        Console.WriteLine("EMTK: API request to get mods failed with code {0}", modListCache.statuscode);
                    }

                    return modListCache;
                }
            } catch (Exception ex) {
                Console.WriteLine("EMTK: API request to get mods failed with error: {0}", ex);
                return new APIStatusModList {statuscode = "412"};
            } finally {
                modsQueryFinished = true;
            }
        }

        public static APIStatusModInfo GetMod(string modid, int tries = 3) {
            if (modInfoCache.ContainsKey(modid)) {
                string code = modInfoCache[modid].statuscode;
                if (code == "200" || code == "404") return modInfoCache[modid];
            }

            try {
                HttpResponseMessage response = MakeRequest("https://mods.vintagestory.at/api/mod/" + modid, tries);
                using (var reader = new StreamReader(response.Content.ReadAsStream())) {
                    var mod = JsonConvert.DeserializeObject<APIStatusModInfo>(reader.ReadToEnd());
                    if (mod.statuscode != "200" && mod.statuscode != "404") {
                        ScreenManager.Platform.Logger.Warning("EMTK: API request to get mod info of \"{0}\" failed with code {1}", modid, mod.statuscode);
                    }
                    modInfoCache[modid] = mod;
                    return mod;
                }
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("EMTK: API request to get mod info of \"{0}\" failed with error: {1}", modid, ex);
                return new APIStatusModInfo {statuscode = "412"};
            }
        }

        public static void CheckEMTKUpdate() {
            try {
                HttpResponseMessage response = MakeRequest("https://mods.vintagestory.at/emtk");
                using (var reader = new StreamReader(response.Content.ReadAsStream())) {
                    string l;
                    while ((l = reader.ReadLine()) != null) {
                        // Could be done with regex I guess, but this is easier
                        int i = l.IndexOf("class=\"downloadbutton\"");
                        if (i < 0) continue;
                        
                        i = l.IndexOf(">");
                        if (i < 0) continue;
                        int e = l.IndexOf("<", i);
                        if (e < 0) continue;

                        string name = l.Substring(i, e-i);

                        i = name.IndexOf("-")+1;
                        e = name.LastIndexOf(".");
                        if (i <= 0 || e < 0) continue;

                        Version ver = new Version(name.Substring(i, e-i));
                        if (ver <= new Version(EMTK.version)) return;

                        EMTK.updateAvailable = true;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("EMTK: Update check failed with error: {0}", ex);
            }
        }

        public static BitmapExternal GetImage(string url, int tries = 3) {
            string uuid;
            using (SHA256 sha = SHA256.Create()) {
                uuid = new Guid(sha.ComputeHash(Encoding.Default.GetBytes(url)).Take(16).ToArray()).ToString();
            }

            string loc = Path.Combine(ImageCacheDir, uuid);
            if (!File.Exists(loc)) {
                if (!Directory.Exists(ImageCacheDir)) Directory.CreateDirectory(ImageCacheDir);

                try {
                    using (MemoryStream ms = new MemoryStream()) {
                        Task.Run(async () => {
                            await MakeRequest(url, tries).Content.CopyToAsync(ms);
                            File.WriteAllBytes(loc, ms.ToArray());
                        }).Wait();
                    }
                } catch (Exception ex) {
                    ScreenManager.Platform.Logger.Error("EMTK: Image request for \"{0}\" failed with error: {1}", url, ex);
                    if (File.Exists(loc)) File.Delete(loc);
                    return null;
                }
            }

            return new BitmapExternal(loc);
        }

        public static string GetAsset(string url, int tries = 3) {
            string uuid;
            using (SHA256 sha = SHA256.Create()) {
                uuid = new Guid(sha.ComputeHash(Encoding.Default.GetBytes(url)).Take(16).ToArray()).ToString();
            }

            string loc = Path.Combine(AssetsCacheDir, uuid);
            if (!File.Exists(loc)) {
                if (!Directory.Exists(AssetsCacheDir)) Directory.CreateDirectory(AssetsCacheDir);

                try {
                    using (MemoryStream ms = new MemoryStream()) {
                        Task.Run(async () => {
                            await MakeRequest(url, tries).Content.CopyToAsync(ms);
                            File.WriteAllBytes(loc, ms.ToArray());
                        }).Wait();
                    }
                } catch (Exception ex) {
                    ScreenManager.Platform.Logger.Error("EMTK: Asset request for \"{0}\" failed with error: {1}", url, ex);
                    if (File.Exists(loc)) File.Delete(loc);
                    return null;
                }
                
            }

            return loc;
        }
    }

    public class CustomModCellEntry : ModCellEntry {
        public static readonly DirectoryInfo TEMP_DIR;
        public static readonly MethodInfo modInfoSet = AccessTools.PropertySetter(typeof(ModContainer), "Info");

        public string ModID;
        public string Keywords;
        public APIModSummary Summary;

        static CustomModCellEntry() {
            if (!Directory.Exists(GamePaths.DataPathMods)) {
                Directory.CreateDirectory(GamePaths.DataPathMods);
            }
            TEMP_DIR = new DirectoryInfo(GamePaths.DataPathMods);
        }

        public CustomModCellEntry() {
            this.Mod = new ModContainer(TEMP_DIR, ScreenManager.Platform.Logger, false);
            modInfoSet.Invoke(this.Mod, new[] {new ModInfo()});
        }
    }

    public class APIStatusModList {
        public string statuscode;
        public APIModSummary[] mods;
    }

    public class APIModSummary {
        public int modid;
        public int assetid;
        public int downloads;
        public int follows;
        public int trendingpoints;
        public int comments;
        public string name;
        public string[] modidstrs;
        public string author;
        public string urlalias;
        public string side;
        public string type;
        public string logo;
        public string[] tags;
        public DateTimeOffset lastreleased;
    }

    public class APIStatusModInfo {
        public string statuscode;
        public APIModInfo mod;
    }

    public class APIModInfo {
        public int modid;
        public int assetid;
        public string name;
        public string text;
        public string author;
        public string urlalias;
        public string logofilename;
        public string logofile;
        public string homepageurl;
        public string sourcecodeurl;
        public string trailervideourl;
        public string issuetrackerurl;
        public string wikiurl;
        public int downloads;
        public int follows;
        public int trendingpoints;
        public int comments;
        public string side;
        public string type;
        public DateTimeOffset created;
        public DateTimeOffset lastmodified;
        public string[] tags;
        public APIModRelease[] releases;
        public APIScreenshot[] screenshots;
    }

    public class APIStatusUpdates {
        public int statuscode;
        public JToken updates;

        Dictionary<string, APIModRelease> updatesCache;

        public Dictionary<string, APIModRelease> Updates {
            get {
                if (updatesCache == null) {
                    updatesCache = updates.Type == JTokenType.Object
                        ? updates.ToObject<Dictionary<string, APIModRelease>>()
                        : new();
                }
                return updatesCache;
            }
        }
    }

    public class APIModRelease {
        public int releaseid;
        public string mainfile;
        public string filename;
        public int fileid;
        public int downloads;
        public string[] tags;
        public string modidstr;
        public string modversion;
        public DateTimeOffset created;
    }

    public class APIScreenshot {
        public int fileid;
        public string mainfile;
        public string filename;
        public string thumbnailfilename;
        public DateTimeOffset created;
    }
}