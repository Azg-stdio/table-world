using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Reading a PDF on screen (M7 T8.8): the page fills the window, ◀ ▶ (or the arrow and Page keys) turn pages for you
    /// alone, and "Show on the table" turns the PDF on the table to this page for everyone. Escape closes it.
    /// </summary>
    public sealed class PdfReader
    {
        private const string Hidden = "hidden";

        private readonly TableView _table;
        private readonly VisualElement _panel;
        private readonly VisualElement _page;
        private readonly Label _number;
        private string _objectId;
        private int _shown;

        public bool Visible => !_panel.ClassListContains(Hidden);

        /// <summary>The page on screen, from 0.</summary>
        public int Page => _shown;

        public PdfReader(VisualElement root, TableView table)
        {
            _table = table;
            _panel = new VisualElement { name = "pdfReader" };
            _panel.AddToClassList("pdf-reader");
            _panel.AddToClassList(Hidden);
            _page = new VisualElement { name = "pdfPage", pickingMode = PickingMode.Ignore };
            _page.AddToClassList("pdf-reader__page");
            _panel.Add(_page);
            var bar = new VisualElement();
            bar.AddToClassList("pdf-reader__bar");
            bar.Add(Button("◀", () => Turn(-1)));
            _number = new Label();
            _number.AddToClassList("pdf-reader__number");
            bar.Add(_number);
            bar.Add(Button("▶", () => Turn(1)));
            bar.Add(Button("Show on the table", ShowOnTable));
            bar.Add(Button("Close", Hide));
            _panel.Add(bar);
            root.Add(_panel);
        }

        private static Button Button(string text, System.Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("hud-button");
            return button;
        }

        /// <summary>Opens the PDF <paramref name="view"/> at the page it shows on the table.</summary>
        public void Show(ObjectView view)
        {
            if (!(view?.Model is Pdf pdf))
                return;
            _objectId = view.Id;
            _shown = pdf.Page;
            _panel.RemoveFromClassList(Hidden);
            Draw();
        }

        public void Hide()
        {
            _panel.AddToClassList(Hidden);
            _page.style.backgroundImage = StyleKeyword.None;
        }

        private Pdf Model => _objectId != null ? _table.Engine?.Find(_objectId) as Pdf : null;

        private int Pages => Model is { } pdf ? _table.Assets?.PdfDocument(pdf.Asset)?.PageCount ?? 0 : 0;

        public void Turn(int by)
        {
            var pages = Pages;
            if (pages == 0)
                return;
            _shown = Mathf.Clamp(_shown + by, 0, pages - 1);
            Draw();
        }

        private void ShowOnTable()
        {
            if (Model is { } pdf)
                _table.Execute(new SetPageCommand { ObjectId = pdf.Id, Page = _shown, Actor = _table.LocalSeat });
        }

        private void Draw()
        {
            var pdf = Model;
            if (pdf == null)
            {
                Hide();
                return;
            }
            var pages = Pages;
            _shown = Mathf.Clamp(_shown, 0, Mathf.Max(0, pages - 1));
            var texture = pages > 0 ? _table.Assets.PdfPage(pdf.Asset, _shown, AssetLibrary.PdfReaderPixels) : null;
            // The page drawn is a texture made at runtime: a value, not a style.
            _page.style.backgroundImage = texture != null ? new StyleBackground(texture) : StyleKeyword.None;
            var title = string.IsNullOrEmpty(pdf.Name) ? "" : pdf.Name + "  ·  ";
            _number.text = pages == 0 ? "The PDF is not here yet" : $"{title}page {_shown + 1 + pdf.PageOffset}  ({_shown + 1} of {pages})";
        }

        /// <summary>Arrow and Page keys turn pages while it is open (the table does not get them then); Escape closes it.</summary>
        public void Update()
        {
            if (!Visible)
                return;
            if (Model == null)
            {
                Hide();
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            // The HUD's own keys are off while it is open (it counts as typing), so Escape is handled here.
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame)
                Turn(1);
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame)
                Turn(-1);
        }
    }
}
