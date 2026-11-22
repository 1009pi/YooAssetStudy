using UnityEngine;
using UnityEngine.UI;

namespace Game.AOT
{
    /// <summary>
    /// 启动界面：进度条 + 失败弹窗。只订阅 BootFlow 事件，不控制流程。
    /// </summary>
    public class BootUI : MonoBehaviour
    {
        private GameObject _canvasGo;
        private Text _tip;
        private Text _percent;
        private RectTransform _barFill;

        private GameObject _dialog;
        private Text _dialogText;
        private Button _skipButton;

        private void Awake()
        {
            BuildUI();

            BootFlow.ProgressChanged += OnProgress;
            BootFlow.Failed += OnFailed;
            BootFlow.Finished += OnFinished;
        }

        private void OnDestroy()
        {
            BootFlow.ProgressChanged -= OnProgress;
            BootFlow.Failed -= OnFailed;
            BootFlow.Finished -= OnFinished;
        }

        private void OnProgress(BootProgress progress)
        {
            if (_tip != null) _tip.text = progress.Message;
            if (_percent != null) _percent.text = $"{progress.Value:P0}";
            if (_barFill != null) _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress.Value), 1f);
        }

        private void OnFailed(BootFailure failure)
        {
            _dialogText.text = failure.Message;
            _skipButton.gameObject.SetActive(failure.CanSkipUpdate);
            _dialog.SetActive(true);
        }

        private void OnFinished(bool success)
        {
            if (success)
            {
                if (_canvasGo != null) Destroy(_canvasGo);
                Destroy(this);
                return;
            }

            _dialogText.text = "启动失败，请检查网络或稍后重试";
            _skipButton.gameObject.SetActive(false);
            _dialog.SetActive(true);
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("BootCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvasGo = canvasGo;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Font font = CreateRuntimeFont();

            Image background = CreateImage("Background", canvasGo.transform,
                new Color(0.05f, 0.06f, 0.08f, 1f));
            Stretch(background.rectTransform);

            _tip = CreateText("Tip", canvasGo.transform, font, 40);
            SetRect(_tip.rectTransform, new Vector2(1400f, 100f), new Vector2(0f, 120f));

            Image barBg = CreateImage("BarBg", canvasGo.transform, new Color(1f, 1f, 1f, 0.12f));
            SetRect(barBg.rectTransform, new Vector2(1000f, 28f), Vector2.zero);

            Image fill = CreateImage("BarFill", barBg.transform, new Color(0.25f, 0.6f, 1f, 1f));
            _barFill = fill.rectTransform;
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;

            _percent = CreateText("Percent", canvasGo.transform, font, 32);
            SetRect(_percent.rectTransform, new Vector2(400f, 60f), new Vector2(0f, -70f));

            BuildDialog(canvasGo.transform, font);
        }

        private void BuildDialog(Transform parent, Font font)
        {
            Image panel = CreateImage("Dialog", parent, new Color(0.1f, 0.12f, 0.15f, 0.98f));
            panel.raycastTarget = true;
            SetRect(panel.rectTransform, new Vector2(760f, 340f), Vector2.zero);
            _dialog = panel.gameObject;

            _dialogText = CreateText("DialogText", panel.transform, font, 30);
            SetRect(_dialogText.rectTransform, new Vector2(680f, 200f), new Vector2(0f, 55f));

            CreateButton("Retry", panel.transform, font, "重试", new Vector2(-220f, -105f), () =>
            {
                _dialog.SetActive(false);
                BootFlow.Answer(BootDecision.Retry);
            });

            _skipButton = CreateButton("Skip", panel.transform, font, "跳过更新", new Vector2(0f, -105f), () =>
            {
                _dialog.SetActive(false);
                BootFlow.Answer(BootDecision.SkipUpdate);
            });

            CreateButton("Quit", panel.transform, font, "退出", new Vector2(220f, -105f), Quit);

            _dialog.SetActive(false);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #region UI 工具
        private static Font CreateRuntimeFont()
        {
            string[] candidates = { "Microsoft YaHei", "微软雅黑", "SimHei", "PingFang SC", "Arial" };
            foreach (string name in candidates)
            {
                Font font = Font.CreateDynamicFontFromOSFont(name, 20);
                if (font != null) return font;
            }
            return null;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, Font font, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, Font font,
            string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            Image image = CreateImage(name, parent, new Color(0.2f, 0.45f, 0.8f, 1f));
            image.raycastTarget = true;
            SetRect(image.rectTransform, new Vector2(190f, 70f), position);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", image.transform, font, 28);
            Stretch(text.rectTransform);
            text.text = label;
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        #endregion
    }
}