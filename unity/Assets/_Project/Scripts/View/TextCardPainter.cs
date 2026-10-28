using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TableWorld.View
{
    /// <summary>
    /// Draws text cards (<see cref="CardText"/>): faces and backs laid out with UI Toolkit in the HUD's font on an offscreen
    /// panel, up to <see cref="PerBatch"/> at a time, read back and handed to <see cref="TextureBaker"/> like a decoded
    /// image. Each machine draws its own, only when a face is first shown, so a 200-card text deck costs nothing until
    /// its cards are seen. A face with art waits for its image (up to <see cref="ArtWait"/> seconds).
    /// </summary>
    public sealed class TextCardPainter : MonoBehaviour
    {
        public const int CellWidth = 512;
        public const int CellMaxHeight = 1024;
        public const int Columns = 4;
        public const int Rows = 2;
        public const int PerBatch = Columns * Rows;
        public const float ArtWait = 10f;
        /// <summary>Frames between laying a batch out and reading it back (the panel draws in between).</summary>
        private const int FramesToDraw = 2;

        public sealed class Request
        {
            public string Id;
            /// <summary>Null for a back.</summary>
            public CardText Text;
            public CardStyle Style;
            public float Aspect;
            /// <summary>The art's image now, or null while it is not loaded.</summary>
            public Func<Texture2D> Art;
            /// <summary>The art's cell in its image (<c>_BaseMap_ST</c> form).</summary>
            public Vector4 ArtCell;
            /// <summary>True while the art may still arrive.</summary>
            public Func<bool> ArtPending;
            public Action<Texture2D> Done;
            public float Asked;
        }

        private static TextCardPainter _instance;
        private readonly List<Request> _waiting = new List<Request>();
        private readonly List<(Request Request, RectInt Cell)> _batch = new List<(Request, RectInt)>();
        private readonly List<Sprite> _sprites = new List<Sprite>();
        private int _framesLeft;
        private RenderTexture _target;
        private UIDocument _document;
        private StyleSheet _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Faces and backs waiting or being drawn.</summary>
        public static int Pending => _instance != null ? _instance._waiting.Count + _instance._batch.Count : 0;

        public static void Paint(Request request)
        {
            if (_instance == null)
            {
                var host = new GameObject("Text card painter") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<TextCardPainter>();
            }
            request.Asked = Time.realtimeSinceStartup;
            _instance._waiting.Add(request);
        }

        /// <summary>Pixel height of a card drawing of this aspect (height over width), a multiple of 4.</summary>
        public static int CellHeight(float aspect) => Mathf.Clamp(Mathf.RoundToInt(CellWidth * aspect / 4f) * 4, 64, CellMaxHeight);

        private void Awake()
        {
            _target = new RenderTexture(CellWidth * Columns, CellMaxHeight * Rows, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Text cards",
            };
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Text cards (offscreen)";
            panel.targetTexture = _target;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            panel.clearColor = true;
            panel.colorClearValue = Color.clear;
            // The HUD's theme carries its font (Inter) and defaults.
            foreach (var document in Object.FindObjectsByType<UIDocument>())
                if (document.panelSettings != null && document.panelSettings.themeStyleSheet != null)
                {
                    panel.themeStyleSheet = document.panelSettings.themeStyleSheet;
                    break;
                }
            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = panel;
            _style = Resources.Load<StyleSheet>("UI/TextCard");
        }

        private void OnDestroy()
        {
            if (_document != null && _document.panelSettings != null)
                Destroy(_document.panelSettings);
            if (_target != null)
                _target.Release();
            Destroy(_target);
            DropSprites();
        }

        private void Update()
        {
            if (_batch.Count > 0)
            {
                if (--_framesLeft > 0)
                    return;
                ReadBack();
                return;
            }
            if (_waiting.Count == 0)
                return;
            var root = _document.rootVisualElement;
            if (root == null)
                return;
            if (_style != null && !root.styleSheets.Contains(_style))
                root.styleSheets.Add(_style);
            root.Clear();
            var now = Time.realtimeSinceStartup;
            for (var i = 0; i < _waiting.Count && _batch.Count < PerBatch; i++)
            {
                var request = _waiting[i];
                var art = request.Text?.Art != null ? request.Art?.Invoke() : null;
                if (request.Text?.Art != null && art == null && (request.ArtPending?.Invoke() ?? false) && now - request.Asked < ArtWait)
                    continue;
                var slot = _batch.Count;
                var cell = new RectInt(slot % Columns * CellWidth, slot / Columns * CellMaxHeight, CellWidth, CellHeight(request.Aspect));
                var element = request.Text != null ? Face(request, art) : Back(request.Style);
                element.style.left = cell.x;
                element.style.top = cell.y;
                element.style.width = cell.width;
                element.style.height = cell.height;
                root.Add(element);
                _batch.Add((request, cell));
                _waiting.RemoveAt(i--);
            }
            _framesLeft = FramesToDraw;
        }

        private void ReadBack()
        {
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            foreach (var (request, cell) in _batch)
            {
                var texture = new Texture2D(cell.width, cell.height, TextureFormat.RGBA32, false) { name = request.Id, anisoLevel = 4 };
                // Panel rows run down from the top; the texture's from the bottom.
                texture.ReadPixels(new Rect(cell.x, _target.height - cell.y - cell.height, cell.width, cell.height), 0, 0, false);
                texture.Apply(false);
                TextureBaker.Start(texture, request.Done);
            }
            RenderTexture.active = previous;
            _batch.Clear();
            _document.rootVisualElement?.Clear();
            DropSprites();
        }

        private void DropSprites()
        {
            foreach (var sprite in _sprites)
                if (sprite != null)
                    Destroy(sprite);
            _sprites.Clear();
        }

        // ---- layout ------------------------------------------------------------------------------------------------------
        // Per-card colours and fitted font sizes come from the card's data, so they are set in code; the rest is TextCard.uss.

        private VisualElement Face(Request request, Texture2D art)
        {
            var text = request.Text;
            var style = request.Style;
            var paper = Rgb(style?.Paper, CardStyle.DefaultPaper);
            var ink = Rgb(style?.Ink, CardStyle.DefaultInk);
            var accent = Rgb(style?.Accent, CardStyle.DefaultAccent);
            var height = CellHeight(request.Aspect);

            var face = Box("tc-face");
            face.style.backgroundColor = paper;
            SetBorder(face, accent);

            // Words on the coloured band take whichever of dark or light ink reads on it.
            var onAccent = Readable(accent);
            var band = Box("tc-band");
            band.style.backgroundColor = accent;
            var title = Text("tc-title", text.Title ?? "", onAccent);
            title.style.fontSize = TitleSize(text.Title);
            band.Add(title);
            if (!string.IsNullOrWhiteSpace(text.Corner))
            {
                var corner = Text("tc-corner", text.Corner, accent);
                corner.style.backgroundColor = onAccent;
                band.Add(corner);
            }
            face.Add(band);

            if (!string.IsNullOrWhiteSpace(text.Kind))
                face.Add(Text("tc-kind", text.Kind, Color.Lerp(ink, accent, 0.5f)));

            var bodyHeight = height - 20f - 64f - (string.IsNullOrWhiteSpace(text.Kind) ? 0f : 36f) - (string.IsNullOrWhiteSpace(text.Footer) ? 0f : 46f);
            if (art != null)
            {
                var picture = Box("tc-art");
                SetBorder(picture, accent);
                var st = request.ArtCell;
                var sprite = Sprite.Create(art, new Rect(st.z * art.width, st.w * art.height, st.x * art.width, st.y * art.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _sprites.Add(sprite);
                picture.style.backgroundImage = new StyleBackground(sprite);
                face.Add(picture);
                bodyHeight -= height * 0.42f + 8f;
            }

            var body = Text("tc-body", text.Body ?? "", ink);
            body.style.fontSize = BodySize(text.Body, bodyHeight);
            face.Add(body);

            if (!string.IsNullOrWhiteSpace(text.Footer))
            {
                var footer = Text("tc-footer", text.Footer, onAccent);
                footer.style.backgroundColor = accent;
                face.Add(footer);
            }
            return face;
        }

        private static VisualElement Back(CardStyle style)
        {
            var paper = Rgb(style?.BackPaper, CardStyle.DefaultBackPaper);
            var ink = Rgb(style?.BackInk, CardStyle.DefaultBackInk);
            var back = Box("tc-back");
            back.style.backgroundColor = paper;
            var frame = Box("tc-back-frame");
            SetBorder(frame, ink);
            back.Add(frame);
            var label = style?.BackLabel;
            if (!string.IsNullOrWhiteSpace(label))
            {
                var text = Text("tc-back-label", label, ink);
                text.style.fontSize = BackLabelSize(label);
                back.Add(text);
            }
            return back;
        }

        /// <summary>34 px for a short title, smaller for a long one (it may also wrap).</summary>
        public static float TitleSize(string title)
        {
            var length = Mathf.Max(1, title?.Length ?? 0);
            return length <= 16 ? 34f : Mathf.Max(22f, 34f * Mathf.Sqrt(16f / length));
        }

        /// <summary>
        /// The largest size at which <paramref name="body"/> fills about 85 % of a <paramref name="height"/>-pixel box
        /// 472 pixels wide (Inter: a character is about half its size wide, a line 1.3 times its size high), 16–40 px.
        /// A number or a word of a few letters (a quest's team size, a track's space) is printed as big as the card
        /// holds, up to 160 px, to read from across the table.
        /// </summary>
        public static float BodySize(string body, float height)
        {
            var lines = (body ?? "").Split('\n');
            var longest = lines.Max(l => l.Trim().Length);
            if (longest > 0 && longest <= 4 && lines.Length <= 2)
                return Mathf.Clamp(Mathf.Min(Mathf.Max(60f, height) * 0.7f / lines.Length, 472f / (0.62f * longest)), 40f, 160f);
            var length = Mathf.Max(1, body?.Length ?? 0);
            var size = Mathf.Sqrt(0.85f * Mathf.Max(60f, height) * 472f / (0.676f * length));
            return Mathf.Clamp(size, 16f, 40f);
        }

        /// <summary>
        /// Fits the back's longest word across the frame (bold capitals are about 0.78 of their size wide, plus the letter
        /// spacing): 84 px at most, 24 at least; longer labels wrap between words.
        /// </summary>
        public static float BackLabelSize(string label)
        {
            var longest = Mathf.Max(1, label.Split(' ').Max(w => w.Length));
            return Mathf.Clamp((400f - 4f * longest) / (longest * 0.78f), 24f, 84f);
        }

        /// <summary>Near-black or near-white, whichever reads on <paramref name="background"/>.</summary>
        public static Color Readable(Color background)
        {
            var luminance = 0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b;
            return luminance > 0.55f ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.98f, 0.97f, 0.94f);
        }

        private static VisualElement Box(string className)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList(className);
            return box;
        }

        private static Label Text(string className, string text, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            label.style.color = color;
            return label;
        }

        private static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
        }

        private static Color Rgb(float[] rgb, float[] fallback)
        {
            var c = rgb is { Length: >= 3 } ? rgb : fallback;
            return new Color(c[0], c[1], c[2], 1f);
        }
    }
}
