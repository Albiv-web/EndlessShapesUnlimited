using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum DecorationEditorTool
    {
        Select,
        Move,
        Anchor,
        Mesh,
        Surface,
        Paint,
        Focus,
        Rotate,
        Scale
    }

    internal enum DecorationEditorViewMode
    {
        Normal,
        Mixed,
        Wireframe,
        DecorationOnly,
        Mass,
        Drag,
        Cost,
        Surface,
        Important
    }

    internal enum DecorationSelectionMode
    {
        Single,
        Box
    }

    internal enum DecorationTransformOrientation
    {
        Global,
        Local
    }

    internal enum DecorationOutlinerRowKind
    {
        Construct,
        Tether,
        Decoration
    }

    internal static class DecorationEditorTheme
    {
        internal static readonly Color Cyan = new Color(0.05f, 0.9f, 1f, 1f);
        internal static readonly Color PanelColor = new Color(0.01f, 0.07f, 0.09f, 0.9f);
        internal static readonly Color PanelHeaderColor = new Color(0.025f, 0.24f, 0.3f, 0.96f);
        internal static readonly Color RowColor = new Color(0f, 0.18f, 0.23f, 0.74f);
        internal static readonly Color SelectedRowColor = new Color(0f, 0.42f, 0.5f, 0.94f);
        internal static readonly Color WarningColor = new Color(1f, 0.72f, 0.2f, 1f);
        internal static readonly Color ErrorColor = new Color(1f, 0.25f, 0.2f, 1f);

        private static bool _ready;
        private static Texture2D _panel;
        private static Texture2D _header;
        private static Texture2D _row;
        private static Texture2D _rowSelected;
        private static Texture2D _button;
        private static Texture2D _buttonHover;
        private static Texture2D _buttonActive;
        private static Texture2D _buttonDisabled;
        private static Texture2D _field;
        private static Texture2D _fieldFocused;
        private static Texture2D _dimLight;
        private static Texture2D _dim;
        private static Texture2D _dimStrong;
        private static Texture2D _separator;
        private static float _styleScale = -1f;

        internal static GUIStyle Panel { get; private set; }
        internal static GUIStyle Header { get; private set; }
        internal static GUIStyle SubHeader { get; private set; }
        internal static GUIStyle CompactHeaderText { get; private set; }
        internal static GUIStyle SectionHeader { get; private set; }
        internal static GUIStyle Badge { get; private set; }
        internal static GUIStyle Body { get; private set; }
        internal static GUIStyle BodyWrap { get; private set; }
        internal static GUIStyle Mini { get; private set; }
        internal static GUIStyle MiniWrap { get; private set; }
        internal static GUIStyle Row { get; private set; }
        internal static GUIStyle RowSelected { get; private set; }
        internal static GUIStyle Button { get; private set; }
        internal static GUIStyle ActiveButton { get; private set; }
        internal static GUIStyle DisabledButton { get; private set; }
        internal static GUIStyle DialogHeader { get; private set; }
        internal static GUIStyle DialogTitle { get; private set; }
        internal static GUIStyle DialogBody { get; private set; }
        internal static GUIStyle DialogWarning { get; private set; }
        internal static GUIStyle DialogToggle { get; private set; }
        internal static GUIStyle DialogToggleSelected { get; private set; }
        internal static GUIStyle DialogButton { get; private set; }
        internal static GUIStyle DialogActiveButton { get; private set; }
        internal static GUIStyle TextField { get; private set; }
        internal static GUIStyle Status { get; private set; }
        internal static GUIStyle Warning { get; private set; }
        internal static GUIStyle Error { get; private set; }
        internal static Texture2D DimTexture => _dim;

        internal static Texture2D DimTextureFor(DecorationEditorViewMode mode)
        {
            switch (mode)
            {
                case DecorationEditorViewMode.Mixed:
                    return _dim;
                case DecorationEditorViewMode.Wireframe:
                    return _dimLight;
                case DecorationEditorViewMode.DecorationOnly:
                    return _dimStrong;
                case DecorationEditorViewMode.Mass:
                case DecorationEditorViewMode.Drag:
                case DecorationEditorViewMode.Cost:
                case DecorationEditorViewMode.Surface:
                case DecorationEditorViewMode.Important:
                    return _dimLight;
                default:
                    return null;
            }
        }

        internal static void Ensure()
        {
            Ensure(EsuHudLayout.CurrentScale);
        }

        internal static void Ensure(float scale)
        {
            scale = Mathf.Clamp(scale, EsuHudLayout.MinEffectiveScale, EsuHudLayout.MaxEffectiveScale);
            if (_ready && Mathf.Abs(_styleScale - scale) < 0.001f)
                return;

            if (_panel == null || _buttonHover == null || _fieldFocused == null)
            {
                _panel = Solid(PanelColor, "ESU panel");
                _header = Solid(PanelHeaderColor, "ESU header");
                _row = Solid(RowColor, "ESU row");
                _rowSelected = Solid(SelectedRowColor, "ESU selected row");
                _button = Solid(new Color(0f, 0.2f, 0.26f, 0.94f), "ESU button");
                _buttonHover = Solid(new Color(0f, 0.3f, 0.37f, 0.98f), "ESU button hover");
                _buttonActive = Solid(new Color(0f, 0.42f, 0.49f, 0.98f), "ESU button active");
                _buttonDisabled = Solid(new Color(0.08f, 0.1f, 0.11f, 0.7f), "ESU button disabled");
                _field = Solid(new Color(0f, 0.1f, 0.13f, 0.95f), "ESU field");
                _fieldFocused = Solid(new Color(0f, 0.2f, 0.25f, 0.98f), "ESU field focused");
                _dimLight = Solid(new Color(0f, 0f, 0f, 0.14f), "ESU focus dim light");
                _dim = Solid(new Color(0f, 0f, 0f, 0.28f), "ESU focus dim");
                _dimStrong = Solid(new Color(0f, 0f, 0f, 0.55f), "ESU decoration-only dim");
                _separator = Solid(Cyan, "ESU separator");
            }

            Panel = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panel, textColor = Color.white },
                padding = EsuHudLayout.Offset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
                border = EsuHudLayout.Offset(1, 1, 1, 1)
            };
            Header = new GUIStyle(GUI.skin.label)
            {
                normal = { background = _header, textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = EsuHudLayout.FontSize(15),
                padding = EsuHudLayout.Offset(6, 6, 3, 3)
            };
            SubHeader = new GUIStyle(Header)
            {
                fontSize = EsuHudLayout.FontSize(12),
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            CompactHeaderText = new GUIStyle(SubHeader)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            CompactHeaderText.normal.background = null;
            SectionHeader = new GUIStyle(SubHeader)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = EsuHudLayout.FontSize(11),
                padding = EsuHudLayout.Offset(7, 7, 2, 2)
            };
            Badge = new GUIStyle(GUI.skin.label)
            {
                normal = { background = _rowSelected, textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = EsuHudLayout.FontSize(10),
                padding = EsuHudLayout.Offset(5, 5, 1, 1),
                margin = new RectOffset(0, 0, 0, 0)
            };
            Body = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                wordWrap = false,
                fontSize = EsuHudLayout.FontSize(12),
                padding = EsuHudLayout.Offset(3, 3, 2, 2)
            };
            BodyWrap = new GUIStyle(Body)
            {
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            Mini = new GUIStyle(Body)
            {
                fontSize = EsuHudLayout.FontSize(10),
                normal = { textColor = new Color(0.82f, 0.95f, 1f, 1f) }
            };
            MiniWrap = new GUIStyle(Mini)
            {
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            Row = new GUIStyle(GUI.skin.button)
            {
                normal = { background = _row, textColor = Color.white },
                hover = { background = _buttonHover, textColor = Color.white },
                active = { background = _buttonActive, textColor = Color.white },
                focused = { background = _buttonHover, textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                fontSize = EsuHudLayout.FontSize(11),
                padding = EsuHudLayout.Offset(6, 6, 2, 2),
                margin = new RectOffset(0, 0, EsuHudLayout.Pixel(1), EsuHudLayout.Pixel(1))
            };
            RowSelected = new GUIStyle(Row)
            {
                normal = { background = _rowSelected, textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
            Button = new GUIStyle(GUI.skin.button)
            {
                normal = { background = _button, textColor = Color.white },
                hover = { background = _buttonHover, textColor = Color.white },
                active = { background = _buttonActive, textColor = Color.white },
                focused = { background = _buttonHover, textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = EsuHudLayout.FontSize(11),
                padding = EsuHudLayout.Offset(4, 4, 3, 3),
                margin = EsuHudLayout.Offset(2, 2, 0, 0),
                imagePosition = ImagePosition.ImageAbove
            };
            ActiveButton = new GUIStyle(Button)
            {
                normal = { background = _buttonActive, textColor = Color.white },
                hover = { background = _rowSelected, textColor = Color.white },
                focused = { background = _rowSelected, textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
            DisabledButton = new GUIStyle(Button)
            {
                normal = { background = _buttonDisabled, textColor = new Color(0.55f, 0.65f, 0.68f, 1f) },
                hover = { background = _buttonDisabled, textColor = new Color(0.55f, 0.65f, 0.68f, 1f) },
                active = { background = _buttonDisabled, textColor = new Color(0.55f, 0.65f, 0.68f, 1f) }
            };
            DialogHeader = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _header, textColor = Color.white },
                padding = EsuHudLayout.Offset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0)
            };
            DialogTitle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fontSize = EsuHudLayout.FontSize(14),
                padding = EsuHudLayout.Offset(8, 8, 3, 3),
                imagePosition = ImagePosition.ImageLeft
            };
            DialogBody = new GUIStyle(BodyWrap)
            {
                fontSize = EsuHudLayout.FontSize(12),
                padding = EsuHudLayout.Offset(4, 4, 2, 2)
            };
            DialogWarning = new GUIStyle(DialogBody)
            {
                normal = { textColor = WarningColor },
                fontStyle = FontStyle.Bold
            };
            DialogToggle = new GUIStyle(Row)
            {
                imagePosition = ImagePosition.TextOnly,
                fontSize = EsuHudLayout.FontSize(11),
                padding = EsuHudLayout.Offset(8, 8, 3, 3)
            };
            DialogToggleSelected = new GUIStyle(DialogToggle)
            {
                normal = { background = _rowSelected, textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
            DialogButton = new GUIStyle(Button)
            {
                imagePosition = ImagePosition.ImageLeft,
                fontStyle = FontStyle.Normal,
                padding = EsuHudLayout.Offset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0)
            };
            DialogActiveButton = new GUIStyle(DialogButton)
            {
                normal = { background = _buttonActive, textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
            TextField = new GUIStyle(GUI.skin.textField)
            {
                normal = { background = _field, textColor = Color.white },
                hover = { background = _fieldFocused, textColor = Color.white },
                focused = { background = _fieldFocused, textColor = Color.white },
                padding = EsuHudLayout.Offset(4, 4, 2, 2),
                fontSize = EsuHudLayout.FontSize(12)
            };
            Status = new GUIStyle(Body)
            {
                normal = { background = _panel, textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                padding = EsuHudLayout.Offset(8, 8, 4, 4)
            };
            Warning = new GUIStyle(Body)
            {
                normal = { textColor = WarningColor },
                fontStyle = FontStyle.Bold
            };
            Error = new GUIStyle(Body)
            {
                normal = { textColor = ErrorColor },
                fontStyle = FontStyle.Bold
            };

            _styleScale = scale;
            _ready = true;
        }

        internal static GUIStyle ToolButton(bool active, bool enabled = true)
        {
            if (!enabled)
                return DisabledButton;
            return active ? ActiveButton : Button;
        }

        internal static void Separator()
        {
            float thickness = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            Rect rect = GUILayoutUtility.GetRect(thickness, thickness, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, _separator);
        }

        private static Texture2D Solid(Color color, string name)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }
    }
}
