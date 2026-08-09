using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sprocket.MaterialEditor
{
    
    
    
    
    
    public static class BlueprintMaterial
    {
        
        public static int Apply(string inputPath, string[] prefixes, string[] contains,
                                string material, string outputDir = null, bool dryRun = false)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Blueprint file not found: " + inputPath);
            if (string.IsNullOrWhiteSpace(material))
                throw new ArgumentException("material cannot be empty");

            string raw = File.ReadAllText(inputPath, Encoding.UTF8);
            var meshNames = BuildMeshNameMap(raw);
            var anchorRe = new Regex(
                "\"armourVolume\"\\s*:\\s*([0-9]*\\.?[0-9]+)(.*?)\"bodyMeshVuid\"\\s*:\\s*(\\d+)\\b(.*?)\"armourTechID\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.Singleline);

            var targets = new List<(long vuid, string oldMat, string meshName, int occurrenceIndex)>();
            var vuidOccur = new Dictionary<long, int>();
            foreach (Match m in anchorRe.Matches(raw))
            {
                long vuid = long.Parse(m.Groups[3].Value);
                string oldMat = m.Groups[5].Value;
                meshNames.TryGetValue(vuid, out string meshName);
                if (!vuidOccur.ContainsKey(vuid)) vuidOccur[vuid] = 0;
                int occur = vuidOccur[vuid]++;
                if (NameMatches(meshName, prefixes, contains))
                    targets.Add((vuid, oldMat, meshName ?? "", occur));
            }

            if (dryRun) return targets.Count;
            Dictionary<string, float> densities = null;
            return ApplyTargets(raw, inputPath, targets, material, outputDir, densities, out float _);
        }

        
        
        public static int ApplyToParts(string inputPath, List<PartHit> parts, string material,
            Dictionary<string, float> densities, out float massDelta)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Blueprint file not found: " + inputPath);
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("No parts selected");
            if (string.IsNullOrWhiteSpace(material))
                throw new ArgumentException("material cannot be empty");

            string raw = File.ReadAllText(inputPath, Encoding.UTF8);
            var targets = new List<(long vuid, string oldMat, string meshName, int occurrenceIndex)>();
            foreach (var p in parts)
                targets.Add((p.vuid, p.oldMat, p.meshName, p.occurrenceIndex));

            return ApplyTargets(raw, inputPath, targets, material, null, densities, out massDelta, inPlace: false);
        }

        
        public static int ApplyToPartsInPlace(string inputPath, List<PartHit> parts, string material,
            Dictionary<string, float> densities, out float massDelta)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Blueprint file not found: " + inputPath);
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("No parts selected");
            if (string.IsNullOrWhiteSpace(material))
                throw new ArgumentException("material cannot be empty");

            string raw = File.ReadAllText(inputPath, Encoding.UTF8);
            var targets = new List<(long vuid, string oldMat, string meshName, int occurrenceIndex)>();
            foreach (var p in parts)
                targets.Add((p.vuid, p.oldMat, p.meshName, p.occurrenceIndex));

            return ApplyTargets(raw, inputPath, targets, material, null, densities, out massDelta, inPlace: true);
        }

        private static int ApplyTargets(string raw, string inputPath,
            List<(long vuid, string oldMat, string meshName, int occurrenceIndex)> targets, string material, string outputDir,
            Dictionary<string, float> densities, out float massDelta, bool inPlace = false)
        {
            massDelta = 0f;
            if (targets.Count == 0) return 0;

            var selected = new HashSet<(long vuid, int occurrenceIndex)>();
            foreach (var t in targets) selected.Add((t.vuid, t.occurrenceIndex));

            var anchorRe = new Regex(
                "\"armourVolume\"\\s*:\\s*([0-9]*\\.?[0-9]+)(.*?)\"bodyMeshVuid\"\\s*:\\s*(\\d+)\\b(.*?)\"armourTechID\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.Singleline);

            float densityNew = 0f;
            if (densities != null) densities.TryGetValue(material, out densityNew);

            var vuidOccur = new Dictionary<long, int>();
            int replaced = 0;
            float massAccum = 0f;
            string newRaw = anchorRe.Replace(raw, m =>
            {
                long vuid = long.Parse(m.Groups[3].Value);
                if (!vuidOccur.ContainsKey(vuid)) vuidOccur[vuid] = 0;
                int occur = vuidOccur[vuid]++;

                if (selected.Contains((vuid, occur)))
                {
                    string oldMat = m.Groups[5].Value;
                    float densityOld = 0f;
                    if (densities != null) densities.TryGetValue(oldMat, out densityOld);
                    float vol = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (densityNew > 0f && densityOld > 0f)
                        massAccum += vol * (densityNew - densityOld);
                    replaced++;
                    int g5Start = m.Groups[5].Index - m.Index;
                    int g5End = g5Start + m.Groups[5].Length;
                    return m.Value.Substring(0, g5Start) + material + m.Value.Substring(g5End);
                }
                return m.Value;
            });

            if (replaced == 0) return 0;

            
            if (densities != null && Math.Abs(massAccum) > 1e-6f)
            {
                var massRe = new Regex("\"mass\"\\s*:\\s*(\\d+)");
                newRaw = massRe.Replace(newRaw, mm =>
                {
                    if (!float.TryParse(mm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out float oldMass))
                        return mm.Value;
                    float newMass = oldMass + massAccum;
                    return "\"mass\": " + Math.Max(0, (int)Math.Round(newMass)).ToString(CultureInfo.InvariantCulture);
                });
            }
            massDelta = massAccum;

            string outPath;
            if (inPlace)
            {
                outPath = inputPath;
            }
            else
            {
                if (outputDir == null)
                {
                    string dir = Path.GetDirectoryName(inputPath);
                    string stem = Path.GetFileNameWithoutExtension(inputPath);
                    outputDir = Path.Combine(dir, stem + "_" + material);
                }
                Directory.CreateDirectory(outputDir);
                outPath = Path.Combine(outputDir, Path.GetFileName(inputPath));
            }
            File.WriteAllText(outPath, newRaw, new UTF8Encoding(false));
            return replaced;
        }

        
        public static List<PartHit> ListAllParts(string inputPath)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Blueprint file not found: " + inputPath);
            return FindParts(inputPath, null, null);
        }

        
        
        
        
        
        
        
        public static List<PartHit> MatchSelected(string inputPath, List<long> selectedVuids)
        {
            var result = new List<PartHit>();
            if (selectedVuids == null || selectedVuids.Count == 0)
                return result;
            var all = ListAllParts(inputPath); 
            var wanted = new HashSet<long>(selectedVuids); 
            foreach (var p in all)
            {
                if (wanted.Contains(p.vuid))
                    result.Add(p);
            }
            return result;
        }

        
        public static List<PartHit> FindParts(string inputPath, string[] prefixes, string[] contains)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Blueprint file not found: " + inputPath);

            string raw = File.ReadAllText(inputPath, Encoding.UTF8);
            var meshNames = BuildMeshNameMap(raw);
            var anchorRe = new Regex(
                "\"armourVolume\"\\s*:\\s*([0-9]*\\.?[0-9]+)(.*?)\"bodyMeshVuid\"\\s*:\\s*(\\d+)\\b(.*?)\"armourTechID\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.Singleline);

            var hits = new List<PartHit>();
            var vuidOccur = new Dictionary<long, int>();
            foreach (Match m in anchorRe.Matches(raw))
            {
                long vuid = long.Parse(m.Groups[3].Value);
                string oldMat = m.Groups[5].Value;
                meshNames.TryGetValue(vuid, out string meshName);
                if (!vuidOccur.ContainsKey(vuid)) vuidOccur[vuid] = 0;
                int occur = vuidOccur[vuid]++;
                if (NameMatches(meshName, prefixes, contains))
                    hits.Add(new PartHit { vuid = vuid, meshName = meshName ?? "", oldMat = oldMat, occurrenceIndex = occur });
            }
            return hits;
        }

        private static Dictionary<long, string> BuildMeshNameMap(string raw)
        {
            var map = new Dictionary<long, string>();
            var vuidRe = new Regex(
                "\"vuid\"\\s*:\\s*(\\d+)(.*?)(?=\"vuid\"\\s*:\\s*\\d+|\\Z)",
                RegexOptions.Singleline);
            foreach (Match m in vuidRe.Matches(raw))
            {
                long vuid = long.Parse(m.Groups[1].Value);
                if (map.ContainsKey(vuid)) continue;
                var nameM = Regex.Match(m.Groups[2].Value, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                map[vuid] = nameM.Success ? nameM.Groups[1].Value.Trim() : "";
            }
            return map;
        }

        
        public class PartHit
        {
            public long vuid;
            public string meshName;
            public string oldMat;
            public int occurrenceIndex;
            public override string ToString() => $"{vuid} {(string.IsNullOrEmpty(meshName) ? "(unknown)" : meshName)} [{oldMat}]";
        }

        private static bool NameMatches(string name, string[] prefixes, string[] contains)
        {
            if (prefixes == null && contains == null) return true; 
            if (string.IsNullOrEmpty(name)) return false;
            if (prefixes != null)
                foreach (var p in prefixes)
                    if (!string.IsNullOrEmpty(p) && name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        return true;
            if (contains != null)
                foreach (var c in contains)
                    if (!string.IsNullOrEmpty(c) && name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            return false;
        }
    }
}
