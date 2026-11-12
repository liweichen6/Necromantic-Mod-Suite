using System;
using UnityEngine;
using UnityEngine.UI;

namespace NecromanticCommon
{
    /// <summary>
    /// Shared uGUI ScreenSpaceOverlay canvas used by every Necromantic plugin to show
    /// short notification banners (e.g. "God Mode: ENABLED"). Renders at sortingOrder
    /// 32700 so it sits above the game's own canvases.
    ///
    /// The root object is HideAndDontSave so it survives scene loads and is invisible to
    /// FindObjectsOfType-style cleanup. If it is destroyed anyway, <see cref="Instance"/>
    /// rebuilds it on next access.
    /// </summary>
    public class CanvasOverlay : MonoBehaviour
    {
        private static CanvasOverlay _instance;

        public static CanvasOverlay Instance
        {
            get
            {
                // Unity's overloaded == also catches destroyed instances.
                if (_instance == null)
                {
                    EnsureInitialized();
                }
                return _instance;
            }
        }

        private GameObject _bannerGo;
        private Text _bannerText;
        private CanvasGroup _bannerGroup;
        private float _bannerExpiresAt;

        public static void EnsureInitialized()
        {
            if (_instance != null) return;

            GameObject root = new GameObject("Necromantic_CanvasOverlay");
            root.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<CanvasOverlay>();
            _instance.InitUI();
        }

        /// <summary>
        /// Unity 2022.2+ removed the "Arial.ttf" builtin (it now throws / returns null);
        /// "LegacyRuntime.ttf" is its replacement. Try both, then fall back to an OS font.
        /// </summary>
        private static Font GetDefaultFont()
        {
            string[] builtins = { "LegacyRuntime.ttf", "Arial.ttf" };
            foreach (string name in builtins)
            {
                try
                {
                    Font f = Resources.GetBuiltinResource<Font>(name);
                    if (f != null) return f;
                }
                catch { }
            }

            try
            {
                Font f = Font.CreateDynamicFontFromOSFont("Arial", 16);
                if (f != null) return f;
            }
            catch { }

            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            return (all != null && all.Length > 0) ? all[0] : null;
        }

        private void InitUI()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32700;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Banner background
            _bannerGo = new GameObject("NotificationBanner");
            _bannerGo.transform.SetParent(transform, false);

            RectTransform bannerRect = _bannerGo.AddComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.anchoredPosition = new Vector2(0f, -18f);
            bannerRect.sizeDelta = new Vector2(820f, 44f);

            Image bg = _bannerGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.12f, 0.94f);
            bg.raycastTarget = false;

            _bannerGroup = _bannerGo.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;

            // Banner text
            GameObject textGo = new GameObject("BannerText");
            textGo.transform.SetParent(_bannerGo.transform, false);
            RectTransform tr = textGo.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(16f, 0f);
            tr.offsetMax = new Vector2(-16f, 0f);

            _bannerText = textGo.AddComponent<Text>();
            _bannerText.font = GetDefaultFont();
            _bannerText.fontSize = 16;
            _bannerText.fontStyle = FontStyle.Bold;
            _bannerText.alignment = TextAnchor.MiddleCenter;
            _bannerText.color = new Color(0.96f, 0.88f, 0.38f, 1f);
            _bannerText.raycastTarget = false;
            _bannerText.text = "";
        }

        private void Update()
        {
            if (_bannerGroup != null && _bannerGroup.alpha > 0f && Time.unscaledTime >= _bannerExpiresAt)
            {
                _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
            }
        }

        /// <summary>
        /// Shows a banner at the top-centre of the screen for <paramref name="durationSeconds"/>
        /// (unscaled time, so it works while the game is paused or sped up).
        /// </summary>
        public void ShowNotification(string message, float durationSeconds = 5.0f)
        {
            if (_bannerText == null || _bannerGroup == null) return;
            _bannerText.text = message;
            _bannerGroup.alpha = 1f;
            _bannerExpiresAt = Time.unscaledTime + durationSeconds;
        }

        /// <summary>
        /// Exception-safe static helper so callers never have to wrap overlay calls.
        /// </summary>
        public static void Notify(string message, float durationSeconds = 3.0f)
        {
            try
            {
                CanvasOverlay overlay = Instance;
                if (overlay != null) overlay.ShowNotification(message, durationSeconds);
            }
            catch { }
        }
    }
}
