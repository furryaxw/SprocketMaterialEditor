using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using Il2CppSprocket.Blueprints;
using Il2CppSprocket.Vehicles.Serialization;
using Il2CppSprocket.VehicleDesigner.Access;
using Il2CppSprocket.Vehicles;
using Il2CppSprocket.Vehicles.PlateStructures;
using Il2CppSprocket.PlateStructures.Blueprints;
using Il2CppSprocket.Vehicles.Selection;

[assembly: MelonInfo(typeof(Sprocket.MaterialEditor.MaterialEditorMod), "Sec's Material Editor", "1.8.25", "Sec")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.Id", "sectumsempra.sprocket-material-editor")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.DisplayName", "Sec's Material Editor")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.Description", "In-game editor for changing armour materials on saved Sprocket vehicle blueprints.")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.Authors", "Sectumsempra")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.Repository", "furryaxw/SprocketMaterialEditor")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.Category", "utility")]
[assembly: System.Reflection.AssemblyMetadata("Sprocket.Mod.License", "MIT")]
[assembly: MelonColor(0, 255, 255, 255)]

namespace Sprocket.MaterialEditor
{
    
    public static class UIPanelState
    {
        public static bool IsOpen;
        public static Rect Bounds;
    }

    public class MaterialEditorMod : MelonMod
    {
        public const string Name = "Sec's Material Editor";
        public const string Version = "1.8.24";

        
        private class MatInfo
        {
            public string id;
            public Dictionary<string, string> props = new Dictionary<string, string>();
            public bool isArmour;   
        }

        private bool _show;
        private bool _guiErrorLogged;
        private Rect _area;

        
        private static System.Reflection.FieldInfo _otherOpen, _otherRect;
        private bool _areaInit;
        private bool _dragging;
        private Vector2 _dragOffset;

        private string _resolvedPath;
        private List<BlueprintMaterial.PartHit> _allParts = new List<BlueprintMaterial.PartHit>();
        private List<BlueprintMaterial.PartHit> _selectedParts = new List<BlueprintMaterial.PartHit>();
        private List<MatInfo> _availableMaterials = new List<MatInfo>();
        private Dictionary<string, float> _densityByName = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private string _selectedMaterial;
        private string _log = "Save a vehicle in game, then click Refresh.";
        private int _matPage = 0;
        private int _partPage = 0;

        
        private bool _typing;
        private string _typingTarget;   
        private string _typed = "";
        private bool _shift;

        private GUIStyle _sTitle;
        private GUIStyle _sLabel;
        private GUIStyle _sButton;
        private GUIStyle _sSelected;        private GUIStyle _sBtnText, _sBtnTextSel;
        public override void OnInitializeMelon()
        {
            LoadMaterials();
            MelonLogger.Msg($"[{Name}] v{Version} loaded. F8 or left-middle button to open.");
        }

        private bool IsArmour(string txt) => txt.Contains("\"rhaFactor\"");

        
        private void ScanTechFile(Dictionary<string, MatInfo> dict, string file)
        {
            try
            {
                string txt = File.ReadAllText(file);
                var tm = new Regex("\"type\"\\s*:\\s*\"([^\"]+)\"");
                if (!tm.IsMatch(txt)) return;
                string id = tm.Match(txt).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(id)) return;

                var mi = new MatInfo { id = id, isArmour = IsArmour(txt) };
                int pStart = txt.IndexOf("\"properties\"", StringComparison.Ordinal);
                if (pStart >= 0)
                {
                    int brace = txt.IndexOf('{', pStart);
                    int depth = 0, end = -1;
                    for (int i = brace; i < txt.Length; i++)
                    {
                        if (txt[i] == '{') depth++;
                        else if (txt[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                    }
                    if (end > brace)
                    {
                        string propsTxt = txt.Substring(brace, end - brace + 1);
                        foreach (Match pm in new Regex("\"(?<k>[A-Za-z_][A-Za-z0-9_]*)\\s*\"\\s*:\\s*(?<v>-?\\d+\\.?\\d*)").Matches(propsTxt))
                            mi.props[pm.Groups["k"].Value] = pm.Groups["v"].Value;
                    }
                }
                dict[id] = mi;
            }
            catch { }
        }

        
        private void LoadMaterials()
        {
            var dict = new Dictionary<string, MatInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                
                string techDir = Path.Combine(Application.dataPath, "StreamingAssets", "Technology");
                if (Directory.Exists(techDir))
                    foreach (var f in Directory.EnumerateFiles(techDir, "*.json", SearchOption.TopDirectoryOnly))
                        ScanTechFile(dict, f);

                
                string modsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                if (Directory.Exists(modsDir))
                    foreach (var f in Directory.EnumerateFiles(modsDir, "*.json", SearchOption.AllDirectories))
                        ScanTechFile(dict, f);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[MaterialEditor] material scan warning: " + ex.Message);
            }

            _availableMaterials = dict.Values.OrderBy(m => m.id).ToList();
            if (_availableMaterials.Count == 0)
            {
                _availableMaterials.Add(new MatInfo { id = "70Aluminum" });
                _availableMaterials.Add(new MatInfo { id = "rha" });
            }
            int armour = _availableMaterials.Count(m => m.isArmour);
            MelonLogger.Msg($"[{Name}] materials: {_availableMaterials.Count} total files, {armour} armour (base+mods).");

            
            _densityByName = _availableMaterials
                .Where(m => m.props.ContainsKey("density"))
                .ToDictionary(m => m.id, m => float.Parse(m.props["density"], System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        }

        private string FormatProps(MatInfo m)
        {
            if (m.props.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in m.props)
                parts.Add(kv.Key + "=" + kv.Value);
            return string.Join("  ", parts);
        }

        private void EnsureStyles()
        {
            if (_sTitle != null) return;

            
            
            
            

            var cTextGold = new Color(0.95f, 0.78f, 0.42f, 1f);    
            var cTextLight = new Color(0.90f, 0.90f, 0.90f, 1f);   
            var cTextMuted = new Color(0.72f, 0.72f, 0.72f, 1f);   
            var cTextBright = new Color(1.00f, 0.95f, 0.70f, 1f);  
            var cBg = new Color(0.16f, 0.17f, 0.19f, 0.88f);      
            var cBar = new Color(0.10f, 0.11f, 0.12f, 0.92f);     

            _sTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = cTextLight },
                hover = { textColor = cTextLight },
                active = { textColor = cTextLight }
            };

            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = cTextLight },
                hover = { textColor = cTextLight },
                active = { textColor = cTextLight }
            };

            _sButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = cTextGold, background = Texture2D.whiteTexture },
                hover = { textColor = cTextBright, background = Texture2D.whiteTexture },
                active = { textColor = cTextBright, background = Texture2D.whiteTexture },
                focused = { textColor = cTextGold, background = Texture2D.whiteTexture },
                onNormal = { textColor = cTextBright, background = Texture2D.whiteTexture },
                onHover = { textColor = cTextBright, background = Texture2D.whiteTexture },
                onActive = { textColor = cTextBright, background = Texture2D.whiteTexture },
                border = new RectOffset(0, 0, 0, 0)
            };

            _sSelected = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = cTextBright, background = Texture2D.whiteTexture },
                hover = { textColor = Color.white, background = Texture2D.whiteTexture },
                active = { textColor = cTextBright, background = Texture2D.whiteTexture },
                focused = { textColor = cTextBright, background = Texture2D.whiteTexture },
                onNormal = { textColor = cTextBright, background = Texture2D.whiteTexture },
                onHover = { textColor = Color.white, background = Texture2D.whiteTexture },
                onActive = { textColor = cTextBright, background = Texture2D.whiteTexture },
                border = new RectOffset(0, 0, 0, 0)
            };
            
            _sBtnText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = cTextGold },
                hover = { textColor = cTextBright },
                active = { textColor = cTextBright }
            };
            _sBtnTextSel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = cTextBright },
                hover = { textColor = cTextBright },
                active = { textColor = cTextBright }
            };
        }

        
        private void DrawModalMask()
        {
            
            Rect? hole = null;
            if (TryGetOtherPanel(out var hr)) hole = hr;
            DrawDimWithHole(hole);
        }

        
        private static void DrawDimWithHole(Rect? hole)
        {
            var prev = GUI.color;
            float sw = Screen.width, sh = Screen.height;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            if (hole == null)
            {
                GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture, ScaleMode.StretchToFill);
                GUI.color = prev;
                return;
            }
            Rect h = hole.Value;
            if (h.x < 0) { h.width += h.x; h.x = 0; }
            if (h.y < 0) { h.height += h.y; h.y = 0; }
            if (h.x + h.width > sw) h.width = sw - h.x;
            if (h.y + h.height > sh) h.height = sh - h.y;
            if (h.width <= 0 || h.height <= 0)
            {
                GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture, ScaleMode.StretchToFill);
                GUI.color = prev;
                return;
            }
            if (h.y > 0) GUI.DrawTexture(new Rect(0f, 0f, sw, h.y), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            float by = h.y + h.height;
            if (by < sh) GUI.DrawTexture(new Rect(0f, by, sw, sh - by), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            if (h.x > 0) GUI.DrawTexture(new Rect(0f, h.y, h.x, h.height), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            float rx = h.x + h.width;
            if (rx < sw) GUI.DrawTexture(new Rect(rx, h.y, sw - rx, h.height), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        
        private static bool TryGetOtherPanel(out Rect r)
        {
            r = default;
            if (_otherOpen == null) CacheOtherPanelState("SprocketFCS.UIPanelState");
            if (_otherOpen == null) return false;
            if (!(bool)_otherOpen.GetValue(null)) return false;
            r = (Rect)_otherRect.GetValue(null);
            return true;
        }

        private static void CacheOtherPanelState(string typeName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName);
                if (t != null)
                {
                    _otherOpen = t.GetField("IsOpen");
                    _otherRect = t.GetField("Bounds");
                    return;
                }
            }
            
        }

        private void LoadParts()
        {
            _resolvedPath = FindLatestBlueprint();
            _matPage = 0;
            _partPage = 0;
            if (_resolvedPath != null && File.Exists(_resolvedPath))
            {
                _allParts = BlueprintMaterial.ListAllParts(_resolvedPath);
                _log = $"Loaded {Path.GetFileName(_resolvedPath)}. {_allParts.Count} parts found.";
            }
            else
            {
                _allParts = new List<BlueprintMaterial.PartHit>();
                _log = "No blueprint found. Save a vehicle in game first.";
            }
            _selectedParts.Clear();
        }

        
        
        
        
        
        
        
        
        private void UseGameSelection()
        {
            try
            {
                var designer = UnityEngine.Object.FindObjectOfType<Il2CppSprocket.VehicleDesigner.VehicleDesignerCore>();
                if (designer == null) { _log = "Designer not found. Open the vehicle designer first."; return; }

                var src = designer.TryCast<IVehicleEditorSource>();
                if (src == null) { _log = "IVehicleEditorSource not available."; return; }
                var editor = src.Editor;
                if (editor == null) { _log = "Editor not available (no active design?)."; return; }
                var sel = editor.SelectionReader;
                if (sel == null) { _log = "SelectionReader not available."; return; }

                int count = sel.Count;
                var selectedVuids = new List<long>();
                int skipped = 0;
                for (int i = 0; i < count; i++)
                {
                    var comp = sel.Items[i];
                    if (comp == null) { skipped++; continue; }
                    var plate = comp.TryCast<PlateStructure>();
                    if (plate == null) { skipped++; continue; }   
                    selectedVuids.Add(plate.Blueprint.BodyMeshVUID);
                }

                if (string.IsNullOrEmpty(_resolvedPath) || !File.Exists(_resolvedPath))
                { _log = "No blueprint loaded. Click 'Refresh last saved blueprint' first."; return; }

                _selectedParts = BlueprintMaterial.MatchSelected(_resolvedPath, selectedVuids);
                _log = $"Read {count} in-game selected ({skipped} non-armour skipped) -> matched {_selectedParts.Count} armour blocks (by bodyMeshVuid) in blueprint. Same-vuid blocks rewritten together.";
                MelonLogger.Msg(_log);
            }
            catch (Exception e)
            {
                _log = "Selection read error: " + e.Message;
                MelonLogger.Error(e);
            }
        }

        
        
        
        private string SelectedMaterialSummary()
        {
            int n = _selectedParts?.Count ?? 0;
            if (n == 0) return "  Current material: (nothing selected)";
            var mats = _selectedParts
                .Select(p => string.IsNullOrEmpty(p.oldMat) ? "(empty)" : p.oldMat)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (mats.Count == 1)
                return $"  Current material: {mats[0]}  ({n} part{(n == 1 ? "" : "s")})";
            return $"  Non-uniform material — not shown (selected {n} parts, {mats.Count} different materials)";
        }

        
        private void StartTyping(string target)
        {
            _typingTarget = target;
            _typed = (target == "material") ? (_selectedMaterial ?? "") : "";
            _shift = false;
            _typing = true;
        }

        private void CommitTyping()
        {
            if (_typingTarget == "material")
                _selectedMaterial = _typed.Trim();
            _typing = false;
            _typed = "";
        }

        private void DrawKeyboard(float x, float y, float w)
        {
            float lineH = 24f;
            float btnH = 42f;
            float gap = 4f;
            GUI.Label(new Rect(x, y, w, lineH), "Type MATERIAL name:", _sLabel); y += lineH;
            string display = (_typed.Length == 0) ? "<type...>" : _typed;
            GUI.Label(new Rect(x, y, w, 36f), ">> " + display, _sSelected); y += 36f + gap;

            
            float keyW = 58f;
            float rowW = 10f * keyW + 9f * gap;
            float cx0 = x + Mathf.Max(0f, (w - rowW) * 0.5f);
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
            foreach (var row in rows)
            {
                float cx = cx0;
                foreach (char c in row)
                {
                    string ch = _shift ? char.ToUpper(c).ToString() : c.ToString();
                    if (ThemedButton(new Rect(cx, y, keyW, btnH), ch, _sButton))
                        _typed += ch;
                    cx += keyW + gap;
                }
                y += btnH + gap;
            }

            
            string[] syms = { ",", ".", "-", "_", "/", "x" };
            float symW = 58f;
            float symRowW = syms.Length * symW + (syms.Length - 1) * gap;
            float sx0 = x + Mathf.Max(0f, (w - symRowW) * 0.5f);
            float sx = sx0;
            foreach (var sym in syms)
            {
                if (ThemedButton(new Rect(sx, y, symW, btnH), sym, _sButton))
                    _typed += sym;
                sx += symW + gap;
            }
            y += btnH + gap;

            
            float ctrlW = (w - 2f * gap) / 3f;
            if (ThemedButton(new Rect(x, y, ctrlW, btnH), _shift ? "SHIFT ON" : "shift", _sButton))
                _shift = !_shift;
            if (ThemedButton(new Rect(x + ctrlW + gap, y, ctrlW, btnH), "SPACE", _sButton))
                _typed += " ";
            if (ThemedButton(new Rect(x + 2f * (ctrlW + gap), y, ctrlW, btnH), "BKSP", _sButton) && _typed.Length > 0)
                _typed = _typed.Substring(0, _typed.Length - 1);
            y += btnH + gap;

            if (ThemedButton(new Rect(x, y, ctrlW, btnH), "CLR", _sButton))
                _typed = "";
            if (ThemedButton(new Rect(x + ctrlW + gap, y, ctrlW, btnH), "CANCEL", _sButton))
            {
                _typing = false;
                _typed = "";
            }
            if (ThemedButton(new Rect(x + 2f * (ctrlW + gap), y, ctrlW, btnH), "DONE", _sButton))
                CommitTyping();
            y += btnH + gap;
        }

        
        
        
        private bool ThemedButton(Rect r, string label, GUIStyle style)
        {
            bool selected = ReferenceEquals(style, _sSelected);
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            var prev = GUI.color;
            
            GUI.color = (selected || hover) ? new Color(0.196f, 0.196f, 0.196f, 1f) : new Color(0.117f, 0.117f, 0.117f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = prev;
            bool res = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.Label(r, label, (selected || hover) ? _sBtnTextSel : _sBtnText);
            return res;
        }

        
        private int PageCount(int total, int pageSize) => total <= 0 ? 1 : (total + pageSize - 1) / pageSize;

        private void PageButtonsR(ref float y, float x, float w, ref int page, int total, int pageSize)
        {
            int pages = PageCount(total, pageSize);
            if (pages <= 1) return;
            const float labelW = 120f;
            float btnW = (w - labelW - 2f * 4f) / 2f; 
            if (ThemedButton(new Rect(x, y, btnW, 32f), "< Prev", _sButton)) page--;
            GUI.Label(new Rect(x + btnW + 4f, y, labelW, 32f), $"Page {page + 1}/{pages}", _sLabel);
            if (ThemedButton(new Rect(x + btnW + labelW + 8f, y, btnW, 32f), "Next >", _sButton)) page++;
            page = Mathf.Clamp(page, 0, pages - 1);
            y += 32f + 2f;
        }

        
        private void SafeDraw(string section, Action draw)
        {
            try
            {
                draw();
            }
            catch (Exception ex)
            {
                if (!_guiErrorLogged)
                {
                    MelonLogger.Error($"[MaterialEditor] OnGUI section '{section}' error: {ex}");
                    _guiErrorLogged = true;
                }
                throw;
            }
        }

        public override void OnGUI()
        {
            try
            {
                EnsureStyles();

                var e = Event.current;
                if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F8)
                {
                    _show = !_show;
                    e.Use();
                }

                
                if (!_areaInit)
                {
                    float panelW = Mathf.Max(300f, Mathf.Min(760f, Screen.width - 20f));
                    float panelH = Mathf.Max(200f, Mathf.Min(1050f, Screen.height - 20f));
                    float y = 10f;
                    if (y + panelH > Screen.height) y = Screen.height - panelH - 10f;
                    if (y < 0) y = 0;
                    _area = new Rect(10f, y, panelW, panelH);
                    _areaInit = true;
                }

                
                UIPanelState.IsOpen = _show;
                UIPanelState.Bounds = _area;

                
                int rowsPerPage = Mathf.Clamp((int)((_area.height - 40f - 300f) / 58f), 2, 7);
                int matPageSize = rowsPerPage;          
                const int partCols = 2;                 
                int partPageSize = rowsPerPage * partCols;
                var armourMaterials = _availableMaterials?.Where(m => m.isArmour).ToList() ?? new List<MatInfo>();
                int matCount = armourMaterials.Count;
                int partCount = _allParts?.Count ?? 0;
                int matPages = PageCount(matCount, matPageSize);
                int partPages = PageCount(partCount, partPageSize);
                if (_matPage >= matPages) _matPage = 0;
                if (_partPage >= partPages) _partPage = 0;

                if (!_show)
                {
                    float by = _area.y;
                    if (by + 40 > Screen.height) by = Screen.height - 40;
                    if (by < 0) by = 0;
                    if (ThemedButton(new Rect(0, by, 220, 40), "Material Editor [+]", _sButton))
                    {
                        _show = true;
                        if (_allParts == null || _allParts.Count == 0) LoadParts();
                    }
                    return;
                }

                const float barH = 40f;
                Rect barRect = new Rect(_area.x, _area.y, _area.width, barH);
                Rect xRect = new Rect(_area.x + _area.width - 42f, _area.y + 4f, 38f, 32f);

                
                if (e != null)
                {
                    if (e.type == EventType.MouseDown && barRect.Contains(e.mousePosition) && !xRect.Contains(e.mousePosition))
                    {
                        _dragging = true;
                        _dragOffset = e.mousePosition - new Vector2(_area.x, _area.y);
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDrag && _dragging)
                    {
                        Vector2 np = e.mousePosition - _dragOffset;
                        _area.x = Mathf.Clamp(np.x, 0f, Screen.width - _area.width);
                        _area.y = Mathf.Clamp(np.y, 0f, Screen.height - _area.height);
                        e.Use();
                    }
                    else if (e.type == EventType.MouseUp && _dragging)
                    {
                        _dragging = false;
                        e.Use();
                    }
                    else if (e.type == EventType.ScrollWheel)
                    {
                        if (_area.Contains(e.mousePosition))
                        {
                            float split = _area.y + _area.height * 0.45f;
                            if (e.mousePosition.y < split) 
                            {
                                if (e.delta.y > 0) _partPage = Mathf.Max(0, _partPage - 1);
                                else _partPage = Mathf.Min(partPages - 1, _partPage + 1);
                            }
                            else 
                            {
                                if (e.delta.y > 0) _matPage = Mathf.Max(0, _matPage - 1);
                                else _matPage = Mathf.Min(matPages - 1, _matPage + 1);
                            }
                            e.Use();
                        }
                        else
                        {
                            
                            e.Use();
                        }
                    }
                }

                
                DrawModalMask();

                
                var prevColor = GUI.color;
                GUI.color = new Color(0.16f, 0.17f, 0.19f, 0.92f);   
                GUI.DrawTexture(_area, Texture2D.whiteTexture, ScaleMode.StretchToFill);
                GUI.color = new Color(0.10f, 0.11f, 0.12f, 0.96f);  
                GUI.DrawTexture(barRect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
                GUI.color = prevColor;
                GUI.Label(new Rect(_area.x + 10f, _area.y + 6f, _area.width - 60f, 28f),
                    _dragging ? "Material Editor (F8)  <<drag>>" : "Material Editor (F8 · drag to move)", _sTitle);
                if (ThemedButton(xRect, "X", _sButton))
                    _show = false;

                
                Rect bodyRect = new Rect(_area.x, _area.y + barH, _area.width, _area.height - barH);
                GUI.BeginGroup(bodyRect);
                float mx = 6f;                       
                float my = 4f;                       
                float mw = bodyRect.width - 12f;     
                float lineH = 22f;
                float gap = 4f;
                float bigBtnH = 30f;
                float smallBtnH = 28f;
                float rowBtnH = 26f;

                
                if (_typing)
                {
                    DrawKeyboard(mx, my, mw);
                    GUI.EndGroup();
                    _guiErrorLogged = false;
                    return;
                }

                
                GUI.Label(new Rect(mx, my, mw, lineH), "1. Refresh / select blueprint:", _sLabel); my += lineH;
                GUI.Label(new Rect(mx, my, mw, lineH), "  " + (_resolvedPath != null ? Path.GetFileName(_resolvedPath) : "none"), _sLabel); my += lineH;
                if (ThemedButton(new Rect(mx, my, mw, bigBtnH), "Refresh last saved blueprint", _sButton))
                    LoadParts();
                my += bigBtnH + gap;

                SafeDraw("parts", () =>
                {
                    my += 2f;
                    GUI.Label(new Rect(mx, my, mw, lineH), "2. Select parts (reads selected blocks' bodyMeshVuid):", _sLabel); my += lineH;
                    if (ThemedButton(new Rect(mx, my, mw, bigBtnH), "Use selected in game", _sButton))
                        UseGameSelection();
                    my += bigBtnH + gap;

                    float halfW = (mw - gap) / 2f;
                    if (ThemedButton(new Rect(mx, my, halfW, smallBtnH), "All", _sButton))
                        _selectedParts = new List<BlueprintMaterial.PartHit>(_allParts ?? new List<BlueprintMaterial.PartHit>());
                    if (ThemedButton(new Rect(mx + halfW + gap, my, halfW, smallBtnH), "Clear", _sButton))
                        _selectedParts.Clear();
                    my += smallBtnH + gap;

                    GUI.Label(new Rect(mx, my, mw, lineH), $"  Selected: {(_selectedParts?.Count ?? 0)}  (mouse wheel to scroll)", _sLabel); my += lineH;
                    GUI.Label(new Rect(mx, my, mw, lineH), SelectedMaterialSummary(), _sLabel); my += lineH;

                    PageButtonsR(ref my, mx, mw, ref _partPage, partCount, partPageSize);
                    int pStart = _partPage * partPageSize;
                    int pEnd = Mathf.Min(pStart + partPageSize, partCount);
                    const int partCols = 2;
                    float colW = (mw - gap) / 2f;
                    if (_allParts != null && pStart >= 0 && pStart < partCount && pEnd > pStart)
                    {
                        for (int row = 0; row < rowsPerPage; row++)
                        {
                            float cx = mx;
                            for (int c = 0; c < partCols; c++)
                            {
                                int idx = pStart + row * partCols + c;
                                if (idx >= 0 && idx < pEnd && idx < _allParts.Count)
                                {
                                    var p = _allParts[idx];
                                    if (p != null)
                                    {
                                        bool sel = _selectedParts.Contains(p);
                                        if (ThemedButton(new Rect(cx, my, colW, rowBtnH), p.ToString(), (sel ? _sSelected : _sButton)))
                                        {
                                            if (sel) _selectedParts.Remove(p);
                                            else _selectedParts.Add(p);
                                        }
                                    }
                                    else
                                    {
                                        GUI.Label(new Rect(cx, my, colW, rowBtnH), "null", _sBtnText);
                                    }
                                }
                                cx += colW + gap;
                            }
                            my += rowBtnH + 2f;
                        }
                    }
                });

                SafeDraw("materials", () =>
                {
                    my += 2f;
                    GUI.Label(new Rect(mx, my, mw, lineH), $"3. Select target material  ({matCount} armour materials)  (mouse wheel to scroll)", _sLabel); my += lineH;
                    GUI.Label(new Rect(mx, my, mw, lineH), "   params: rha=等效防护 cost=成本 spall=剥落 density=密度", _sLabel); my += lineH;
                    PageButtonsR(ref my, mx, mw, ref _matPage, matCount, matPageSize);
                    int mStart = _matPage * matPageSize;
                    int mEnd = Mathf.Min(mStart + matPageSize, matCount);
                    const float matBtnW = 165f;
                    float labelW = Mathf.Max(60f, mw - matBtnW - gap);
                    if (mStart >= 0 && mStart < matCount && mEnd > mStart)
                    {
                        for (int i = mStart; i < mEnd; i++)
                        {
                            if (i < 0 || i >= armourMaterials.Count) continue;
                            var m = armourMaterials[i];
                            bool isCurMat = string.Equals(m.id, _selectedMaterial, StringComparison.OrdinalIgnoreCase);
                            if (ThemedButton(new Rect(mx, my, matBtnW, rowBtnH), m.id, isCurMat ? _sSelected : _sButton))
                                _selectedMaterial = m.id;
                            GUI.Label(new Rect(mx + matBtnW + gap, my, labelW, rowBtnH), FormatProps(m), _sLabel);
                            my += rowBtnH + 2f;
                        }
                    }
                    if (ThemedButton(new Rect(mx, my, mw, smallBtnH), "Type custom material name...", _sButton))
                        StartTyping("material");
                    my += smallBtnH + gap;
                });

                my += 4f;
                float applyW = (mw - gap) / 2f;
                if (ThemedButton(new Rect(mx, my, applyW, 34f), "Apply (write copy)", _sButton))
                    DoApply();
                if (ThemedButton(new Rect(mx + applyW + gap, my, applyW, 34f), "Apply in place", _sButton))
                    DoApplyInPlace();
                my += 34f + gap;

                GUI.Label(new Rect(mx, my, mw, lineH), "  After 'Apply in place': re-open this vehicle from the game's Load menu to see changes.", _sLabel); my += lineH;
                GUI.Label(new Rect(mx, my, mw, lineH), _log, _sLabel);

                GUI.EndGroup();
                _guiErrorLogged = false;
            }
            catch (Exception ex)
            {
                if (!_guiErrorLogged)
                {
                    MelonLogger.Error($"[MaterialEditor] OnGUI error: {ex}");
                    _guiErrorLogged = true;
                }
            }
        }

        private void DoApply()
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedMaterial))
                {
                    _log = "Select a material first.";
                    return;
                }
                if (_selectedParts.Count == 0)
                {
                    _log = "Select at least one part.";
                    return;
                }
                if (string.IsNullOrEmpty(_resolvedPath) || !File.Exists(_resolvedPath))
                {
                    _log = "No valid blueprint. Click Refresh first.";
                    return;
                }
                int n = BlueprintMaterial.ApplyToParts(_resolvedPath, _selectedParts, _selectedMaterial, _densityByName, out float md);
                _log = $"Done: {n}/{_selectedParts.Count} parts -> {_selectedMaterial}. Copy in *_{_selectedMaterial} folder.";
                if (n > 0 && Math.Abs(md) > 1e-3f)
                    _log += $" Mass {(md >= 0 ? "+" : "")}{md.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} kg (total now synced).";
                if (n == 0) _log += " (0 replaced: try Refresh and reselect if blueprint changed)";
                MelonLogger.Msg(_log);
            }
            catch (Exception e)
            {
                _log = "Error: " + e.Message;
                MelonLogger.Error(e);
            }
        }

        
        
        
        private void DoApplyInPlace()
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedMaterial)) { _log = "Select a material first."; return; }
                if (_selectedParts.Count == 0) { _log = "Select at least one part."; return; }
                if (string.IsNullOrEmpty(_resolvedPath) || !File.Exists(_resolvedPath)) { _log = "No valid blueprint. Click Refresh first."; return; }

                
                string dir = Path.GetDirectoryName(_resolvedPath);
                string stem = Path.GetFileNameWithoutExtension(_resolvedPath);
                string backupDir = Path.Combine(dir, "_backups");
                Directory.CreateDirectory(backupDir);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(backupDir, $"{stem}_{ts}.blueprint");
                File.Copy(_resolvedPath, backupPath, true);

                
                int n = BlueprintMaterial.ApplyToPartsInPlace(_resolvedPath, _selectedParts, _selectedMaterial, _densityByName, out float md);
                if (n == 0) { _log = $"No matching armourTechID replaced ({_selectedParts.Count} selected). Backup kept."; return; }

                
                string mdStr = (md >= 0 ? "+" : "") + md.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                _log = $"Done: {n}/{_selectedParts.Count} parts -> {_selectedMaterial}. Mass {mdStr} kg. Backup: {Path.GetFileName(backupPath)}. >>> Now RE-OPEN '{Path.GetFileName(_resolvedPath)}' from the game's Load menu to apply.";
                MelonLogger.Msg(_log);
            }
            catch (Exception e)
            {
                _log = "Error: " + e.Message;
                MelonLogger.Error(e);
            }
        }

        
        private string FindLatestBlueprint()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string root = Path.Combine(docs, "My Games", "Sprocket", "Factions");
            if (!Directory.Exists(root)) return null;

            string newest = null;
            DateTime best = DateTime.MinValue;
            foreach (var f in Directory.EnumerateFiles(root, "*.blueprint", SearchOption.AllDirectories))
            {
                var t = File.GetLastWriteTime(f);
                if (t > best) { best = t; newest = f; }
            }
            return newest;
        }
    }
}
