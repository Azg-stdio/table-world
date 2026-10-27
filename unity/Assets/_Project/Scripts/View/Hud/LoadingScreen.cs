using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Covers the table while a game opens: what is happening now (reading, placing the pieces, starting the scripts) and
    /// then the images and downloads still on their way, with a bar. It leaves when everything is in, or when the player
    /// chooses to play anyway (offered after <see cref="SkipAfterSeconds"/>; the rest keeps arriving on the table).
    /// </summary>
    public sealed class LoadingScreen
    {
        public const float SkipAfterSeconds = 5f;
        private const string Hidden = "hidden";

        private readonly TableView _table;
        private readonly VisualElement _screen;
        private readonly Label _title;
        private readonly Label _step;
        private readonly Label _detail;
        private readonly ProgressBar _bar;
        private readonly Button _skip;
        private bool _placing;
        private float _shownAt;

        public LoadingScreen(VisualElement root, TableView table)
        {
            _table = table;
            _screen = root.Q("loadingScreen");
            _title = root.Q<Label>("loadingTitle");
            _step = root.Q<Label>("loadingStep");
            _detail = root.Q<Label>("loadingDetail");
            _bar = root.Q<ProgressBar>("loadingBar");
            _skip = root.Q<Button>("loadingSkip");
            _skip.clicked += Hide;
            _table.LoadStepped += OnStep;
        }

        public bool Visible { get; private set; }

        private void OnStep(string title, string step)
        {
            if (step == null)
            {
                // Placed and running: what is left is the images, watched in Update.
                _placing = false;
                if (!_table.LastLoadOk)
                    Hide();
                return;
            }
            if (!Visible)
            {
                Visible = true;
                _shownAt = Time.unscaledTime;
                _title.text = title;
                _detail.text = "";
                _bar.value = 0f;
                _skip.AddToClassList(Hidden);
                _screen.RemoveFromClassList(Hidden);
            }
            _placing = true;
            _step.text = step;
            // The blocking steps take roughly these shares of a heavy table's opening.
            _bar.value = Mathf.Max(_bar.value, step == Ui.T("Reading the table") ? 0.05f : step == Ui.T("Starting the game's scripts") ? 0.35f : 0.15f);
        }

        public void Update()
        {
            if (!Visible || _placing)
                return;
            var (imagesLeft, images, downloadsLeft, downloads) = _table.Assets?.Progress ?? default;
            var left = imagesLeft + downloadsLeft;
            var total = images + downloads;
            if (left == 0)
            {
                Hide();
                return;
            }
            _step.text = downloadsLeft > 0 ? Ui.T("Downloading files") : Ui.T("Preparing images");
            _detail.text = Ui.T("Images {value} of {images}", ("value", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", images - imagesLeft)), ("images", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", images))) + (downloads > 0 ? Ui.T(", downloads {value} of {downloads}", ("value", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", downloads - downloadsLeft)), ("downloads", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", downloads))) : "");
            _bar.value = Mathf.Max(_bar.value, 0.4f + 0.6f * (total - left) / Mathf.Max(1, total));
            if (Time.unscaledTime - _shownAt > SkipAfterSeconds)
                _skip.RemoveFromClassList(Hidden);
        }

        private void Hide()
        {
            Visible = false;
            _placing = false;
            _screen.AddToClassList(Hidden);
        }
    }
}
