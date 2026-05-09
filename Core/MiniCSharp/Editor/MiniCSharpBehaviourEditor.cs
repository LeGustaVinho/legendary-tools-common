using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.MiniCSharp.Editor
{
    [CustomEditor(typeof(MiniCSharpBehaviour))]
    internal sealed class MiniCSharpBehaviourEditor : UnityEditor.Editor
    {
        private const float SourceHeight = 220f;
        private const float HighlightHeight = 220f;

        private SerializedProperty _sourceProperty;
        private SerializedProperty _disableComponentOnScriptErrorProperty;
        private SerializedProperty _objectBindingsProperty;
        private Vector2 _sourceScroll;
        private Vector2 _highlightScroll;
        private bool _showHighlightPreview = true;
        private string _lastValidatedSource;
        private bool _lastIsValid;
        private string _lastValidationMessage;
        private string _lastHighlightedSource;
        private GUIStyle _codeEditorStyle;
        private GUIStyle _highlightStyle;

        private void OnEnable()
        {
            _sourceProperty = serializedObject.FindProperty("_source");
            _disableComponentOnScriptErrorProperty = serializedObject.FindProperty("_disableComponentOnScriptError");
            _objectBindingsProperty = serializedObject.FindProperty("_objectBindings");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();

            serializedObject.Update();
            DrawScriptReference();
            DrawSettings();
            DrawSourceEditor();
            DrawValidationResult();
            DrawHighlightPreview();
            DrawRuntimeActions();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptReference()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromMonoBehaviour((MonoBehaviour)target);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.PropertyField(_disableComponentOnScriptErrorProperty);
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(_objectBindingsProperty, new GUIContent("Object Bindings"), true);
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "Unity callbacks are forwarded to MiniCSharp local functions when they exist. " +
                "The script also receives `this`, `gameObject`, `transform`, `monoBehaviour` and `behaviour` in its context. " +
                "Each binding name below becomes a global MiniCSharp variable that points to the assigned UnityObject.",
                MessageType.Info);
        }

        private void DrawSourceEditor()
        {
            EditorGUILayout.LabelField("Mini CSharp Script", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _sourceScroll = EditorGUILayout.BeginScrollView(_sourceScroll, GUILayout.Height(SourceHeight));
            string updatedSource = EditorGUILayout.TextArea(_sourceProperty.stringValue ?? string.Empty, _codeEditorStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                _sourceProperty.stringValue = updatedSource;
                InvalidateCaches();
            }
        }

        private void DrawValidationResult()
        {
            string source = _sourceProperty.stringValue ?? string.Empty;
            ValidateSource(source);

            MessageType messageType = _lastIsValid ? MessageType.Info : MessageType.Error;
            string prefix = _lastIsValid ? "Syntax OK" : "Syntax Error";
            EditorGUILayout.HelpBox($"{prefix}: {_lastValidationMessage}", messageType);
        }

        private void DrawHighlightPreview()
        {
            EditorGUILayout.Space(4f);
            _showHighlightPreview = EditorGUILayout.Foldout(_showHighlightPreview, "Syntax Highlight Preview", true);

            if (!_showHighlightPreview)
            {
                return;
            }

            string source = _sourceProperty.stringValue ?? string.Empty;
            string highlighted = BuildHighlightedRichText(source);

            _highlightScroll = EditorGUILayout.BeginScrollView(_highlightScroll, GUILayout.Height(HighlightHeight));
            float width = Mathf.Max(EditorGUIUtility.currentViewWidth - 50f, 100f);
            float height = Mathf.Max(_highlightStyle.CalcHeight(new GUIContent(highlighted), width), HighlightHeight - 24f);
            Rect rect = GUILayoutUtility.GetRect(width, height, _highlightStyle, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.13f, 0.13f, 0.13f)
                : new Color(0.94f, 0.94f, 0.94f));
            GUI.Label(rect, highlighted, _highlightStyle);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeActions()
        {
            MiniCSharpBehaviour behaviour = (MiniCSharpBehaviour)target;

            using (new EditorGUI.DisabledScope(Application.isPlaying == false))
            {
                if (GUILayout.Button("Reload Script Runtime State"))
                {
                    behaviour.ReloadScript();
                }
            }
        }

        private void ValidateSource(string source)
        {
            if (string.Equals(_lastValidatedSource, source, StringComparison.Ordinal))
            {
                return;
            }

            MiniCSharpInterpreter interpreter = new MiniCSharpInterpreter();
            MiniCSharpBehaviour.RegisterCommonUnityTypes(interpreter);

            if (interpreter.TryCompile(source, out _, out string errorMessage))
            {
                _lastValidatedSource = source;
                _lastIsValid = true;
                _lastValidationMessage = "The script compiled successfully.";
                return;
            }

            _lastValidatedSource = source;
            _lastIsValid = false;
            _lastValidationMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "Unknown MiniCSharp parsing error."
                : errorMessage;
        }

        private void InvalidateCaches()
        {
            _lastValidatedSource = null;
            _lastHighlightedSource = null;
        }

        private void EnsureStyles()
        {
            if (_codeEditorStyle == null)
            {
                _codeEditorStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false,
                    richText = false
                };
            }

            if (_highlightStyle == null)
            {
                _highlightStyle = new GUIStyle(EditorStyles.textArea)
                {
                    richText = true,
                    wordWrap = false,
                    padding = new RectOffset(8, 8, 8, 8)
                };
            }
        }

        private string BuildHighlightedRichText(string source)
        {
            if (string.Equals(_lastHighlightedSource, source, StringComparison.Ordinal))
            {
                return _cachedHighlight;
            }

            _lastHighlightedSource = source;
            _cachedHighlight = MiniCSharpRichTextHighlighter.Highlight(source);
            return _cachedHighlight;
        }

        private string _cachedHighlight;
    }

    internal static class MiniCSharpRichTextHighlighter
    {
        private const string KeywordColor = "#569CD6";
        private const string TypeColor = "#4EC9B0";
        private const string StringColor = "#CE9178";
        private const string NumberColor = "#B5CEA8";
        private const string CommentColor = "#6A9955";
        private const string OperatorColor = "#D4D4D4";
        private const string ContextColor = "#9CDCFE";

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "else", "try", "catch", "finally", "switch", "case", "default", "for", "foreach", "in", "while", "break", "continue", "return", "yield",
            "var", "new", "is", "as", "typeof", "true", "false", "null"
        };

        private static readonly HashSet<string> BuiltInTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "void", "bool", "byte", "short", "int", "long", "float", "double", "decimal", "string", "object", "Type", "IEnumerator"
        };

        private static readonly HashSet<string> ContextNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "this", "monoBehaviour", "behaviour", "gameObject", "transform"
        };

        public static string Highlight(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(source.Length * 2);
            int index = 0;

            while (index < source.Length)
            {
                char current = source[index];

                if (TryReadLineComment(source, ref index, out string lineComment))
                {
                    AppendColored(builder, lineComment, CommentColor);
                    continue;
                }

                if (TryReadBlockComment(source, ref index, out string blockComment))
                {
                    AppendColored(builder, blockComment, CommentColor);
                    continue;
                }

                if (TryReadString(source, ref index, out string literalString))
                {
                    AppendColored(builder, literalString, StringColor);
                    continue;
                }

                if (char.IsDigit(current))
                {
                    string number = ReadNumber(source, ref index);
                    AppendColored(builder, number, NumberColor);
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    string identifier = ReadIdentifier(source, ref index);

                    if (Keywords.Contains(identifier))
                    {
                        AppendColored(builder, identifier, KeywordColor);
                    }
                    else if (BuiltInTypes.Contains(identifier))
                    {
                        AppendColored(builder, identifier, TypeColor);
                    }
                    else if (ContextNames.Contains(identifier))
                    {
                        AppendColored(builder, identifier, ContextColor);
                    }
                    else if (char.IsUpper(identifier[0]))
                    {
                        AppendColored(builder, identifier, TypeColor);
                    }
                    else
                    {
                        builder.Append(EscapeRichText(identifier));
                    }

                    continue;
                }

                if (IsOperatorCharacter(current))
                {
                    builder.Append(WrapColor(EscapeRichText(current.ToString()), OperatorColor));
                    index++;
                    continue;
                }

                builder.Append(EscapeRichText(current.ToString()));
                index++;
            }

            return builder.ToString();
        }

        private static bool TryReadLineComment(string source, ref int index, out string comment)
        {
            comment = null;

            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '/')
            {
                return false;
            }

            int start = index;
            index += 2;

            while (index < source.Length && source[index] != '\n')
            {
                index++;
            }

            comment = source.Substring(start, index - start);
            return true;
        }

        private static bool TryReadBlockComment(string source, ref int index, out string comment)
        {
            comment = null;

            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '*')
            {
                return false;
            }

            int start = index;
            index += 2;

            while (index + 1 < source.Length)
            {
                if (source[index] == '*' && source[index + 1] == '/')
                {
                    index += 2;
                    comment = source.Substring(start, index - start);
                    return true;
                }

                index++;
            }

            index = source.Length;
            comment = source.Substring(start);
            return true;
        }

        private static bool TryReadString(string source, ref int index, out string value)
        {
            value = null;

            int start = index;
            bool interpolated = false;

            if (source[index] == '$' && index + 1 < source.Length && source[index + 1] == '"')
            {
                interpolated = true;
                index++;
            }

            if (source[index] != '"')
            {
                if (interpolated)
                {
                    index = start;
                }

                return false;
            }

            index++;
            bool escaping = false;

            while (index < source.Length)
            {
                char current = source[index++];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (current == '"')
                {
                    value = source.Substring(start, index - start);
                    return true;
                }
            }

            value = source.Substring(start);
            return true;
        }

        private static string ReadNumber(string source, ref int index)
        {
            int start = index;

            while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.'))
            {
                index++;
            }

            if (index < source.Length && char.IsLetter(source[index]))
            {
                index++;
            }

            return source.Substring(start, index - start);
        }

        private static string ReadIdentifier(string source, ref int index)
        {
            int start = index;

            while (index < source.Length && IsIdentifierPart(source[index]))
            {
                index++;
            }

            return source.Substring(start, index - start);
        }

        private static bool IsIdentifierStart(char character)
        {
            return char.IsLetter(character) || character == '_';
        }

        private static bool IsIdentifierPart(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_';
        }

        private static bool IsOperatorCharacter(char character)
        {
            switch (character)
            {
                case '+':
                case '-':
                case '*':
                case '/':
                case '%':
                case '=':
                case '!':
                case '<':
                case '>':
                case '?':
                case ':':
                case '.':
                case ',':
                case ';':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                    return true;

                default:
                    return false;
            }
        }

        private static void AppendColored(StringBuilder builder, string content, string color)
        {
            builder.Append(WrapColor(EscapeRichText(content), color));
        }

        private static string WrapColor(string content, string color)
        {
            return $"<color={color}>{content}</color>";
        }

        private static string EscapeRichText(string content)
        {
            return content
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
