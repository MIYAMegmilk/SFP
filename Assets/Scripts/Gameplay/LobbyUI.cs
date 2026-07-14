using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public sealed class LobbyUI : MonoBehaviour
    {
        private GameObject _lobbyPanel;
        private Text _statusText;
        private InputField _ipInputField;
        private Button _hostButton;
        private Button _joinButton;
        private Button _disconnectButton;
        private bool _isVisible;

        private void Start()
        {
            CreateUI();
            _isVisible = false;
            _lobbyPanel.SetActive(false);
        }

        private void Update()
        {
            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                ToggleLobby();
            }

            UpdateStatus();
            UpdateControlVisibility();
            UpdateCursor();
        }

        private void CreateUI()
        {
            var canvasGO = new GameObject("LobbyCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            _lobbyPanel = new GameObject("LobbyPanel");
            _lobbyPanel.transform.SetParent(canvasGO.transform, false);
            var panelImage = _lobbyPanel.AddComponent<Image>();
            panelImage.color = new Color(0, 0, 0, 0.8f);
            var panelRect = _lobbyPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(_lobbyPanel.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(500, 600);
            var layoutGroup = contentGO.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.spacing = 15f;
            layoutGroup.padding = new RectOffset(30, 30, 30, 30);

            CreateTitle(contentGO.transform);
            CreateStatusText(contentGO.transform);
            CreateIPLabel(contentGO.transform);
            CreateIPInput(contentGO.transform);
            CreateHostButton(contentGO.transform);
            CreateJoinButton(contentGO.transform);
            CreateDisconnectButton(contentGO.transform);
        }

        private void CreateTitle(Transform parent)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = "SFP Multiplayer";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 60);
        }

        private void CreateStatusText(Transform parent)
        {
            var go = new GameObject("StatusText");
            go.transform.SetParent(parent, false);
            _statusText = go.AddComponent<Text>();
            _statusText.text = "Disconnected";
            _statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _statusText.fontSize = 16;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = Color.white;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 50);
        }

        private void CreateIPLabel(Transform parent)
        {
            var go = new GameObject("IPLabel");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = "Server IP:";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 30);
        }

        private void CreateIPInput(Transform parent)
        {
            var go = new GameObject("IPInput");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            _ipInputField = go.AddComponent<InputField>();
            _ipInputField.text = "127.0.0.1";

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "127.0.0.1";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);

            _ipInputField.textComponent = text;
            _ipInputField.targetGraphic = image;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 50);
        }

        private void CreateHostButton(Transform parent)
        {
            var go = new GameObject("HostButton");
            go.transform.SetParent(parent, false);
            _hostButton = CreateButtonComponent(go, "Host");
            _hostButton.onClick.AddListener(() => NetworkBootstrap.Instance.StartHost());
        }

        private void CreateJoinButton(Transform parent)
        {
            var go = new GameObject("JoinButton");
            go.transform.SetParent(parent, false);
            _joinButton = CreateButtonComponent(go, "Join");
            _joinButton.onClick.AddListener(() => NetworkBootstrap.Instance.StartClient(_ipInputField.text));
        }

        private void CreateDisconnectButton(Transform parent)
        {
            var go = new GameObject("DisconnectButton");
            go.transform.SetParent(parent, false);
            _disconnectButton = CreateButtonComponent(go, "Disconnect");
            _disconnectButton.onClick.AddListener(() => NetworkBootstrap.Instance.Shutdown());
        }

        private Button CreateButtonComponent(GameObject buttonGO, string label)
        {
            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.6f, 1f);

            var button = buttonGO.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.2f, 0.6f, 1f);
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.8f, 1f);
            colors.pressedColor = new Color(0.1f, 0.1f, 0.4f, 1f);
            button.colors = colors;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            buttonGO.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 60);
            return button;
        }

        private void ToggleLobby()
        {
            _isVisible = !_isVisible;
            _lobbyPanel.SetActive(_isVisible);
        }

        private void UpdateStatus()
        {
            if (_statusText == null || NetworkBootstrap.Instance == null) return;

            if (NetworkBootstrap.Instance.IsConnected)
            {
                if (NetworkBootstrap.Instance.IsHost)
                {
                    _statusText.text = "Hosting (Server + Client)";
                }
                else if (NetworkBootstrap.Instance.IsClient)
                {
                    _statusText.text = "Connected as Client";
                }
            }
            else
            {
                _statusText.text = "Disconnected";
            }
        }

        private void UpdateControlVisibility()
        {
            if (_hostButton == null || NetworkBootstrap.Instance == null) return;

            bool isConnected = NetworkBootstrap.Instance.IsConnected;
            _hostButton.gameObject.SetActive(!isConnected);
            _joinButton.gameObject.SetActive(!isConnected);
            _ipInputField.gameObject.SetActive(!isConnected);
            _disconnectButton.gameObject.SetActive(isConnected);
        }

        private void UpdateCursor()
        {
            if (_isVisible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
