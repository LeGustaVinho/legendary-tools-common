#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using UnityEditor;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Registry for clipboard classifiers.
    /// - Extensible: additional classifiers can be registered at editor time.
    /// - Deterministic: classifiers are evaluated by descending priority.
    /// </summary>
    [InitializeOnLoad]
    public static class ClipboardClassifierRegistry
    {
        private sealed class ClassifierEntry
        {
            public int Priority;
            public IClipboardClassifier Classifier;
        }

        private static readonly List<ClassifierEntry> _classifiers = new(8);
        private static bool _initialized;

        static ClipboardClassifierRegistry()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            _classifiers.Clear();

            // Default classifiers (safe set).
            Register(new CSharpFileClipboardClassifier(), 100);
            Register(new GitPatchClipboardClassifier(), 50);

            SortByPriority();
        }

        /// <summary>
        /// Registers a classifier with a priority (higher runs first).
        /// </summary>
        public static void Register(IClipboardClassifier classifier, int priority = 0)
        {
            if (classifier == null)
                return;

            // Prevent duplicates by TypeId.
            for (int i = 0; i < _classifiers.Count; i++)
            {
                if (string.Equals(_classifiers[i].Classifier.TypeId, classifier.TypeId, StringComparison.Ordinal))
                {
                    _classifiers[i].Classifier = classifier;
                    _classifiers[i].Priority = priority;
                    SortByPriority();
                    return;
                }
            }

            _classifiers.Add(new ClassifierEntry
            {
                Classifier = classifier,
                Priority = priority
            });

            SortByPriority();
        }

        /// <summary>
        /// Clears the registry. Intended for tests or controlled reconfiguration.
        /// </summary>
        public static void Clear()
        {
            _classifiers.Clear();
        }

        /// <summary>
        /// Tries to classify the clipboard text.
        /// Returns false when no registered classifier accepts the text.
        /// </summary>
        public static bool TryClassify(string text, out ClipboardClassification classification)
        {
            EnsureInitialized();

            classification = null;

            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < _classifiers.Count; i++)
            {
                IClipboardClassifier c = _classifiers[i].Classifier;
                if (c == null)
                    continue;

                if (c.TryClassify(text, out classification) && classification != null)
                    return true;
            }

            return false;
        }

        private static void SortByPriority()
        {
            _classifiers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }
}
#endif