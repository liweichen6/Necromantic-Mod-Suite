using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Blinkmoon.GameSystem;
using NecromanticCommon;

namespace NecromanticQoL
{
    /// <summary>
    /// Top-right minimap for matches (the game has none). Rotates with the camera so "up" on
    /// the map is "up" on screen. Shows the local player, other players, enemies by tier,
    /// chests and pickups. Out-of-range bosses, minibosses and chests stick to the edge so
    /// their direction is always visible.
    ///
    /// Objects are found with FindObjectsOfType a few times per second (not per frame); only
    /// their positions are read every frame.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<float> SizePixels;
        public static ConfigEntry<float> RangeUnits;
        public static ConfigEntry<bool> ShowPickups;

        private enum Kind { Mob, Elite, Miniboss, Boss, Player, Chest, Pickup }

        private struct Blip
        {
            public Transform T;
            public DynamicAttributes Attr; // mobs only, to skip dead ones
            public Kind Kind;
        }

        private const float ScanInterval = 0.25f;
        private const int MaxPickups = 400;

        private readonly List<Blip> _blips = new List<Blip>();
        private float _nextScan;
        private Texture2D _white;

        public static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("Minimap", "EnableMinimap", true, "Show the minimap during matches.");
            ToggleKey = config.Bind("Minimap", "ToggleMinimapKey", KeyCode.M, "Show/hide the minimap.");
            SizePixels = config.Bind("Minimap", "SizePixels", 220f, "Minimap width/height in pixels at 1080p (scaled with resolution).");
            RangeUnits = config.Bind("Minimap", "RangeUnits", 60f, "World units from the player to the map edge. Larger = more area, smaller dots spacing.");
            ShowPickups = config.Bind("Minimap", "ShowPickups", true, "Show XP gems, scrolls and other pickups.");
        }

        private void Update()
        {
            if (HotkeyHelper.IsKeyDown(ToggleKey.Value, QoLPlugin.Log, "Toggle Minimap"))
            {
                Enabled.Value = !Enabled.Value;
                CanvasOverlay.Notify("Minimap: " + (Enabled.Value ? "ON" : "OFF"), 2.0f);
            }

            if (!Enabled.Value || !InMatch()) { _blips.Clear(); return; }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                Rescan();
            }
        }

        private static bool InMatch()
        {
            try { return Map.Instance != null && PlayerManager.LocalPlayer != null; }
            catch { return false; }
        }

        private void Rescan()
        {
            _blips.Clear();
            try
            {
                foreach (MobBehaviour mob in FindObjectsOfType<MobBehaviour>())
                {
                    Blip b;
                    b.T = mob.transform;
                    b.Attr = mob.GetComponent<DynamicAttributes>();
                    switch (mob.info.type)
                    {
                        case MobBehaviour.MobType.Elite: b.Kind = Kind.Elite; break;
                        case MobBehaviour.MobType.Miniboss: b.Kind = Kind.Miniboss; break;
                        case MobBehaviour.MobType.Boss: b.Kind = Kind.Boss; break;
                        default: b.Kind = Kind.Mob; break;
                    }
                    _blips.Add(b);
                }

                Character local = PlayerManager.LocalPlayer;
                foreach (Character c in FindObjectsOfType<Character>())
                {
                    if (c == local) continue;
                    _blips.Add(new Blip { T = c.transform, Kind = Kind.Player });
                }

                foreach (CaptureChestBase chest in FindObjectsOfType<CaptureChestBase>())
                {
                    _blips.Add(new Blip { T = chest.transform, Kind = Kind.Chest });
                }

                if (ShowPickups.Value)
                {
                    int n = 0;
                    foreach (CollectibleBase col in FindObjectsOfType<CollectibleBase>())
                    {
                        if (++n > MaxPickups) break;
                        _blips.Add(new Blip { T = col.transform, Kind = Kind.Pickup });
                    }
                }
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogWarning("Minimap scan failed: " + ex.Message);
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!Enabled.Value || !InMatch()) return;

            Character local = PlayerManager.LocalPlayer;
            if (local == null) return;
            if (_white == null) _white = Texture2D.whiteTexture;

            float scale = Screen.height / 1080f;
            float size = Mathf.Clamp(SizePixels.Value, 80f, 600f) * scale;
            float margin = 12f * scale;
            Rect area = new Rect(Screen.width - size - margin, margin, size, size);
            Vector2 center = area.center;
            float half = size * 0.5f;
            float range = Mathf.Max(5f, RangeUnits.Value);
            float pxPerUnit = half / range;

            // Map axes follow the camera's heading on the ground plane.
            Vector3 up = Vector3.forward;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 0.0001f) up = f.normalized;
            }
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 origin = local.transform.position;

            Color prev = GUI.color;
            DrawRect(area, new Color(0.05f, 0.05f, 0.08f, 0.70f));
            DrawFrame(area, new Color(0.6f, 0.6f, 0.7f, 0.9f), Mathf.Max(1f, scale));

            // Pickups first so enemies and chests draw on top.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _blips.Count; i++)
                {
                    Blip b = _blips[i];
                    bool isPickup = b.Kind == Kind.Pickup;
                    if ((pass == 0) != isPickup) continue;
                    if (b.T == null || !b.T.gameObject.activeInHierarchy) continue;
                    if (b.Attr != null && b.Attr.isDead) continue;

                    Vector3 d = b.T.position - origin;
                    Vector2 p = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)) * pxPerUnit;

                    bool pinToEdge = b.Kind == Kind.Boss || b.Kind == Kind.Miniboss || b.Kind == Kind.Chest || b.Kind == Kind.Player;
                    float limit = half - 3f * scale;
                    if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.y) > limit)
                    {
                        if (!pinToEdge) continue;
                        float k = limit / Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                        p *= k;
                    }

                    Color color; float dot;
                    Style(b.Kind, out color, out dot);
                    DrawDot(new Vector2(center.x + p.x, center.y - p.y), dot * scale, color);
                }
            }

            // Local player last, on top.
            DrawDot(center, 7f * scale, Color.white);
            GUI.color = prev;
        }

        private static void Style(Kind kind, out Color color, out float size)
        {
            switch (kind)
            {
                case Kind.Elite:    color = new Color(1.00f, 0.55f, 0.10f); size = 5f; return;
                case Kind.Miniboss: color = new Color(0.95f, 0.25f, 0.95f); size = 8f; return;
                case Kind.Boss:     color = new Color(1.00f, 0.15f, 0.55f); size = 11f; return;
                case Kind.Player:   color = new Color(0.30f, 0.85f, 1.00f); size = 7f; return;
                case Kind.Chest:    color = new Color(1.00f, 0.90f, 0.20f); size = 7f; return;
                case Kind.Pickup:   color = new Color(0.40f, 0.95f, 0.50f, 0.8f); size = 2f; return;
                default:            color = new Color(0.95f, 0.20f, 0.20f); size = 3f; return;
            }
        }

        private void DrawDot(Vector2 c, float size, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), _white);
        }

        private void DrawRect(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, _white);
        }

        private void DrawFrame(Rect r, Color color, float t)
        {
            DrawRect(new Rect(r.x, r.y, r.width, t), color);
            DrawRect(new Rect(r.x, r.yMax - t, r.width, t), color);
            DrawRect(new Rect(r.x, r.y, t, r.height), color);
            DrawRect(new Rect(r.xMax - t, r.y, t, r.height), color);
        }
    }
}
